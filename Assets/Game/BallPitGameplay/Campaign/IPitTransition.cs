// IPitTransition: the cosmetic beat that covers the dispose -> rebuild hitch between shipment pits (Deliverable D;
// DESIGN-D §9.5, plan item 5). One coroutine spans the whole change: cover the screen, run the director's work, reveal.
// Future skins (garage door, a crate droned in) implement this; the director never cares which.
using System.Collections;

namespace BallPitGameplay
{
    public interface IPitTransition
    {
        /// <summary>One coroutine spanning dispose -> build: cover the screen, <c>yield return work</c> (the director's
        /// dispose + rebuild enumerator), then reveal. Cosmetic only: it must never be the thing that strands the rebuild.</summary>
        IEnumerator Play(IEnumerator work);
    }
}
