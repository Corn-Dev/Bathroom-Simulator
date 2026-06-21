using Steamworks;
using UnityEngine;

/// <summary>
/// TEST ONLY — Press F9 to grant a Test Bluegill to your Steam inventory.
/// Logs the result to the Unity console.
/// Remove this script before shipping.
/// </summary>
public class InventoryTest : MonoBehaviour
{
    private const int TEST_BLUEGILL_ITEMDEF = 1;

    private SteamInventoryResult_t currentResult;
    private Callback<SteamInventoryResultReady_t> resultReadyCallback;

    private void Start()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[InventoryTest] Steam not initialized.");
            enabled = false;
            return;
        }

        resultReadyCallback = Callback<SteamInventoryResultReady_t>.Create(OnInventoryResultReady);
        Debug.Log("[InventoryTest] Ready — press G to grant a Test Bluegill.");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            GrantTestFish();
        }
    }

    private void GrantTestFish()
    {
        Debug.Log("[InventoryTest] Attempting to grant Test Bluegill...");

        SteamItemDef_t[] itemDefs = new SteamItemDef_t[] { (SteamItemDef_t)TEST_BLUEGILL_ITEMDEF };
        uint[] quantities = new uint[] { 1 };

        bool success = SteamInventory.GenerateItems(
            out currentResult,
            itemDefs,
            quantities,
            (uint)itemDefs.Length
        );

        if (!success)
        {
            Debug.LogError("[InventoryTest] GenerateItems call failed immediately — check item def ID and Inventory Service status.");
        }
    }

    private void OnInventoryResultReady(SteamInventoryResultReady_t callback)
    {
        if (callback.m_handle != currentResult)
            return;

        if (callback.m_result == EResult.k_EResultOK)
        {
            Debug.Log("[InventoryTest] SUCCESS — Test Bluegill granted! Check your Steam inventory.");
        }
        else
        {
            Debug.LogError($"[InventoryTest] Grant failed with result: {callback.m_result}");
        }

        SteamInventory.DestroyResult(currentResult);
    }
}
