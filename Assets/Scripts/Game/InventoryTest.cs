using Steamworks;
using UnityEngine;

/// <summary>
/// TEST ONLY — Dev inventory tools.
/// G = grant a test Bluegill
/// Delete = clear ALL items from your Phish inventory
/// Remove this script before shipping.
/// </summary>
public class InventoryTest : MonoBehaviour
{
    private const int TEST_BLUEGILL_ITEMDEF = 1;

    private SteamInventoryResult_t currentResult;
    private Callback<SteamInventoryResultReady_t> resultReadyCallback;
    private bool waitingForClear = false;

    private void Start()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[InventoryTest] Steam not initialized.");
            enabled = false;
            return;
        }

        resultReadyCallback = Callback<SteamInventoryResultReady_t>.Create(OnInventoryResultReady);
        Debug.Log("[InventoryTest] Ready — G=grant fish, Delete=clear all inventory.");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            GrantTestFish();
        }

        if (Input.GetKeyDown(KeyCode.Delete))
        {
            ClearInventory();
        }
    }

    private void GrantTestFish()
    {
        Debug.Log("[InventoryTest] Granting Test Bluegill...");

        SteamItemDef_t[] itemDefs = new SteamItemDef_t[] { (SteamItemDef_t)TEST_BLUEGILL_ITEMDEF };
        uint[] quantities = new uint[] { 1 };

        bool success = SteamInventory.GenerateItems(
            out currentResult,
            itemDefs,
            quantities,
            (uint)itemDefs.Length
        );

        if (!success)
            Debug.LogError("[InventoryTest] GenerateItems failed.");
    }

    private void ClearInventory()
    {
        Debug.Log("[InventoryTest] Fetching inventory to clear...");
        waitingForClear = true;

        bool success = SteamInventory.GetAllItems(out currentResult);
        if (!success)
        {
            Debug.LogError("[InventoryTest] GetAllItems failed.");
            waitingForClear = false;
        }
    }

    private void OnInventoryResultReady(SteamInventoryResultReady_t callback)
    {
        if (callback.m_handle != currentResult)
            return;

        if (callback.m_result != EResult.k_EResultOK)
        {
            Debug.LogError($"[InventoryTest] Result failed: {callback.m_result}");
            SteamInventory.DestroyResult(currentResult);
            waitingForClear = false;
            return;
        }

        if (waitingForClear)
        {
            DestroyAllItems(currentResult);
            waitingForClear = false;
        }
        else
        {
            Debug.Log("[InventoryTest] SUCCESS — item granted.");
        }

        SteamInventory.DestroyResult(currentResult);
    }

    private void DestroyAllItems(SteamInventoryResult_t resultHandle)
    {
        uint itemCount = 0;
        SteamInventory.GetResultItems(resultHandle, null, ref itemCount);

        if (itemCount == 0)
        {
            Debug.Log("[InventoryTest] Inventory already empty.");
            return;
        }

        SteamItemDetails_t[] items = new SteamItemDetails_t[itemCount];
        SteamInventory.GetResultItems(resultHandle, items, ref itemCount);

        int destroyed = 0;
        for (int i = 0; i < itemCount; i++)
        {
            SteamInventoryResult_t consumeResult;
            bool success = SteamInventory.ConsumeItem(
                out consumeResult,
                items[i].m_itemId,
                items[i].m_unQuantity
            );

            if (success)
            {
                destroyed++;
                SteamInventory.DestroyResult(consumeResult);
            }
            else
            {
                Debug.LogWarning($"[InventoryTest] Failed to consume item {items[i].m_itemId}");
            }
        }

        Debug.Log($"[InventoryTest] Cleared {destroyed}/{itemCount} items from inventory.");
    }
}
