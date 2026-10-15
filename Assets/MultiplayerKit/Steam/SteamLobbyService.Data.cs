// Steam lobby, part 2 of 2: owner-only mutators (privacy, joinable, member limit, metadata), member data, lobby chat,
// the Members snapshot, k.owner/k.count upkeep and Flush, which raises the coalesced events once per frame
// (LobbyDataChanged, MemberDataChanged, MembersChanged, then OwnerChanged). Design: 01 §4.1, 03 §3-§6, §7, §8.1,
// 06 S5 (plus the S4 mutators, moved here for the size rule).
// Password rule (03 §4, §15): SetLobbyData is only called from WriteData (SetData/SetDataBatch, whose callers pass
// HostSettings.ToLobbyData output without HostOnly keys) and from the k.owner/k.count writers below. Values are
// never logged. ChatReceived is raised straight from LobbyChatMsg_t, never coalesced.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Steamworks;

namespace MultiplayerKit.Steam
{
    public sealed partial class SteamLobbyService
    {
        private const int MaxKeyBytes = 255;
        private const int MaxValueBytes = 8192;
        private const int MaxChatBytes = 2048;
        private const int ChatBufferBytes = 4096;
        private const int KeyBufferChars = 256;
        private const int ValueBufferChars = 8192;
        private const string UnknownPersona = "[unknown]";

        private static readonly IReadOnlyList<LobbyMember> NoMembers = new LobbyMember[0];

        // Written first in a batch so a racing browser never sees a half-written lobby as compatible (03 §4).
        private static readonly string[] CompatibilityKeys = { KitKeys.Game, KitKeys.Proto, KitKeys.Version };

        private readonly byte[] chatBuffer = new byte[ChatBufferBytes];
        private readonly HashSet<ulong> personaRequested = new HashSet<ulong>();
        private readonly List<PlayerId> dirtyMemberData = new List<PlayerId>();
        private IReadOnlyList<LobbyMember> members = NoMembers;
        private bool lobbyDataDirty;
        private bool membersDirty;
        private bool ownerCheckPending;
        private int writtenCount = -1;

        /// <summary>Owner first, then Steam order. A fresh list per rebuild; empty when not in a lobby.</summary>
        public IReadOnlyList<LobbyMember> Members => members;

        public event Action<IReadOnlyList<LobbyMember>> MembersChanged;
        public event Action LobbyDataChanged;
        public event Action<PlayerId> MemberDataChanged;
        public event Action<PlayerId, string> ChatReceived;

        /// <summary>03 §3.1. Legal in any phase; SessionController writes s.privacy in the same metadata batch.</summary>
        public bool SetPrivacy(LobbyPrivacy privacy)
        {
            if (!RequireOwner("SetPrivacy")) return false;
            bool ok = SteamMatchmaking.SetLobbyType(SteamIds.ToSteam(current), ToLobbyType(privacy));
            if (!ok) KitLog.Warning("SetLobbyType(" + privacy + ") failed.");
            return ok;
        }

        /// <summary>03 §3.2: SessionController owns the value (phase and joinInProgress).</summary>
        public bool SetJoinable(bool joinable)
        {
            if (!RequireOwner("SetJoinable")) return false;
            bool ok = SteamMatchmaking.SetLobbyJoinable(SteamIds.ToSteam(current), joinable);
            if (!ok) KitLog.Warning("SetLobbyJoinable(" + joinable + ") failed.");
            return ok;
        }

        /// <summary>Clamped to 1..250. Steam does not evict members when the limit drops below the count.</summary>
        public bool SetMaxMembers(int maxMembers)
        {
            if (!RequireOwner("SetMaxMembers")) return false;
            int max = Math.Max(1, Math.Min(SteamMaxMembers, maxMembers));
            bool ok = SteamMatchmaking.SetLobbyMemberLimit(SteamIds.ToSteam(current), max);
            if (!ok) KitLog.Warning("SetLobbyMemberLimit(" + max + ") failed.");
            return ok;
        }

        public bool SetData(string key, string value)
        {
            if (!RequireOwner("SetData") || !IsValidKey(key)) return false;
            bool ok = WriteData(SteamIds.ToSteam(current), key, value);
            lobbyDataDirty = true;
            return ok;
        }

        /// <summary>Writes every pair (compatibility keys first); one LobbyDataChanged at the next Flush.</summary>
        public bool SetDataBatch(IReadOnlyDictionary<string, string> pairs)
        {
            if (pairs == null || !RequireOwner("SetDataBatch")) return false;
            CSteamID lobby = SteamIds.ToSteam(current);
            bool ok = true;
            foreach (string key in CompatibilityKeys)
            {
                if (pairs.TryGetValue(key, out string value)) ok &= WriteData(lobby, key, value);
            }
            foreach (KeyValuePair<string, string> pair in pairs)
            {
                if (Array.IndexOf(CompatibilityKeys, pair.Key) >= 0) continue;
                ok &= IsValidKey(pair.Key) && WriteData(lobby, pair.Key, pair.Value);
            }
            lobbyDataDirty = true;
            return ok;
        }

        public string GetData(string key)
        {
            if (!InLobby || string.IsNullOrEmpty(key) || !SteamPlatform.IsInitialized) return "";
            return SteamMatchmaking.GetLobbyData(SteamIds.ToSteam(current), key) ?? "";
        }

        public IReadOnlyDictionary<string, string> GetAllData()
        {
            var all = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!InLobby || !SteamPlatform.IsInitialized) return all;
            CSteamID lobby = SteamIds.ToSteam(current);
            int count = SteamMatchmaking.GetLobbyDataCount(lobby);
            for (int i = 0; i < count; i++)
            {
                if (!SteamMatchmaking.GetLobbyDataByIndex(lobby, i, out string key, KeyBufferChars, out string value, ValueBufferChars))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(key)) all[key] = value ?? "";
            }
            return all;
        }

        public string GetMemberData(PlayerId member, string key)
        {
            if (!InLobby || !member.IsValid || string.IsNullOrEmpty(key) || !SteamPlatform.IsInitialized) return "";
            return SteamMatchmaking.GetLobbyMemberData(SteamIds.ToSteam(current), SteamIds.ToSteam(member), key) ?? "";
        }

        /// <summary>Steam only lets a member write its own member data (03 §5).</summary>
        public bool SetLocalMemberData(string key, string value)
        {
            if (!IsAvailable || !InLobby)
            {
                KitLog.Warning("SetLocalMemberData ignored: " + (IsAvailable ? "not in a lobby." : "Steam is not initialized."));
                return false;
            }
            if (!IsValidKey(key) || !IsValidValue(key, value)) return false;
            SteamMatchmaking.SetLobbyMemberData(SteamIds.ToSteam(current), key, value ?? "");
            PlayerId local = LocalPlayer;
            if (local.IsValid && !dirtyMemberData.Contains(local)) dirtyMemberData.Add(local);
            membersDirty = true;
            return true;
        }

        /// <summary>UTF-8 without a trailing NUL; over 2048 bytes returns false without calling Steam (03 §6).</summary>
        public bool SendChat(string message)
        {
            if (!IsAvailable || !InLobby)
            {
                KitLog.Warning("SendChat ignored: " + (IsAvailable ? "not in a lobby." : "Steam is not initialized."));
                return false;
            }
            if (string.IsNullOrEmpty(message)) return false;
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            if (bytes.Length > MaxChatBytes)
            {
                KitLog.Warning("Lobby chat message of " + bytes.Length + " bytes is over " + MaxChatBytes + "; not sent.");
                return false;
            }
            bool ok = SteamMatchmaking.SendLobbyChatMsg(SteamIds.ToSteam(current), bytes, bytes.Length);
            if (!ok) KitLog.Warning("SendLobbyChatMsg failed.");
            return ok;
        }

        public void Flush(float now)
        {
            if (disposed || !SteamPlatform.IsInitialized || !TryRegisterCallbacks()) return;
            CheckPendingTimeout(now);
            if (!InLobby) return;

            LobbyId lobby = current;
            bool rebuilt = membersDirty || dirtyMemberData.Count > 0;
            if (rebuilt) RebuildMembers();
            PlayerId[] memberData = dirtyMemberData.Count > 0 ? dirtyMemberData.ToArray() : null;
            bool lobbyData = lobbyDataDirty;
            bool ownerCheck = ownerCheckPending || rebuilt;
            dirtyMemberData.Clear();
            membersDirty = false;
            lobbyDataDirty = false;
            ownerCheckPending = false;

            // A handler may leave the lobby; stop raising events for it as soon as that happens.
            if (lobbyData)
            {
                SteamPlatform.Raise(LobbyDataChanged, "LobbyDataChanged");
                if (current != lobby) return;
            }
            if (memberData != null)
            {
                foreach (PlayerId member in memberData)
                {
                    SteamPlatform.Raise(MemberDataChanged, member, "MemberDataChanged");
                    if (current != lobby) return;
                }
            }
            if (rebuilt)
            {
                SteamPlatform.Raise(MembersChanged, members, "MembersChanged");
                if (current != lobby) return;
            }
            if (ownerCheck)
            {
                CheckOwner();
                if (current != lobby) return;
            }
            if (rebuilt) WriteCountKey();
        }

        // Steam callbacks (registered in part 1).

        private void OnDataUpdate(LobbyDataUpdate_t update)
        {
            if (!InLobby || update.m_ulSteamIDLobby != current.Value) return;
            ownerCheckPending = true;
            if (update.m_ulSteamIDMember == update.m_ulSteamIDLobby)
            {
                lobbyDataDirty = true;
                return;
            }
            PlayerId member = SteamIds.ToPlayer(update.m_ulSteamIDMember);
            if (member.IsValid && !dirtyMemberData.Contains(member)) dirtyMemberData.Add(member);
            membersDirty = true;
        }

        private void OnChatMessage(LobbyChatMsg_t message)
        {
            if (!InLobby || message.m_ulSteamIDLobby != current.Value) return;
            if ((EChatEntryType)message.m_eChatEntryType != EChatEntryType.k_EChatEntryTypeChatMsg) return;

            int length = SteamMatchmaking.GetLobbyChatEntry(SteamIds.ToSteam(current), (int)message.m_iChatID,
                out CSteamID sender, chatBuffer, chatBuffer.Length, out EChatEntryType type);
            if (length <= 0 || type != EChatEntryType.k_EChatEntryTypeChatMsg) return;
            if (length > chatBuffer.Length) length = chatBuffer.Length;
            while (length > 0 && chatBuffer[length - 1] == 0) length--;   // trailing NUL bytes

            string text = Encoding.UTF8.GetString(chatBuffer, 0, length);
            SteamPlatform.Raise(ChatReceived, SteamIds.ToPlayer(sender), text, "ChatReceived");
        }

        // Names come from GetFriendPersonaName, so a member's persona change rebuilds the snapshot (03 §7).
        private void OnPersonaChange(PersonaStateChange_t change)
        {
            if (!InLobby) return;
            foreach (LobbyMember member in members)
            {
                if (member.Id.Value != change.m_ulSteamID) continue;
                membersDirty = true;
                return;
            }
        }

        // Snapshot and dirty state.

        private void RebuildMembers()
        {
            if (!InLobby || !SteamPlatform.IsInitialized)
            {
                members = NoMembers;
                return;
            }
            CSteamID lobby = SteamIds.ToSteam(current);
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (!SteamIds.IsUsable(owner)) owner = SteamIds.ToSteam(cachedOwner);
            CSteamID local = SteamIds.ToSteam(LocalPlayer);

            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            var list = new List<LobbyMember>(Math.Max(count, 1));
            for (int i = 0; i < count; i++)
            {
                CSteamID id = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
                if (!SteamIds.IsUsable(id)) continue;
                bool isOwner = id == owner;
                bool ready = isOwner || SteamMatchmaking.GetLobbyMemberData(lobby, id, KitKeys.Ready) == KitKeys.FlagOn;
                var member = new LobbyMember(SteamIds.ToPlayer(id), MemberName(id), isOwner, id == local, ready);
                if (isOwner) list.Insert(0, member);
                else list.Add(member);
            }
            members = list;
        }

        // GetFriendPersonaName returns "" or "[unknown]" until Steam has the persona; ask once per member.
        private string MemberName(CSteamID id)
        {
            string name = SteamFriends.GetFriendPersonaName(id);
            if (!string.IsNullOrEmpty(name) && name != UnknownPersona) return name;
            if (personaRequested.Add(id.m_SteamID)) SteamFriends.RequestUserInformation(id, true);
            return "";
        }

        // 03 §4: k.owner and k.count at create; the owner rewrites k.count after every membership change.
        private void WriteCreateKeys()
        {
            CSteamID lobby = SteamIds.ToSteam(current);
            PlayerId local = LocalPlayer;
            if (local.IsValid && !SteamMatchmaking.SetLobbyData(lobby, KitKeys.Owner, local.ToString()))
            {
                KitLog.Warning("Writing " + KitKeys.Owner + " failed.");
            }
            WriteCount(lobby, Math.Max(1, SteamMatchmaking.GetNumLobbyMembers(lobby)));
        }

        private void WriteCountKey()
        {
            if (!IsLocalOwner) return;
            int count = Math.Max(1, members.Count);
            if (count != writtenCount) WriteCount(SteamIds.ToSteam(current), count);
        }

        private void WriteCount(CSteamID lobby, int count)
        {
            if (SteamMatchmaking.SetLobbyData(lobby, KitKeys.Count, count.ToString(CultureInfo.InvariantCulture))) writtenCount = count;
            else KitLog.Warning("Writing " + KitKeys.Count + " failed.");
        }

        private void ResetDataState()
        {
            members = NoMembers;
            dirtyMemberData.Clear();
            personaRequested.Clear();
            lobbyDataDirty = false;
            membersDirty = false;
            ownerCheckPending = false;
            writtenCount = -1;
        }

        private void MarkMembersDirty()
        {
            membersDirty = true;
            ownerCheckPending = true;
        }

        private void MarkAllDirty()
        {
            lobbyDataDirty = true;
            MarkMembersDirty();
        }

        // Metadata helpers.

        private bool RequireOwner(string what)
        {
            string refusal = !IsAvailable ? "Steam is not initialized" : !InLobby ? "not in a lobby"
                : !IsLocalOwner ? "the local player is not the lobby owner" : null;
            if (refusal == null) return true;
            KitLog.Warning(what + " ignored: " + refusal + ".");
            return false;
        }

        // 03 §3.1. An undefined value maps to the most restrictive type.
        private static ELobbyType ToLobbyType(LobbyPrivacy privacy)
        {
            switch (privacy)
            {
                case LobbyPrivacy.Public: return ELobbyType.k_ELobbyTypePublic;
                case LobbyPrivacy.FriendsOnly: return ELobbyType.k_ELobbyTypeFriendsOnly;
                default: return ELobbyType.k_ELobbyTypePrivate;
            }
        }

        // Null or "" deletes the key (03 §4); deleting a missing key is a success.
        private static bool WriteData(CSteamID lobby, string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (string.IsNullOrEmpty(SteamMatchmaking.GetLobbyData(lobby, key))) return true;
                bool deleted = SteamMatchmaking.DeleteLobbyData(lobby, key);
                if (!deleted) KitLog.Warning("DeleteLobbyData('" + KitLog.Truncate(key) + "') failed.");
                return deleted;
            }
            if (!IsValidValue(key, value)) return false;
            bool ok = SteamMatchmaking.SetLobbyData(lobby, key, value);
            if (!ok) KitLog.Warning("SetLobbyData('" + KitLog.Truncate(key) + "') failed.");
            return ok;
        }

        private static bool IsValidKey(string key)
        {
            if (!string.IsNullOrEmpty(key) && Encoding.UTF8.GetByteCount(key) <= MaxKeyBytes) return true;
            KitLog.Warning("Lobby data key '" + KitLog.Truncate(key) + "' is empty or over " + MaxKeyBytes + " bytes.");
            return false;
        }

        private static bool IsValidValue(string key, string value)
        {
            if (value == null || Encoding.UTF8.GetByteCount(value) <= MaxValueBytes) return true;
            KitLog.Warning("Lobby data '" + KitLog.Truncate(key) + "' is over " + MaxValueBytes + " bytes; not written.");
            return false;
        }
    }
}
