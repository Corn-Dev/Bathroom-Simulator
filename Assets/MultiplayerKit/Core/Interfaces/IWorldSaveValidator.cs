// Optional adapter interface: menu-time validation of a loaded world save. Design: 04 §9.3, 04 §6.1, 01 §4.8.
// Called by HostFromSave / ContinueLatest after IWorldSaveStore.Load, before any lobby exists.
// Not Ok → Toast with the save-load message for result.Error (SessionErrorMessages.ForLoadingSave),
// stay in MainMenu, nothing is created.

namespace MultiplayerKit
{
    public interface IWorldSaveValidator
    {
        /// <summary>SaveCorrupt or IncompatibleVersion on failure.</summary>
        KitResult ValidateWorldSave(WorldSaveData data);
    }
}
