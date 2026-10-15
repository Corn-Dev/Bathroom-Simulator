// PitSites: discovery, selection and activation of the scene's PitSites (deliverable A, file plan V3; design b 4.2-4.4,
// 9.1-9.3; brief 11.2-11.3). Exactly one site is active per session. Selection is a pluggable policy
// (IPitSiteSelector); the default picks the site whose profileKey matches the lobby's pitSize. Also the cross-site
// rules for the validator and PitSiteStandalone, the dev harness's standalone venue preview.
using System.Collections.Generic;
using System.Text;
using BallPitFinal;
using MultiplayerKit;
using UnityEngine;

namespace BallPitGameplay
{
    /// <summary>What a selector decides from: the lobby settings, the resumed save's siteId, and the usable sites.</summary>
    public readonly struct PitSiteQuery
    {
        public PitSiteQuery(IReadOnlyHostSettings settings, string savedSiteId, IReadOnlyList<PitSite> sites)
        {
            Settings = settings;
            SavedSiteId = savedSiteId;
            Sites = sites;
        }

        /// <summary>ctx.Settings, on host and clients alike.</summary>
        public IReadOnlyHostSettings Settings { get; }
        /// <summary>Resuming host: the save's siteId (null for v1 saves). New games and clients: null.</summary>
        public string SavedSiteId { get; }
        /// <summary>PitSites.FindAll(): valid, unique, in siteId order.</summary>
        public IReadOnlyList<PitSite> Sites { get; }
    }

    public interface IPitSiteSelector
    {
        /// <summary>
        /// The active site for this query (one of query.Sites), or null when none fits (the caller fails the session).
        /// Must be deterministic. Clients always query with SavedSiteId == null, so a selector whose answer is not a
        /// function of Settings and Sites must replicate the siteId to clients itself (DECISIONS.md; B's channel).
        /// </summary>
        PitSite Select(in PitSiteQuery query);
    }

    /// <summary>The saved site if it still exists, else the first site (siteId order) whose profileKey equals the
    /// lobby setting, else null.</summary>
    public sealed class DefaultPitSiteSelector : IPitSiteSelector
    {
        public DefaultPitSiteSelector(string profileSettingKey) { ProfileSettingKey = profileSettingKey; }

        /// <summary>The host-settings key whose value is a profile key (the adapter passes BallPitSettingsMap.PitSize).</summary>
        public string ProfileSettingKey { get; }

        public PitSite Select(in PitSiteQuery query)
        {
            IReadOnlyList<PitSite> sites = query.Sites;
            if (sites == null) return null;
            if (query.SavedSiteId != null)
                for (int i = 0; i < sites.Count; i++)
                    if (sites[i] != null && string.Equals(sites[i].SiteId, query.SavedSiteId, System.StringComparison.Ordinal))
                        return sites[i];
            string key = ProfileKeyOf(query.Settings);
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < sites.Count; i++)
                if (sites[i] != null && string.Equals(sites[i].ProfileKey, key, System.StringComparison.Ordinal))
                    return sites[i];
            return null;
        }

        /// <summary>The lobby's profile key, or null without settings or a setting key.</summary>
        public string ProfileKeyOf(IReadOnlyHostSettings settings) =>
            settings == null || string.IsNullOrEmpty(ProfileSettingKey) ? null : settings.GetString(ProfileSettingKey);
    }

    public static class PitSites
    {
        private struct Candidate { public PitSite Site; public int BuildIndex; public int[] Path; }

        /// <summary>
        /// Every usable PitSite in the loaded scenes, sorted by siteId (ordinal): a fresh list per call (selection time
        /// only). Inactive sites are included. A site with an invalid siteId, or a later duplicate of an earlier one, is
        /// skipped with an Error; duplicates are ordered by scene build index, then sibling-index path, so every peer
        /// drops the same object.
        /// </summary>
        public static List<PitSite> FindAll()
        {
            PitSite[] found = Object.FindObjectsOfType<PitSite>(true);
            var candidates = new List<Candidate>(found.Length);
            for (int i = 0; i < found.Length; i++)
            {
                PitSite s = found[i];
                if (s == null) continue;
                if (!PitSite.IsValidSiteId(s.SiteId))
                {
                    Debug.LogError($"[PitSites] PitSite '{PathOf(s)}': siteId '{s.SiteId}' must match " +
                                   $"^[a-z0-9_.]{{1,{PitSite.MaxSiteIdLength}}}$; skipped", s);
                    continue;
                }
                candidates.Add(new Candidate { Site = s, BuildIndex = s.gameObject.scene.buildIndex, Path = SiblingPath(s.transform) });
            }
            candidates.Sort(CompareCandidates);
            var result = new List<PitSite>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                PitSite s = candidates[i].Site;
                if (result.Count > 0 && string.Equals(result[result.Count - 1].SiteId, s.SiteId, System.StringComparison.Ordinal))
                {
                    Debug.LogError($"[PitSites] PitSite '{PathOf(s)}': duplicate siteId '{s.SiteId}' (kept " +
                                   $"'{PathOf(result[result.Count - 1])}'); skipped", s);
                    continue;
                }
                result.Add(s);
            }
            return result;
        }

        /// <summary>Venue mode: at least one PitSite is loaded (inactive ones included).</summary>
        public static bool AnyLoaded() => Object.FindObjectsOfType<PitSite>(true).Length > 0;

        /// <summary>
        /// Disables every site's overview camera and AudioListener, then deactivates every other site's siteRoot and
        /// activates the active one's (two passes, so a shared root ends up on exactly when the active site needs it).
        /// </summary>
        public static void Activate(PitSite active, IReadOnlyList<PitSite> all)
        {
            int n = all != null ? all.Count : 0;
            for (int i = 0; i < n; i++)
            {
                PitSite s = all[i];
                if (s == null) continue;
                s.IsActiveSite = s == active;
                PitSite.DisableOverview(s.OverviewCamera);
            }
            if (active != null)
            {
                active.IsActiveSite = true;
                PitSite.DisableOverview(active.OverviewCamera);
            }
            GameObject activeRoot = active != null ? active.SiteRoot : null;
            for (int i = 0; i < n; i++)
            {
                PitSite s = all[i];
                if (s == null || s == active || s.SiteRoot == null || s.SiteRoot == activeRoot) continue;
                s.SiteRoot.SetActive(false);
            }
            if (activeRoot != null) activeRoot.SetActive(true);
        }

        /// <summary>
        /// The adapter's one call on host and client (design b 9.1 steps 1, 2, 6; 9.2 steps 1-3). Returns the activated
        /// site. Null with error == null: a site-less scene (run today's path). Null with an error: sites are loaded but
        /// none can be used for this session. savedSiteId is null on clients and new games. Logs only the resume
        /// divergence; the caller logs the returned error.
        /// </summary>
        public static PitSite SelectAndActivate(IPitSiteSelector selector, IReadOnlyHostSettings settings, string savedSiteId,
                                                out List<PitSite> sites, out string error)
        {
            error = null;
            sites = FindAll();
            if (sites.Count == 0)
            {
                if (AnyLoaded()) error = "the gameplay scene has PitSites, but none has a valid, unique siteId";
                return null;
            }
            if (selector == null) { error = "no PitSite selector"; return null; }

            PitSite site = Run(selector, new PitSiteQuery(settings, savedSiteId, sites), out string selectorError);
            if (site == null || !sites.Contains(site))
            {
                error = (selectorError ?? "no PitSite fits this session") + Describe(selector, settings, savedSiteId, sites);
                return null;
            }
            if (savedSiteId != null)
            {
                PitSite bySettings = Run(selector, new PitSiteQuery(settings, null, sites), out _);
                if (bySettings != site)
                    Debug.LogError($"[PitSites] resume: the save was made at site '{site.SiteId}', but these settings select " +
                                   $"'{(bySettings != null ? bySettings.SiteId : "none")}'. Continuing at the saved site; clients " +
                                   "select from the settings and build at the other one until the siteId is replicated " +
                                   "(see DECISIONS.md)", site);
            }
            Activate(site, sites);
            return site;
        }

        /// <summary>Cross-site rules for the validator: duplicate siteIds (Error) and shared profileKeys (Warning, the
        /// default selector takes the first by siteId). Pass every loaded site, invalid ones included.</summary>
        public static void CollectCrossSiteProblems(IReadOnlyList<PitSite> all, List<string> errors, List<string> warnings)
        {
            if (all == null) return;
            var ids = new Dictionary<string, PitSite>(System.StringComparer.Ordinal);
            var profiles = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);
            for (int i = 0; i < all.Count; i++)
            {
                PitSite s = all[i];
                if (s == null || !PitSite.IsValidSiteId(s.SiteId)) continue;
                if (ids.TryGetValue(s.SiteId, out PitSite first))
                { errors?.Add($"duplicate siteId '{s.SiteId}': '{PathOf(first)}' and '{PathOf(s)}'"); continue; }
                ids.Add(s.SiteId, s);
                string key = s.ProfileKey ?? "";
                if (!profiles.TryGetValue(key, out List<string> list)) profiles.Add(key, list = new List<string>());
                list.Add(s.SiteId);
            }
            if (warnings == null) return;
            foreach (KeyValuePair<string, List<string>> kv in profiles)
            {
                if (kv.Value.Count < 2) continue;
                kv.Value.Sort(System.StringComparer.Ordinal);
                warnings.Add($"sites {string.Join(", ", kv.Value)} share profile '{kv.Key}': the default selector always " +
                             $"takes '{kv.Value[0]}'; the others need a custom selector, and a resume may diverge on clients");
            }
        }

        /// <summary>Hierarchy path for logs ("Scene/Root/Child").</summary>
        public static string PathOf(Component c)
        {
            if (c == null) return "(destroyed)";
            var sb = new StringBuilder(c.name);
            for (Transform t = c.transform.parent; t != null; t = t.parent) sb.Insert(0, t.name + "/");
            return c.gameObject.scene.name + "/" + sb;
        }

        private static PitSite Run(IPitSiteSelector selector, in PitSiteQuery query, out string error)
        {
            error = null;
            try { return selector.Select(in query); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                error = "the PitSite selector threw " + e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        private static string Describe(IPitSiteSelector selector, IReadOnlyHostSettings settings, string savedSiteId,
                                       IReadOnlyList<PitSite> sites)
        {
            var sb = new StringBuilder();
            if (selector is DefaultPitSiteSelector d) sb.Append(" (pitSize '").Append(d.ProfileKeyOf(settings)).Append('\'');
            else sb.Append(" (selector ").Append(selector.GetType().Name);
            if (savedSiteId != null) sb.Append(", saved site '").Append(savedSiteId).Append('\'');
            sb.Append("); available sites: ");
            for (int i = 0; i < sites.Count; i++)
                sb.Append(i == 0 ? "" : ", ").Append(sites[i].SiteId).Append(':').Append(sites[i].ProfileKey);
            return sb.ToString();
        }

        private static int CompareCandidates(Candidate a, Candidate b)
        {
            int c = string.CompareOrdinal(a.Site.SiteId, b.Site.SiteId);
            if (c != 0) return c;
            c = a.BuildIndex.CompareTo(b.BuildIndex);
            if (c != 0) return c;
            int n = Mathf.Min(a.Path.Length, b.Path.Length);
            for (int i = 0; i < n; i++)
                if (a.Path[i] != b.Path[i]) return a.Path[i].CompareTo(b.Path[i]);
            return a.Path.Length.CompareTo(b.Path.Length);
        }

        // Sibling indices from the scene root down (the root's index is its order among the scene's roots).
        private static int[] SiblingPath(Transform t)
        {
            var path = new List<int>();
            for (; t != null; t = t.parent) path.Add(t.GetSiblingIndex());
            path.Reverse();
            return path.ToArray();
        }
    }

    /// <summary>
    /// Standalone venue preview (design b 9.3, kept per file plan R10): Playing a venue scene without the kit builds the
    /// pit at the scene's site, so artists see the fill in the pool. Installed into the dev harness's hook before every
    /// scene load; the harness calls it before every standalone build (Start, R, T, 1-5).
    /// </summary>
    public static class PitSiteStandalone
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() => BallPitFinalController.StandalonePrepare = TryPrepare;

        /// <summary>False (env null) for a scene without usable sites: the dev scene runs exactly as today.</summary>
        public static bool TryPrepare(PitSettings settings, out IPitEnvironment env)
        {
            env = null;
            if (settings == null) return false;
            List<PitSite> sites = PitSites.FindAll();
            if (sites.Count == 0) return false;

            VenueConfig config = VenueConfig.Load();
            PitSite site = null;
            PitSizeProfile profile = null;
            for (int i = 0; i < sites.Count && site == null; i++)
                if (config.TryGetProfile(sites[i].ProfileKey, out PitSizeProfile p) && p.pitX == settings.pitX &&
                    p.pitY == settings.pitY && p.pitZ == settings.pitZ && p.ballDiameter == settings.ballDiameter)
                { site = sites[i]; profile = p; }
            if (site == null)
            {
                site = sites[0];
                bool known = config.TryGetProfile(site.ProfileKey, out profile);
                Debug.LogWarning($"[PitSiteStandalone] no PitSite's profile matches the inspector pit {settings.pitX}x{settings.pitY}x" +
                                 $"{settings.pitZ} ({settings.ballDiameter} m balls); previewing site '{site.SiteId}' " +
                                 (known ? $"with its profile '{profile.key}'" : $"at the inspector size (profile '{site.ProfileKey}' is missing)"), site);
            }
            if (!site.TryApplyTo(settings, profile, out string error))
            {
                Debug.LogError("[PitSiteStandalone] " + error + "; running the dev scene", site);
                return false;
            }
            PitSites.Activate(site, sites);
            env = new PitSiteEnvironment(site);
            return true;
        }
    }
}
