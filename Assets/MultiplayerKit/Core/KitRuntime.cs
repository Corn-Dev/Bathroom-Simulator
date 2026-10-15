// Service holder the composition root publishes into, so the UI assembly reaches the kit through Core only
// (it must not reference the Ngo assembly that holds the bootstrap). Design: 05 §2.1, 05 §12, 06 C9.
// Publish runs in the bootstrap's Start after every service exists; Clear runs in its OnDestroy.

using System;

namespace MultiplayerKit
{
    public sealed class KitServices
    {
        public SessionController Session { get; }
        public IFriendsLobbyDiscovery Friends { get; }

        /// <summary>Never null; IsSupported may be false.</summary>
        public IPublicLobbyBrowser Browser { get; }

        /// <summary>Null when the WorldSaves capability is off.</summary>
        public IWorldSaveStore Saves { get; }

        public IPlayerIdentity Identity { get; }

        /// <summary>adapter.GameVersion, for save-slot version mismatch warnings. Never null.</summary>
        public string GameVersion { get; }

        public KitServices(SessionController session, IFriendsLobbyDiscovery friends, IPublicLobbyBrowser browser,
                           IWorldSaveStore saves, IPlayerIdentity identity, string gameVersion)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Friends = friends ?? throw new ArgumentNullException(nameof(friends));
            Browser = browser ?? throw new ArgumentNullException(nameof(browser));
            Saves = saves;
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            GameVersion = gameVersion ?? "";
        }
    }

    public static class KitRuntime
    {
        /// <summary>Null until the bootstrap publishes, and again after Clear.</summary>
        public static KitServices Current { get; private set; }

        /// <summary>Raised once per Publish. Views that enabled before the bootstrap bind from here.</summary>
        public static event Action<KitServices> Published;

        /// <summary>
        /// Sets Current and raises Published. Null, or a second Publish while Current is set (a duplicate
        /// bootstrap, which destroys itself), logs Error and is ignored.
        /// </summary>
        public static void Publish(KitServices services)
        {
            if (services == null)
            {
                KitLog.Error("KitRuntime.Publish called with null services; ignored.");
                return;
            }
            if (Current != null)
            {
                KitLog.Error("KitRuntime.Publish called while services are already published; ignored.");
                return;
            }

            Current = services;

            Action<KitServices> handlers = Published;
            if (handlers == null) return;

            // One failing view must not keep the others from binding.
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<KitServices>)handler)(services);
                }
                catch (Exception e)
                {
                    KitLog.Error("KitRuntime.Published handler threw.", e);
                }
            }
        }

        /// <summary>Sets Current to null without raising an event. Subscribers to Published are kept.</summary>
        public static void Clear()
        {
            Current = null;
        }
    }
}
