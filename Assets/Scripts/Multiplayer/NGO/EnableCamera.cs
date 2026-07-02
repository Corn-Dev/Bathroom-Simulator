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
        if (playerCamera != null)
        {
            playerAudioListener = playerCamera.GetComponent<AudioListener>();
        }

        // Disable by default
        if (playerCamera != null)
        {
            playerCamera.enabled = false;
        }

        if (playerAudioListener != null)
        {
            playerAudioListener.enabled = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner && playerCamera != null)
        {
            // Enable only for the local player
            playerCamera.enabled = true;
            playerCamera.tag = "MainCamera";
            if (playerAudioListener != null)
            {
                playerAudioListener.enabled = true;
            }
        }
        else if (playerCamera != null)
        {
            // Non-owner: ensure camera is disabled and untagged
            playerCamera.enabled = false;
            playerCamera.tag = "Untagged";
        }
    }
}