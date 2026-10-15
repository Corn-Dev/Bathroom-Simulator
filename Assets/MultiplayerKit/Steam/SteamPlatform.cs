// Steam availability for every kit assembly, the internal per-frame flush contract and small shared helpers.
// Design: 06 §1 reconciliation (SteamManager is a loose script in Assembly-CSharp, which asmdef assemblies cannot
// reference, so every kit call site reads SteamPlatform.IsInitialized), 06 S3, 03 §1 (Flush from the bootstrap's
// LateUpdate), 03 §7 (late callback registration). The kit never calls SteamAPI.Init or SteamAPI.RunCallbacks;
// the project's SteamManager owns both. Main thread only.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Steamworks;

// MultiplayerKitBootstrap (Ngo assembly) drives ISteamFlushable.Flush and disposes the services.
[assembly: InternalsVisibleTo("MultiplayerKit.Ngo")]

namespace MultiplayerKit.Steam
{
    /// <summary>
    /// Steam services that raise coalesced events, check deadlines and retry late callback registration
    /// (03 §1, §7). The bootstrap calls Flush once per frame from LateUpdate with Time.unscaledTime.
    /// </summary>
    internal interface ISteamFlushable
    {
        void Flush(float now);
    }

    public static class SteamPlatform
    {
        private const int FallbackRetryMilliseconds = 1000;

        private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        private static bool fallbackConfirmed;
        private static bool fallbackTried;
        private static int fallbackLastTry;

        /// <summary>
        /// Readiness probe, set by the game before the bootstrap's Awake (BallPitKitInstaller sets
        /// () => SteamManager.Initialized). When null, IsInitialized falls back to a guarded
        /// SteamUser.GetSteamID() call, cached once it succeeds.
        /// </summary>
        public static Func<bool> ReadyProbe;

        /// <summary>True when the Steam API is initialized and usable. Never throws.</summary>
        public static bool IsInitialized
        {
            get
            {
                Func<bool> probe = ReadyProbe;
                if (probe != null)
                {
                    try
                    {
                        return probe();
                    }
                    catch (Exception e)
                    {
                        WarnOnce("SteamPlatform.ReadyProbe threw " + e.GetType().Name + "; treating Steam as unavailable.");
                        return false;
                    }
                }
                return FallbackProbe();
            }
        }

        /// <summary>This process's Steam AppID; 0 when Steam is not initialized.</summary>
        public static uint AppId
        {
            get
            {
                if (!IsInitialized) return 0;
                try
                {
                    return SteamUtils.GetAppID().m_AppId;
                }
                catch (Exception e)
                {
                    WarnOnce("SteamUtils.GetAppID failed: " + e.GetType().Name);
                    return 0;
                }
            }
        }

        /// <summary>Logs a Warning the first time a given message is seen in this app session.</summary>
        internal static void WarnOnce(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (warned.Add(message)) KitLog.Warning(message);
        }

        // Event and completion invocation. A throwing subscriber is logged and never breaks service state.

        internal static void Raise(Action handler, string what)
        {
            if (handler == null) return;
            try
            {
                handler();
            }
            catch (Exception e)
            {
                KitLog.Error("A handler of " + what + " threw.", e);
            }
        }

        internal static void Raise<T>(Action<T> handler, T arg, string what)
        {
            if (handler == null) return;
            try
            {
                handler(arg);
            }
            catch (Exception e)
            {
                KitLog.Error("A handler of " + what + " threw.", e);
            }
        }

        internal static void Raise<T1, T2>(Action<T1, T2> handler, T1 first, T2 second, string what)
        {
            if (handler == null) return;
            try
            {
                handler(first, second);
            }
            catch (Exception e)
            {
                KitLog.Error("A handler of " + what + " threw.", e);
            }
        }

        // Steamworks.NET throws InvalidOperationException ("Steamworks is not initialized") from any call made
        // before SteamAPI.Init, and DllNotFoundException without the native library. A failed probe is retried
        // at most once per second so an uninitialized build does not throw on every Flush.
        private static bool FallbackProbe()
        {
            if (fallbackConfirmed) return true;
            int tick = Environment.TickCount;
            if (fallbackTried && unchecked(tick - fallbackLastTry) < FallbackRetryMilliseconds) return false;
            fallbackTried = true;
            fallbackLastTry = tick;
            try
            {
                fallbackConfirmed = SteamUser.GetSteamID().IsValid();
            }
            catch (Exception)
            {
                fallbackConfirmed = false;
            }
            return fallbackConfirmed;
        }

        // Domain reload may be disabled in the editor; the installer sets ReadyProbe again in its Awake.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ReadyProbe = null;
            fallbackConfirmed = false;
            fallbackTried = false;
            fallbackLastTry = 0;
            warned.Clear();
        }
    }
}
