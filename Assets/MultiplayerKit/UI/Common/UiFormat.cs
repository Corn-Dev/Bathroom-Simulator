// Display strings shared by the screens: playtime, absolute and relative save times, privacy and phase labels,
// player counts and the Continue subtitle. Pure static functions, unit-tested (05 §11); user-facing English.
// Design: 05 §4 (SlotSubtitle), §5.1 (Privacy, player count), §6 (Phase), §8 (Playtime, "g" times); 06 U7.

using System;
using System.Globalization;

namespace MultiplayerKit.UI
{
    public static class UiFormat
    {
        /// <summary>Separator between the parts of a one-line summary (" · ").</summary>
        public const string Separator = " \u00B7 ";

        private const double MaxPlaytimeSeconds = 3.6e9;   // a million hours; keeps the long conversion defined

        /// <summary>"&lt;1m", "45m", "2h 14m" (minutes rounded down). Negative or NaN reads as "&lt;1m".</summary>
        public static string Playtime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 60.0) return "<1m";
            if (seconds > MaxPlaytimeSeconds) seconds = MaxPlaytimeSeconds;

            long totalMinutes = (long)Math.Floor(seconds / 60.0);
            long hours = totalMinutes / 60;
            string minutes = (totalMinutes % 60).ToString(CultureInfo.InvariantCulture) + "m";
            return hours == 0 ? minutes : hours.ToString(CultureInfo.InvariantCulture) + "h " + minutes;
        }

        /// <summary>Local short date and time in the current culture ("g", 05 §8); "" for an unset time.</summary>
        public static string AbsoluteTime(DateTime utc)
        {
            if (utc == default(DateTime)) return "";
            return utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }

        /// <summary>"today 21:40", "yesterday 21:40", otherwise <see cref="AbsoluteTime"/>. Uses the local clock.</summary>
        public static string RelativeTime(DateTime utc)
        {
            return RelativeTime(utc, DateTime.Now);
        }

        /// <summary>As <see cref="RelativeTime(DateTime)"/> against a given local "now" (for tests).</summary>
        public static string RelativeTime(DateTime utc, DateTime nowLocal)
        {
            if (utc == default(DateTime)) return "";

            DateTime local = utc.ToLocalTime();   // Unspecified kind is treated as UTC, which is what saves store
            DateTime today = nowLocal.Date;
            string time = local.ToString("t", CultureInfo.CurrentCulture);
            if (local.Date == today) return "today " + time;
            if (local.Date == today.AddDays(-1)) return "yesterday " + time;
            return local.ToString("g", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Continue subtitle (05 §4): display name · playtime · last saved, for example "Pit 3 · 2h 14m · yesterday 21:40".
        /// The time is the "g" local time, shortened to today/yesterday as in the 05 §4 example. Empty parts are left out.
        /// </summary>
        public static string SlotSubtitle(WorldSaveSlotInfo info)
        {
            if (info == null) return "";
            return Join(info.DisplayName, Playtime(info.PlaytimeSeconds), RelativeTime(info.UpdatedUtc));
        }

        /// <summary>"Invite only", "Friends only", "Public".</summary>
        public static string Privacy(LobbyPrivacy privacy)
        {
            switch (privacy)
            {
                case LobbyPrivacy.InviteOnly: return "Invite only";
                case LobbyPrivacy.Public: return "Public";
                default: return "Friends only";
            }
        }

        /// <summary>
        /// Label for a "privacy" setting value ("inviteOnly", "friendsOnly", "public"). Unknown text reads as
        /// Friends only, the same fallback the lobby summary parser uses.
        /// </summary>
        public static string Privacy(string choice)
        {
            LobbyEnums.TryParsePrivacy(choice, out LobbyPrivacy privacy);
            return Privacy(privacy);
        }

        /// <summary>"In lobby", "Starting", "In game".</summary>
        public static string Phase(LobbyPhase phase)
        {
            switch (phase)
            {
                case LobbyPhase.Starting: return "Starting";
                case LobbyPhase.InGame: return "In game";
                default: return "In lobby";
            }
        }

        /// <summary>"2/4"; only the count when the limit is unknown (0 or less).</summary>
        public static string PlayerCount(int count, int max)
        {
            string current = Math.Max(0, count).ToString(CultureInfo.InvariantCulture);
            return max > 0 ? current + "/" + max.ToString(CultureInfo.InvariantCulture) : current;
        }

        /// <summary>Joins the non-empty parts with <see cref="Separator"/>.</summary>
        public static string Join(params string[] parts)
        {
            if (parts == null || parts.Length == 0) return "";

            string result = "";
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                result = result.Length == 0 ? parts[i] : result + Separator + parts[i];
            }
            return result;
        }
    }
}
