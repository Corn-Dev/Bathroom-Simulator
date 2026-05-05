using UnityEngine;
using Unity.Netcode;
using Steamworks;
using System;

public class PlayerVoice : NetworkBehaviour
{
    private AudioSource audioSource;
    private const int sampleRate = 48000;
    private const int bufferSize = 48000 * 10; // 10-second circular buffer
    private float[] circularBuffer;
    private int writeOffset = 0;
    private int readOffset = 0;
    private int bufferCount = 0;

    private void Awake()
    {
        circularBuffer = new float[bufferSize];
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.loop = true; // only to keep PCM callback feeding
        audioSource.clip = AudioClip.Create(
            "VoiceClip",
            bufferSize,
            1,
            sampleRate,
            true, // enable streaming
            OnAudioRead
        );
        audioSource.Play();
        Debug.Log("[VOICE] AudioSource and streaming clip initialized.");
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[VOICE] IsOwner={IsOwner} SteamManager.Initialized={SteamManager.Initialized}");
        if (IsOwner && SteamManager.Initialized)
        {
            SteamUser.StartVoiceRecording();
            Debug.Log("[VOICE] Started Steam voice recording.");
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Step 1: Check how many bytes are available
        uint compressedSize;
        EVoiceResult result = SteamUser.GetAvailableVoice(out compressedSize);

        if (result != EVoiceResult.k_EVoiceResultOK)
        {
            Debug.LogWarning($"[VOICE] GetAvailableVoice failed: {result}");
            return;
        }

        if (compressedSize == 0) return;

        // Step 2: Pull compressed voice
        byte[] compressed = new byte[compressedSize];
        uint bytesWritten;
        result = SteamUser.GetVoice(true, compressed, (uint)compressed.Length, out bytesWritten);

        if (result != EVoiceResult.k_EVoiceResultOK)
        {
            Debug.LogWarning($"[VOICE] GetVoice failed: {result}");
            return;
        }

        if (bytesWritten > 0)
            SendVoiceServerRpc(compressed, (int)bytesWritten);
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SendVoiceServerRpc(byte[] compressed, int size, ServerRpcParams serverRpcParams = default)
    {
        ReceiveVoiceClientRpc(compressed, size);
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ReceiveVoiceClientRpc(byte[] compressed, int size, ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner) return; // don’t play own voice

        // Decompress into temporary buffer
        byte[] pcmBuffer = new byte[bufferSize * 2];
        uint pcmWritten;
        EVoiceResult result = SteamUser.DecompressVoice(
            compressed,
            (uint)size,
            pcmBuffer,
            (uint)pcmBuffer.Length,
            out pcmWritten,
            sampleRate
        );

        if (result != EVoiceResult.k_EVoiceResultOK)
        {
            Debug.LogWarning($"[VOICE] Decompress failed: {result}");
            return;
        }

        if (pcmWritten == 0) return;

        // Convert PCM16 -> float and write into circular buffer
        int samples = (int)pcmWritten / 2;
        for (int i = 0; i < samples; i++)
        {
            float sample = BitConverter.ToInt16(pcmBuffer, i * 2) / 32768f;
            circularBuffer[writeOffset] = sample;
            writeOffset = (writeOffset + 1) % circularBuffer.Length;

            if (bufferCount < circularBuffer.Length)
                bufferCount++;
            else
                readOffset = (readOffset + 1) % circularBuffer.Length; // prevent overwrite
        }
        Debug.Log($"[VOICE] Wrote {samples} samples to circular buffer. WriteOffset={writeOffset} ReadOffset={readOffset} Occupancy={bufferCount}");
    }

    // Called by Unity to fill AudioSource clip
    private void OnAudioRead(float[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (bufferCount > 0)
            {
                data[i] = circularBuffer[readOffset];
                readOffset = (readOffset + 1) % circularBuffer.Length;
                bufferCount--;
            }
            else
            {
                data[i] = 0f; // silence
            }
        }
    }
}
