using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Draws a fishing line from the owner's rod tip to this bobber.
/// Runs on ALL clients so everyone can see the line.
/// Attach to the Bobber prefab.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FishingLineRenderer : NetworkBehaviour
{
    [SerializeField] private float lineWidth = 0.003f;

    private LineRenderer line;
    private Transform ownerRodTip;

    public override void OnNetworkSpawn()
    {
        // Create LineRenderer on all clients
        line = gameObject.AddComponent<LineRenderer>();
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.positionCount = 2;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = Color.white;
        line.endColor = Color.white;

        // Find the owner's rod tip
        FindOwnerRodTip();
    }

    private void FindOwnerRodTip()
    {
        // Find the FishingController belonging to our owner
        foreach (var fc in FindObjectsByType<FishingController>(FindObjectsSortMode.None))
        {
            var netObj = fc.GetComponent<NetworkObject>();
            if (netObj != null && netObj.OwnerClientId == OwnerClientId)
            {
                // Get the rod tip from the same player
                var rodTipField = fc.GetRodTip();
                if (rodTipField != null)
                {
                    ownerRodTip = rodTipField;
                }
                break;
            }
        }
    }

    private void LateUpdate()
    {
        if (line == null) return;

        if (ownerRodTip == null)
        {
            FindOwnerRodTip();
            if (ownerRodTip == null) return;
        }

        line.SetPosition(0, ownerRodTip.position);
        line.SetPosition(1, transform.position);
    }
}
