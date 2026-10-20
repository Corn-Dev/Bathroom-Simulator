// In-session pit rebuild seams for BallPitFinalController (new partial, Deliverable D / CampaignDirector). Pit code: names
// no gameplay type. The existing three controller files are untouched; everything here reaches their private state because
// it is the same class. Host/offline/dev host rebuild the pit in place (RebuildActivePit); a session client tears its world
// down (DisposeClientPitForRebuild), re-points at the new entry's site (RebindClientSite) and lets the existing Update path
// (netPit.HeaderPending && world == null) build from the host's re-issued header.

using System;
using UnityEngine;

namespace BallPitFinal
{
    public sealed partial class BallPitFinalController
    {
        /// <summary>
        /// Host / offline / dev host only. Rebuilds the pit in place for the next campaign entry: BuildRoutine disposes the
        /// old pit (PitWorldDisposed), builds the new one, then OnCampaignRebuilt rebinds NetPit and raises PitWorldBuilt.
        /// Does NOT detach the session or touch NetPit's registration. onBuilt(false, why) on any refusal or failure.
        /// </summary>
        public void RebuildActivePit(PitSettings newSettings, Bounds newBounds, IPitEnvironment newEnv, IPitSite newSite,
                                     Camera newOverview, Action<bool, string> onBuilt)
        {
            if (newSettings == null) { Debug.LogError("[BallPitFinal] RebuildActivePit: no settings"); onBuilt?.Invoke(false, "no settings"); return; }
            if (mode == Mode.Client) { Debug.LogError("[BallPitFinal] RebuildActivePit is host/offline only"); onBuilt?.Invoke(false, "client"); return; }
            if (building) { Debug.LogError("[BallPitFinal] RebuildActivePit: a build is already running"); onBuilt?.Invoke(false, "busy"); return; }
            netPit?.BeginRebuild();                 // BEFORE DisposePit (inside BuildRoutine): freezes NetPit's host paths while the old world dies
            settings = newSettings;                 // mirrors BeginHost: HUD and tools track the live pit
            Environment = newEnv;
            activeSite = newSite;
            overviewCamera = newOverview;
            StartCoroutine(BuildRoutine(newSettings, newBounds, true, (ok, msg) => OnCampaignRebuilt(ok, msg, onBuilt)));
        }

        // BuildRoutine's onBuilt for a campaign rebuild. BuildRoutine does not call MarkWorldReady when onBuilt != null, so this does.
        private void OnCampaignRebuilt(bool ok, string msg, Action<bool, string> cb)
        {
            if (!ok)
            {
                // NetPit stays 'rebuilding' (host paths frozen); v1 = logged error, no retry.
                Debug.LogError("[BallPitFinal] campaign rebuild failed: " + msg);
                cb?.Invoke(false, msg);
                return;
            }
            if (netPit != null && netPit.IsHost) netPit.RebindAuthority(authority); // dev host and session host
            if (SessionDriven) authority.ToolFilter = HostToolFilter;               // as OnHostBuilt does (session only)
            MarkWorldReady();                                                       // PitWorldBuilt -> gameplay listeners
            cb?.Invoke(true, null);
        }

        /// <summary>Client only. Reuses the private teardown: raises PitWorldDisposed and leaves world == null, so Update's
        /// `HeaderPending && world == null` rebuild guard fires once the host's header is accepted.</summary>
        public void DisposeClientPitForRebuild() => DisposePit();

        /// <summary>Client only. Re-points the site-dependent controller fields at the new entry's PitSite BEFORE
        /// NetPit.ResetClientWorldForRebuild, so the next StartClientBuild binds the right anchor and environment.</summary>
        public void RebindClientSite(IPitEnvironment env, IPitSite site, Camera overview, Action<PitSettings> prepareClientSettings)
        {
            Environment = env;
            activeSite = site;
            overviewCamera = overview;
            this.prepareClientSettings = prepareClientSettings;
        }
    }
}
