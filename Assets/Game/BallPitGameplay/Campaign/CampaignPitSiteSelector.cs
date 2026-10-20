// CampaignPitSiteSelector: the IPitSiteSelector the director installs on the adapter (Deliverable D; plan item 8). While a
// campaign pit instance is live it IS the answer, independent of the lobby's pitSize and of the save's siteId, so
// PitSites.SelectAndActivate's resume-divergence check agrees with it. With no live pit it delegates to the previous selector
// (freeplay / single-pit behaviour is unchanged).
using System.Collections.Generic;

namespace BallPitGameplay
{
    public sealed class CampaignPitSiteSelector : IPitSiteSelector
    {
        private readonly CampaignDirector director;
        private readonly IPitSiteSelector fallback;

        public CampaignPitSiteSelector(CampaignDirector director, IPitSiteSelector fallback)
        {
            this.director = director;
            this.fallback = fallback;
        }

        public PitSite Select(in PitSiteQuery query)
        {
            PitSite live = director != null ? director.LiveSite : null;
            IReadOnlyList<PitSite> sites = query.Sites;
            if (live != null && sites != null)
                for (int i = 0; i < sites.Count; i++)
                    if (ReferenceEquals(sites[i], live)) return live;
            return fallback != null ? fallback.Select(in query) : null;
        }
    }
}
