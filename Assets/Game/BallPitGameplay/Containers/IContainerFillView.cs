// IContainerFillView: how a container shows how full it is (brief §4.1). The owner's art implements this (a liquid
// level, a bar, particles). The container calls SetFill(0..1) whenever the stored fraction of its colour changes. A
// default implementation (DefaultFillView) moves/scales a pivot so the game reads without custom art.
namespace BallPitGameplay
{
    public interface IContainerFillView
    {
        /// <summary>fraction in [0,1]: the share of this container's colour already sold.</summary>
        void SetFill(float fraction);
    }
}
