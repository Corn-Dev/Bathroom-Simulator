// VenueValidator: Tools/Ball Pit/Validate Venue (deliverable A, file plan E2; design b 11.4 checks 1-21, d.2, d.9).
// Checks the loaded scenes against the VenueConfig with the same functions the runtime uses (PitSite.Validate,
// VenueMath, PitWorld statics, PlayerSpawnValidation, the spawner's template), logs one clickable entry per issue with
// its object as context, and one summary. Boxes and colliders only, never per ball. It never modifies the scene.
// Backlogged, not built in A: Measure Baked Fill Tops (file plan R11).
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BallPitFinal;
using UnityEditor;
using UnityEngine;

namespace BallPitGameplay
{
    internal static class VenueValidator
    {
        private const float IntrusionTolerance = 0.001f; // absolute: modeling precision on flush faces (b 11.4)
        private const int MaxRegisteredColliders = 64;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private sealed class Report
        {
            public int Errors, Warnings, Infos;
            public void Error(string m, Object ctx) { Errors++; Debug.LogError("[Validate Venue] " + m, ctx); }
            public void Warning(string m, Object ctx) { Warnings++; Debug.LogWarning("[Validate Venue] " + m, ctx); }
            public void Info(string m, Object ctx) { Infos++; Debug.Log("[Validate Venue] " + m, ctx); }
            public void Add(List<string> errors, List<string> warnings, string prefix, Object ctx)
            {
                if (errors != null) foreach (string e in errors) Error(prefix + e, ctx);
                if (warnings != null) foreach (string w in warnings) Warning(prefix + w, ctx);
            }
        }

        [MenuItem("Tools/Ball Pit/Validate Venue")]
        private static void Run()
        {
            var report = new Report();
            Physics.SyncTransforms(); // edit-mode overlap and ClosestPoint queries need the current transforms
            VenueConfig config = VenueConfig.Load();
            var controller = Object.FindObjectOfType<BallPitFinalController>(true);
            var spawner = Object.FindObjectOfType<PlayerSpawner>(true);
            PitSettings baseSettings = controller != null && controller.settings != null ? controller.settings : new PitSettings();
            int ballLayer = baseSettings.ballLayer;

            // 21. The Player and PlayerBody layers and the physics matrix (d.2), first.
            var errors = new List<string>();
            var warnings = new List<string>();
            PlayerLayers.Validate(ballLayer, errors, warnings);
            report.Add(errors, warnings, "", null);

            PitSite[] sites = Object.FindObjectsOfType<PitSite>(true);
            if (sites.Length == 0) report.Info("no PitSite: a site-less (legacy) scene", null);                                  // 1
            if (controller == null) report.Error("no BallPitFinalController in the loaded scenes; the adapter requires one", null); // 2
            if (spawner == null) report.Error("no PlayerSpawner in the loaded scenes: every session fails in OnSessionLive", null); // 2b
            foreach (PitSite site in sites)                                                                                       // 2c
            {
                if (site.SiteRoot == null) continue;
                if (controller != null && controller.transform.IsChildOf(site.SiteRoot.transform))
                    report.Error($"the BallPitFinalController lies under PitSite '{site.SiteId}'s siteRoot, which is deactivated at Play", controller);
                if (spawner != null && spawner.transform.IsChildOf(site.SiteRoot.transform))
                    report.Error($"the PlayerSpawner lies under PitSite '{site.SiteId}'s siteRoot, which is deactivated at Play", spawner);
            }

            errors.Clear(); warnings.Clear();                                                                                    // 3
            config.Validate(errors, warnings);
            report.Add(errors, warnings, $"VenueConfig '{config.name}': ", config);
            errors.Clear(); warnings.Clear();                                                                                    // 4 dup, 16
            PitSites.CollectCrossSiteProblems(sites, errors, warnings);
            report.Add(errors, warnings, "", null);
            if (sites.Length > 0)                                                                                                // 17
                foreach (PitSizeProfile p in config.Profiles)
                {
                    bool served = false;
                    foreach (PitSite s in sites) if (s.ProfileKey == p.key && PitSite.IsValidSiteId(s.SiteId)) { served = true; break; }
                    if (!served) report.Error($"VenueConfig profile '{p.key}' has no PitSite: the lobby offers it, and such sessions fail to start", config);
                }

            Collider[] sceneColliders = Object.FindObjectsOfType<Collider>(); // active objects, as RegisterSceneEnvironment sees them
            OutOfBoundsVolume[] volumes = Object.FindObjectsOfType<OutOfBoundsVolume>(true);
            SpawnTemplate template = spawner != null && spawner.TryGetTemplate(out SpawnTemplate t) ? t : SpawnTemplate.Default;
            float clearance = spawner != null ? spawner.Physics.spawnClearance : new PlayerPitPhysicsConfig().spawnClearance;
            foreach (PitSite site in sites) CheckSite(report, site, sites, config, baseSettings, sceneColliders, volumes, template, clearance);

            CheckScene(report, sites, sceneColliders, volumes, ballLayer);
            string summary = $"{report.Errors} errors, {report.Warnings} warnings, {report.Infos} info, {sites.Length} PitSites; checked " +
                             $"against VenueConfig '{config.name}'" + (config.IsBuiltInDefault ? " (built-in sizes: no Resources asset)" : "");
            if (report.Errors > 0) Debug.LogError("[Validate Venue] " + summary);
            else if (report.Warnings > 0) Debug.LogWarning("[Validate Venue] " + summary);
            else Debug.Log("[Validate Venue] " + summary);
        }

        // ------------------------------------------------------------------ Per site (checks 4-15, 18, 19, 19b)

        private static void CheckSite(Report report, PitSite site, PitSite[] sites, VenueConfig config, PitSettings baseSettings,
                                      Collider[] sceneColliders, OutOfBoundsVolume[] volumes, SpawnTemplate template, float clearance)
        {
            string at = $"PitSite '{site.SiteId}' ({PitSites.PathOf(site)}): ";
            var errors = new List<string>();
            var warnings = new List<string>();
            site.Validate(config, errors, warnings);                                                                             // 4, 9 rim 0
            report.Add(errors, warnings, at, site);
            int playerLayer = PlayerLayers.Player;
            if (site.ProxyLayer < 0 || site.ProxyLayer > 31 || site.ProxyLayer == baseSettings.ballLayer || (playerLayer >= 0 && site.ProxyLayer == playerLayer))
                report.Error(at + $"proxyLayer {site.ProxyLayer} is outside 0..31 or equals the ball or Player layer", site);   // 15
            AudioListener overviewListener = site.OverviewCamera != null ? site.OverviewCamera.GetComponent<AudioListener>() : null;
            if (site.OverviewCamera == null)                                                                                    // 19
                report.Warning(at + "no overviewCamera: the runtime shows a fallback loading camera", site);
            else if (site.OverviewCamera.enabled || (overviewListener != null && overviewListener.enabled))
                report.Warning(at + $"overviewCamera '{site.OverviewCamera.name}' (or its AudioListener) is enabled: it renders from scene " +
                               "load until site activation; leave it disabled", site.OverviewCamera);                         // 19b
            if (site.SiteRoot != null && !site.SiteRoot.activeInHierarchy)                                                       // 12
                report.Warning(at + "its siteRoot is inactive in edit mode, so its colliders, spawn points and volumes were not checked", site);
            if (site.Anchor == null || !config.TryGetProfile(site.ProfileKey, out PitSizeProfile profile)) return; // nothing to preview

            PitSettings s = VenueMath.PreviewSettings(site.AnchorPoint, profile, baseSettings);
            PitWorld.ComputeInterior(s, out float x0, out float x1, out float z0, out float z1, out float floorY);
            float d = s.ballDiameter;
            Bounds venue = site.WorldVenueBounds;
            Bounds proxies = VenueMath.PredictProxyBox(s, site.RimHeight);
            float rimTop = proxies.max.y;
            bool venueSet = venue.size.x > 0f && venue.size.y > 0f && venue.size.z > 0f;

            if (venueSet)
            {
                if (!Encloses(venue, proxies))                                                                                   // 5
                    report.Error(at + $"venueBounds {Fmt(venue.min)}..{Fmt(venue.max)} does not contain the pit and its proxies " +
                                 $"{Fmt(proxies.min)}..{Fmt(proxies.max)} (context menu: Fit Venue Bounds To Pit)", site);
                float side = Mathf.Min(Mathf.Min(proxies.min.x - venue.min.x, venue.max.x - proxies.max.x),
                                       Mathf.Min(proxies.min.z - venue.min.z, venue.max.z - proxies.max.z));
                if (side < 2f || venue.max.y - rimTop < 10f)                                                                     // 6
                    report.Warning(at + $"venueBounds leaves {F2(side)} m beside the pit (want 2) and {F2(venue.max.y - rimTop)} m above " +
                                   "the rim (want 10, for throw arcs)", site);
                long bytes = VenueMath.GridIndexBytes(venue.size, d);                                                            // 7
                if (bytes > VenueMath.MaxGridIndexBytes) report.Error(at + $"venueBounds needs {bytes >> 20} MB of grid index (limit {VenueMath.MaxGridIndexBytes >> 20} MB)", site);
                else if (bytes > VenueMath.WarnGridIndexBytes) report.Warning(at + $"venueBounds needs {bytes >> 20} MB of grid index", site);
                double cells = VenueMath.GridCellCount(venue.size, d);
                if (cells > PitWorld.MaxGridCells)
                    report.Error(at + $"venueBounds holds {cells.ToString("0", Inv)} grid cells at {F(d)} m balls, above the limit " +
                                 $"{PitWorld.MaxGridCells.ToString("0", Inv)}", site);
                float q16 = VenueMath.QuantumQ16(venue), q21 = VenueMath.QuantumQ21(venue);                                     // 8
                if (q21 > 0.01f * d) report.Error(at + $"rest-pose resolution {q21.ToString("0.#####", Inv)} m is above 1% of the ball diameter", site);
                if (q16 > 0.05f * d) report.Warning(at + $"moving-ball resolution {q16.ToString("0.####", Inv)} m is above 5% of the ball diameter", site);
            }

            float fccWallTop = PitWorld.PredictFccFillTop(s) + 2f * d, randomWallTop = VenueMath.EstimateRandomFillTop(s) + 2f * d; // 9
            if (site.RimHeight > 0f && floorY + site.RimHeight < fccWallTop)
                report.Error(at + $"the rim (+{F2(site.RimHeight)}) is below the FCC fill's WallTop (+{F2(fccWallTop - floorY)})", site);
            else if (site.RimHeight > 0f && floorY + site.RimHeight < randomWallTop)
                report.Warning(at + $"the rim (+{F2(site.RimHeight)}) is below the Random fill's estimated WallTop (+{F2(randomWallTop - floorY)})", site);

            CheckIntrusion(report, at, site, sites, s.ballLayer, x0, x1, z0, z1, floorY, rimTop);                                // 10, 11
            CheckRegistry(report, at, site, sites, sceneColliders, s.ballLayer, venueSet ? venue : PitWorld.ComputeBounds(s), d,
                          x0, x1, z0, z1, floorY, rimTop);                                                                       // 13

            errors.Clear(); warnings.Clear();                                                                                    // 14, 14b
            System.Func<Vector3, bool> forbidden = p =>
            {
                foreach (OutOfBoundsVolume v in volumes) if (v != null && v.enabled && v.AppliesTo(site) && v.Contains(p)) return true;
                return false;
            };
            PlayerSpawnValidation.Check(site, template.Radius, clearance, template.RequireInsidePlayBounds, forbidden, warnings, errors);
            report.Add(errors, warnings, at, site);

            ReportCache(report, at, site, profile, s, d, x0, x1, z0, z1, floorY, fccWallTop, randomWallTop, venueSet ? venue : PitWorld.ComputeBounds(s)); // 18
        }

        // A collider inside the interior (Error) or within 3 m above the rim (Warning); balls, players and other sites' art
        // (inactive while this site runs) are ignored. An overlap query sees the real geometry, so a hollow pool shell whose
        // triangles stay outside the box is not flagged.
        private static void CheckIntrusion(Report report, string at, PitSite site, PitSite[] sites, int ballLayer,
                                           float x0, float x1, float z0, float z1, float floorY, float rimTop)
        {
            const float tol = IntrusionTolerance;
            foreach (Collider c in Overlap(new Vector3(x0 + tol, floorY + tol, z0 + tol), new Vector3(x1 - tol, rimTop, z1 - tol)))
                if (Counts(c, site, sites, ballLayer))
                    report.Error(at + $"collider '{PitSites.PathOf(c)}' intrudes into the pit interior: remove it from the pool art " +
                                 "(the invisible proxies are the pit's floor and walls)", c);
            foreach (Collider c in Overlap(new Vector3(x0 + tol, rimTop, z0 + tol), new Vector3(x1 - tol, rimTop + 3f, z1 - tol)))
                if (Counts(c, site, sites, ballLayer))
                    report.Warning(at + $"collider '{PitSites.PathOf(c)}' is within 3 m above the pit opening (fine for bridges and rigs)", c);
        }

        private static Collider[] Overlap(Vector3 min, Vector3 max) =>
            max.x <= min.x || max.y <= min.y || max.z <= min.z ? new Collider[0]
            : Physics.OverlapBox((min + max) * 0.5f, (max - min) * 0.5f, Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);

        private static bool Counts(Collider c, PitSite site, PitSite[] sites, int ballLayer)
        {
            int layer = c.gameObject.layer;
            if (layer == ballLayer || (PlayerLayers.Player >= 0 && layer == PlayerLayers.Player)) return false;
            return !UnderOtherSiteRoot(c.transform, site, sites);
        }

        // 13: what RegisterSceneEnvironment would record for this site (the venue region filter of b 10.5).
        private static void CheckRegistry(Report report, string at, PitSite site, PitSite[] sites, Collider[] sceneColliders, int ballLayer,
                                          Bounds playBounds, float d, float x0, float x1, float z0, float z1, float floorY, float rimTop)
        {
            Bounds region = playBounds;
            region.Expand(4f * d);
            var interior = new Bounds();
            interior.SetMinMax(new Vector3(x0, floorY, z0), new Vector3(x1, rimTop, z1));
            int count = 0;
            foreach (Collider c in sceneColliders)
            {
                if (c == null || !c.enabled || c.isTrigger || !Counts(c, site, sites, ballLayer)) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                Bounds b = c.bounds;
                if (!b.Intersects(region)) continue;
                count++;
                if (Encloses(b, interior))
                    report.Warning(at + $"collider '{PitSites.PathOf(c)}' has bounds that contain the whole interior: every support test " +
                                   "near it runs the PhysX query (slower collapses)", c);
            }
            if (count > MaxRegisteredColliders)
                report.Warning(at + $"{count} colliders would register for this site (more than {MaxRegisteredColliders}): consider merging the venue's colliders", site);
        }

        // 18: the per-site summary, and whether the lobby-default Random fill (random, 4 colors, seed 12345) is baked where
        // the runtime looks for it (the anchored cache root, file plan M5).
        private static void ReportCache(Report report, string at, PitSite site, PitSizeProfile profile, PitSettings preview, float d,
                                        float x0, float x1, float z0, float z1, float floorY, float fccWallTop, float randomWallTop, Bounds venue)
        {
            PitSettings s = preview.Clone();
            s.fillType = FillType.Random;
            s.colorCount = 4;
            s.seed = new PitSettings().seed;
            if (site.CheckVenueBounds(s, out _)) { s.overridePlayBounds = true; s.playBoundsOverride = site.WorldVenueBounds; } // as TryApplyTo
            s.pitWalls = false;
            s.renderWalls = false;
            Bounds pb = PitWorld.ComputeBounds(s);
            string file = Path.Combine(BallPitFinalController.AnchoredCacheRoot(Application.streamingAssetsPath, s, pb), "BallPitFills", RandomFill.FileName(s));
            bool baked = File.Exists(file);
            double gridMb = VenueMath.GridIndexBytes(venue.size, d) / (1024.0 * 1024.0);
            string text = at + $"{profile.BallCount.ToString("n0", Inv)} balls, d {F(d)} m, interior {F2(x1 - x0)} x {F2(z1 - z0)} m, " +
                          $"floor y {floorY.ToString("0.000", Inv)}, FCC WallTop +{F2(fccWallTop - floorY)}, Random WallTop ~ +{F2(randomWallTop - floorY)}, " +
                          $"grid {gridMb.ToString("0.0", Inv)} MB; lobby-default Random fill " + (baked ? "baked" : "NOT baked") + $" ({file})";
            if (baked) report.Info(text, site);
            else report.Warning(text + ": the first session generates it; bake it in the editor before shipping", site);
        }

        // ------------------------------------------------------------------ Scene-wide (19c, 20, links, d.2)

        private static void CheckScene(Report report, PitSite[] sites, Collider[] sceneColliders, OutOfBoundsVolume[] volumes, int ballLayer)
        {
            var overviews = new HashSet<GameObject>();
            foreach (PitSite site in sites) if (site.OverviewCamera != null) overviews.Add(site.OverviewCamera.gameObject);
            foreach (Camera cam in Object.FindObjectsOfType<Camera>())                                                           // 19c
            {
                if (!cam.enabled || overviews.Contains(cam.gameObject)) continue;
                if (cam.targetTexture != null) report.Info($"camera '{PitSites.PathOf(cam)}' renders to a texture (an authored monitor)", cam);
                else report.Warning($"camera '{PitSites.PathOf(cam)}' is enabled: it renders alongside the loading view and the player's " +
                                    "camera; disable or remove it (a new scene's default Main Camera)", cam);
            }
            foreach (AudioListener l in Object.FindObjectsOfType<AudioListener>())
                if (l.enabled && !overviews.Contains(l.gameObject))
                    report.Warning($"AudioListener '{PitSites.PathOf(l)}' is enabled: only the player's Head listener should be", l);
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())                                                      // 20
                if (!rb.isKinematic && rb.gameObject.layer != ballLayer)
                    report.Warning($"Rigidbody '{PitSites.PathOf(rb)}' is not kinematic: session clients simulate locally, so it would " +
                                   "move independently on every peer", rb);
            int player = PlayerLayers.Player;                                                                                    // d.2
            if (player >= 0)
                foreach (Collider c in sceneColliders)
                    if (c.gameObject.layer == player && c.GetComponentInParent<PlayerAvatar>() == null)
                        report.Warning($"collider '{PitSites.PathOf(c)}' is on the Player layer: it holds balls but fails the support test, " +
                                       "so balls on it thaw and refreeze; use another layer", c);
            foreach (OutOfBoundsVolume v in volumes)                                                                             // links
                if (v.Site != null && !PitSite.IsValidSiteId(v.Site.SiteId))
                    report.Warning($"OutOfBoundsVolume '{PitSites.PathOf(v)}' is linked to PitSite '{v.Site.SiteId}', which has an invalid " +
                                   "siteId and is never active; the volume never applies", v);
            var colliders = new List<Collider>();
            foreach (ClimbableSurface surface in Object.FindObjectsOfType<ClimbableSurface>(true))
            {
                if (surface.Site != null && !PitSite.IsValidSiteId(surface.Site.SiteId))
                    report.Warning($"ClimbableSurface '{PitSites.PathOf(surface)}' is linked to PitSite '{surface.Site.SiteId}', which has an " +
                                   "invalid siteId and is never active; the surface is never climbable", surface);
                surface.GetColliders(colliders);
                if (colliders.Count == 0) report.Warning($"ClimbableSurface '{PitSites.PathOf(surface)}' marks no collider", surface);
            }
        }

        // ------------------------------------------------------------------ Helpers

        // Art under another site's siteRoot is inactive while this site runs (a root shared with this site is this site's).
        private static bool UnderOtherSiteRoot(Transform t, PitSite site, PitSite[] sites)
        {
            foreach (PitSite other in sites)
            {
                if (other == null || other == site || other.SiteRoot == null) continue;
                if (site.SiteRoot != null && other.SiteRoot == site.SiteRoot) continue;
                if (t.IsChildOf(other.SiteRoot.transform)) return true;
            }
            return false;
        }

        private static bool Encloses(Bounds outer, Bounds inner)
        {
            Vector3 a = outer.min, b = outer.max, c = inner.min, d = inner.max;
            return c.x >= a.x && c.y >= a.y && c.z >= a.z && d.x <= b.x && d.y <= b.y && d.z <= b.z;
        }

        private static string Fmt(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
        private static string F(float v) => v.ToString("0.###", Inv);
        private static string F2(float v) => v.ToString("0.00", Inv);
    }
}
