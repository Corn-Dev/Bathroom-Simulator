// Default English user messages for KitError codes, plus the variant texts that are not 1:1 with a code
// (room "host closed the lobby", save/load texts). Games localize or reword through Resolver.
// Design: 01 §3 (For, Resolver), 02 header and §7b (HostClosedLobby), 04 §6.1 and §11 (save texts),
// 01 §4.8 (start failure texts), 05 §4 (PlatformUnavailable text).

using System;

namespace MultiplayerKit
{
    public static class SessionErrorMessages
    {
        /// <summary>
        /// Replaceable message hook. Receives the error code and the kit's default English text for that
        /// use (DefaultFor(code), or one of the <see cref="English"/> variant texts) and returns the text to
        /// show. Return null or "" to keep the default. Exceptions are logged and the default is kept.
        /// </summary>
        public static Func<KitError, string, string> Resolver;

        /// <summary>Default English variant texts. Resolvers can compare their defaultText argument with these.</summary>
        public static class English
        {
            public const string HostClosedLobby = "The host closed the lobby.";
            public const string SaveFailed = "Couldn't save the game.";
            public const string SaveAndQuitFailed = "Save failed. The game was not closed.";
            public const string SaveSlotsFull = "Save slots are full. Delete a save to continue saving.";
            public const string SaveFromNewerVersion = "This save was made by a newer version of the game.";
            public const string SaveRestoreFailed = "This save couldn't be restored.";
            public const string NoSavedGames = "No saved games.";
            public const string GameFailedToStart = "The game failed to start.";
            public const string GameStartTimedOut = "The game took too long to start.";
        }

        // Variants, already passed through Resolver (so overrides apply to them as well as to For).

        /// <summary>Room/joining variant of HostLeft (02 §7b). For(HostLeft) is the in-game text.</summary>
        public static string HostClosedLobby => Resolve(KitError.HostLeft, English.HostClosedLobby);

        /// <summary>Autosave or leave-time save failed (02 §6c.2).</summary>
        public static string SaveFailed => Resolve(KitError.SaveIoFailed, English.SaveFailed);

        /// <summary>Save &amp; Quit could not save; the session stays in game (02 §6c.3).</summary>
        public static string SaveAndQuitFailed => Resolve(KitError.SaveIoFailed, English.SaveAndQuitFailed);

        /// <summary>Every save slot is used and a new game tried to save (04 §11).</summary>
        public static string SaveSlotsFull => Resolve(KitError.SaveIoFailed, English.SaveSlotsFull);

        /// <summary>Save validation found a newer save format (04 §6.1).</summary>
        public static string SaveFromNewerVersion => Resolve(KitError.IncompatibleVersion, English.SaveFromNewerVersion);

        /// <summary>The game could not rebuild its world from a valid save (04 §11).</summary>
        public static string SaveRestoreFailed => Resolve(KitError.SaveCorrupt, English.SaveRestoreFailed);

        /// <summary>Continue pressed with no save on disk (02 §2b.1).</summary>
        public static string NoSavedGames => Resolve(KitError.SaveNotFound, English.NoSavedGames);

        /// <summary>The adapter threw in OnSessionLive (01 §4.8).</summary>
        public static string GameFailedToStart => Resolve(KitError.Unknown, English.GameFailedToStart);

        /// <summary>The host world was not ready within KitTimeouts.WorldReadySeconds (01 §4.8).</summary>
        public static string GameStartTimedOut => Resolve(KitError.Timeout, English.GameStartTimedOut);

        /// <summary>User message for a code, after Resolver. KitError.None returns "".</summary>
        public static string For(KitError code)
        {
            if (code == KitError.None) return "";
            return Resolve(code, DefaultFor(code));
        }

        /// <summary>
        /// Menu-time load/validation context (04 §6.1): IncompatibleVersion reads as "newer version of the
        /// game" instead of the join text; every other code is For(code).
        /// </summary>
        public static string ForLoadingSave(KitError code)
        {
            return code == KitError.IncompatibleVersion ? SaveFromNewerVersion : For(code);
        }

        /// <summary>Applies Resolver to a default text. Never returns null.</summary>
        public static string Resolve(KitError code, string defaultText)
        {
            string fallback = defaultText ?? "";
            Func<KitError, string, string> resolver = Resolver;
            if (resolver == null) return fallback;
            try
            {
                string text = resolver(code, fallback);
                return string.IsNullOrEmpty(text) ? fallback : text;
            }
            catch (Exception e)
            {
                KitLog.Error("SessionErrorMessages.Resolver threw for " + code + ": " + e.Message);
                return fallback;
            }
        }

        /// <summary>Built-in English text per code, ignoring Resolver.</summary>
        public static string DefaultFor(KitError code)
        {
            switch (code)
            {
                case KitError.None: return "";
                case KitError.PlatformUnavailable: return "Steam is not running.";
                case KitError.Timeout: return "That took too long. Please try again.";
                case KitError.Busy: return "Please wait for the current action to finish.";
                case KitError.InvalidArgument: return "That request isn't valid.";
                case KitError.LobbyCreateFailed: return "Couldn't create the lobby.";
                case KitError.LobbyNotFound: return "That game no longer exists.";
                case KitError.LobbyFull: return "That game is full.";
                case KitError.LobbyAccessDenied: return "You can't join that game.";
                case KitError.LobbyNotJoinable: return "That game can't be joined right now.";
                case KitError.IncompatibleVersion: return "Different game version.";
                case KitError.PasswordRequired: return "This game needs a password.";
                case KitError.WrongPassword: return "Incorrect password.";
                case KitError.Banned: return "You are banned from this game.";
                case KitError.Kicked: return "You were removed from the lobby.";
                case KitError.HostLeft: return "The host left the game. Your progress was saved by the host.";
                case KitError.ConnectionLost: return "Lost connection to the host.";
                case KitError.ApprovalRejected: return "The host refused the connection.";
                case KitError.NetworkStartFailed: return "Couldn't start the network session.";
                case KitError.SceneLoadFailed: return "Couldn't load the game.";
                case KitError.SaveIoFailed: return "Couldn't access the save file.";
                case KitError.SaveCorrupt: return "This save is damaged and can't be loaded.";
                case KitError.SaveNotFound: return "That save no longer exists.";
                case KitError.Cancelled: return "Cancelled.";
                default: return "Something went wrong.";
            }
        }
    }
}
