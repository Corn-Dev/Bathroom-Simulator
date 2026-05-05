using UnityEngine;
using Unity.Netcode;

public class EnableCamera : NetworkBehaviour
{
    private Camera playerCamera;
    private AudioListener playerAudioListener;

    void Awake()
    {
        // Get the Camera component attached to this object (or child)
        playerCamera = GetComponentInChildren<Camera>(true);
        playerAudioListener = playerCamera.GetComponent<AudioListener>();

        // Disable by default
        if (playerCamera != null)
            playerCamera.enabled = false;
            playerAudioListener.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner && playerCamera != null)
        {
            // Enable only for the local player
            playerCamera.enabled = true;
            playerAudioListener.enabled = true;
        }
    }
}