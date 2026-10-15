// The only thing a game implements: identity/version, scenes, settings schema, capabilities, and the
// session lifecycle hooks. Design: 01 §4.8; optional extras IGameReadyGate and IWorldSaveValidator (04 §9.3).
// SessionController is the only caller and enforces the contract: OnSessionLive exactly once per session per
// peer and OnSessionEnding exactly once after it; every callback is wrapped in try/catch (an exception in
// OnSessionLive ends the session, others are logged and swallowed).

using System;
using System.Collections.Generic;

namespace MultiplayerKit
{
    [Flags]
    public enum GameCapabilities
    {
        None = 0,

        /// <summary>Off: "public" removed from privacy choices, browser hidden, IPublicLobbyBrowser.IsSupported false.</summary>
        PublicLobbies = 1 << 0,

        /// <summary>Off: Continue/Load hidden, no autosave, CaptureWorldSave never called.</summary>
        WorldSaves = 1 << 1,

        /// <summary>Off: joinInProgress forced false and its control hidden.</summary>
        JoinInProgress = 1 << 2,
    }

    /// <summary>Everything a peer's adapter needs when the gameplay scene is live (decision 8 step 7).</summary>
    public sealed class SessionLiveContext
    {
        /// <summary>The kit-owned NGO NetworkManager, as object; the adapter casts it.</summary>
        public object NetworkHandle { get; }

        public bool IsHost { get; }
        public PlayerId LocalPlayer { get; }
        public IReadOnlyHostSettings Settings { get; }

        /// <summary>Host only; null for a new game and on clients.</summary>
        public WorldSaveData ResumeSave { get; }

        /// <summary>For TryGetPlayer(clientId) lookups.</summary>
        public ISessionNetwork Network { get; }

        public SessionLiveContext(object networkHandle, bool isHost, PlayerId localPlayer,
                                  IReadOnlyHostSettings settings, WorldSaveData resumeSave, ISessionNetwork network)
        {
            NetworkHandle = networkHandle;
            IsHost = isHost;
            LocalPlayer = localPlayer;
            Settings = settings;
            ResumeSave = resumeSave;
            Network = network;
        }
    }

    public enum SessionEndReason { LocalLeft, SaveAndQuit, HostLeft, Kicked, ConnectionLost, Error }

    public interface IGameAdapter
    {
        /// <summary>Lobby filter "k.game"; ^[a-z0-9_]{1,24}$.</summary>
        string GameId { get; }

        /// <summary>Must match exactly between peers; max 32 chars.</summary>
        string GameVersion { get; }

        /// <summary>Must be in the NGO scene list and build settings.</summary>
        string GameplaySceneName { get; }

        string MenuSceneName { get; }

        /// <summary>1..250 (Steam lobby limit).</summary>
        int MaxPlayersCap { get; }

        GameCapabilities Capabilities { get; }

        /// <summary>
        /// Called once at bootstrap. Returns game-specific definitions; Core merges in the core definitions it
        /// requires (01 §5.3) and validates the result.
        /// </summary>
        IReadOnlyList<SettingDefinition> BuildSettingsSchema();

        /// <summary>Keys that shape the world. Locked (read-only) when the session resumes a save.</summary>
        IReadOnlyCollection<string> WorldShapingKeys { get; }

        /// <summary>Cross-setting validation before Start. Null if valid, else a user message.</summary>
        string ValidateSettings(IReadOnlyHostSettings settings);

        /// <summary>
        /// Every peer, after NGO scene sync of GameplaySceneName completes. Host: restore ResumeSave, if present,
        /// BEFORE starting game replication.
        /// </summary>
        void OnSessionLive(SessionLiveContext context);

        /// <summary>Host only, after OnSessionLive.</summary>
        void OnPlayerJoined(PlayerId player, ulong clientId);

        /// <summary>Host only.</summary>
        void OnPlayerLeft(PlayerId player, ulong clientId);

        /// <summary>In game, for EditableAfterStart keys.</summary>
        void OnSettingsChanged(IReadOnlyHostSettings settings, string key);

        /// <summary>
        /// Host only, while InGame and only with WorldSaves: autosave, Save &amp; Quit, and before shutdown when
        /// ending as host. Return null to skip (for example world not ready). Must not throw; exceptions are
        /// caught, logged at Error and treated as null.
        /// </summary>
        byte[] CaptureWorldSave();

        /// <summary>Every peer, before NGO shutdown, while NetworkHandle is still valid.</summary>
        void OnSessionEnding(SessionEndReason reason);
    }
}
