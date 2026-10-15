// Invites, overlay join requests, the "+connect_lobby <id>" launch argument and rich presence.
// Design: 01 §4.4, 03 §7 (GameLobbyJoinRequested_t, GameRichPresenceJoinRequested_t), §8.4-§8.5, 06 S9.
// Replaces the input SteamInviteService (friends overlay only) and the join half of SteamLobbyDiscoveryService:
// a join request is only reported (JoinRequested); this service never calls JoinLobby, SessionController decides.
// Rich presence keys: connect ("+connect_lobby <id>" or "" = no Join Game), steam_display (#Status_* tokens from the
// owner's localization file), status (plain text fallback), steam_player_group / steam_player_group_size (party).
// Implements ISteamFlushable only to retry callback registration when constructed before Steam was up (03 §7).

using System;
using System.Globalization;
using Steamworks;

namespace MultiplayerKit.Steam
{
    public sealed class SteamInviteService : IInviteService, ISteamFlushable, IDisposable
    {
        public const string DisplayInMenu = "#Status_InMenu";
        public const string DisplayInLobby = "#Status_InLobby";
        public const string DisplayInGame = "#Status_InGame";

        private const string KeyConnect = "connect";
        private const string KeyDisplay = "steam_display";
        private const string KeyStatus = "status";
        private const string KeyGroup = "steam_player_group";
        private const string KeyGroupSize = "steam_player_group_size";

        private Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
        private Callback<GameRichPresenceJoinRequested_t> presenceJoinRequested;
        private bool callbacksRegistered;
        private bool registrationWarned;
        private bool disposed;

        private bool launchConsumed;
        private bool presenceSet;
        private PresenceInfo lastPresence;

        public SteamInviteService()
        {
            TryRegisterCallbacks();
        }

        /// <summary>Overlay "Join Game", an accepted invite, or a rich-presence join while the game runs.</summary>
        public event Action<LobbyId> JoinRequested;

        /// <summary>Works for every privacy type: invites bypass the lobby type (03 §8.4).</summary>
        public bool OpenInviteOverlay(LobbyId lobby)
        {
            if (!CanUseOverlay("invite dialog")) return false;
            if (!lobby.IsValid)
            {
                KitLog.Warning("Invite overlay not opened: not in a lobby.");
                return false;
            }
            SteamFriends.ActivateGameOverlayInviteDialog(SteamIds.ToSteam(lobby));
            return true;
        }

        public bool OpenFriendsOverlay()
        {
            if (!CanUseOverlay("friends list")) return false;
            SteamFriends.ActivateGameOverlay("Friends");
            return true;
        }

        /// <summary>First call scans the command line (LaunchArgs.TryFind logs a malformed id); later calls return false.</summary>
        public bool TryConsumeLaunchLobby(out LobbyId lobby)
        {
            lobby = LobbyId.None;
            if (launchConsumed) return false;
            launchConsumed = true;
            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch (Exception e)
            {
                KitLog.Warning("Command line not readable: " + e.GetType().Name);
                return false;
            }
            if (!LaunchArgs.TryFind(args, out lobby)) return false;
            KitLog.Info("Launch argument requests lobby " + lobby + ".");
            return true;
        }

        /// <summary>Deduplicated: an identical snapshot is not sent again (03 §8.5).</summary>
        public void SetPresence(PresenceInfo info)
        {
            if (presenceSet && info == lastPresence) return;
            if (!SteamPlatform.IsInitialized)
            {
                SteamPlatform.WarnOnce("Rich presence not set: Steam is not initialized.");
                return;
            }
            bool joinable = info.JoinableLobby.IsValid;
            string group = joinable ? info.JoinableLobby.ToString() : "";
            bool ok = SteamFriends.SetRichPresence(KeyConnect, joinable ? LaunchArgs.ConnectLobby + " " + info.JoinableLobby : "");
            ok &= SteamFriends.SetRichPresence(KeyDisplay, DisplayToken(info.Status));
            ok &= SteamFriends.SetRichPresence(KeyStatus, StatusText(info.Status));
            ok &= SteamFriends.SetRichPresence(KeyGroup, group);
            ok &= SteamFriends.SetRichPresence(KeyGroupSize,
                group.Length > 0 && info.PartySize > 0 ? info.PartySize.ToString(CultureInfo.InvariantCulture) : "");
            if (!ok) KitLog.Warning("SetRichPresence failed for at least one key.");
            presenceSet = true;
            lastPresence = info;
        }

        public void ClearPresence()
        {
            presenceSet = false;
            if (!SteamPlatform.IsInitialized) return;
            SteamFriends.ClearRichPresence();
        }

        public void Flush(float now)
        {
            if (!disposed && !callbacksRegistered) TryRegisterCallbacks();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            callbacksRegistered = false;
            lobbyJoinRequested?.Dispose();
            presenceJoinRequested?.Dispose();
            lobbyJoinRequested = null;
            presenceJoinRequested = null;
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t request)
        {
            LobbyId lobby = SteamIds.ToLobby(request.m_steamIDLobby);
            if (!lobby.IsValid)
            {
                KitLog.Warning("Ignoring an overlay join request without a valid lobby.");
                return;
            }
            KitLog.Info("Overlay join request for lobby " + lobby + ".");
            SteamPlatform.Raise(JoinRequested, lobby, "JoinRequested");
        }

        // m_rgchConnect is the friend's "connect" value, which this kit writes as "+connect_lobby <id>".
        private void OnRichPresenceJoinRequested(GameRichPresenceJoinRequested_t request)
        {
            string connect = request.m_rgchConnect;
            if (!LaunchArgs.TryParseConnectLobby(connect, out LobbyId lobby))
            {
                KitLog.Warning("Ignoring a rich presence join request '" + KitLog.Truncate(connect) + "'.");
                return;
            }
            KitLog.Info("Rich presence join request for lobby " + lobby + ".");
            SteamPlatform.Raise(JoinRequested, lobby, "JoinRequested");
        }

        private static bool CanUseOverlay(string what)
        {
            if (!SteamPlatform.IsInitialized)
            {
                KitLog.Warning("Steam overlay " + what + " not opened: Steam is not initialized.");
                return false;
            }
            if (!SteamUtils.IsOverlayEnabled())
            {
                KitLog.Warning("Steam overlay " + what + " not opened: the overlay is disabled.");
                return false;
            }
            return true;
        }

        private static string DisplayToken(PresenceStatus status)
        {
            switch (status)
            {
                case PresenceStatus.InLobbyRoom: return DisplayInLobby;
                case PresenceStatus.InGame: return DisplayInGame;
                default: return DisplayInMenu;
            }
        }

        private static string StatusText(PresenceStatus status)
        {
            switch (status)
            {
                case PresenceStatus.InLobbyRoom: return "In a lobby";
                case PresenceStatus.InGame: return "In game";
                default: return "In the menu";
            }
        }

        private bool TryRegisterCallbacks()
        {
            if (callbacksRegistered) return true;
            if (disposed) return false;
            if (!SteamPlatform.IsInitialized)
            {
                if (!registrationWarned) KitLog.Warning("SteamInviteService: Steam is not initialized; callbacks are registered once it is.");
                registrationWarned = true;
                return false;
            }
            lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            presenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichPresenceJoinRequested);
            callbacksRegistered = true;
            if (registrationWarned) KitLog.Info("SteamInviteService: Steam callbacks registered late.");
            return true;
        }
    }
}
