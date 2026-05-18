using UnityEngine;
using Unity.Netcode;
using Steamworks;
using System;

public class PlayerVoice : NetworkBehaviour
{
    [SerializeField] private bool verboseLogs;

    private AudioSource audioSource;
    private const int sampleRate = 48000;
    private const int bufferSize = 48000 * 10; // 10-second circular buffer
    private const int maxCompressedVoiceBytes = 8192;
    private float[] circularBuffer;
    private byte[] compressedBuffer;
    private byte[] pcmBuffer;
    private int writeOffset = 0;
    private int readOffset = 0;
    private int bufferCount = 0;
    private NetworkVoiceRouter voiceRouter;

    private void Awake()
    {
        circularBuffer = new float[bufferSize];
        compressedBuffer = new byte[maxCompressedVoiceBytes];
        pcmBuffer = new byte[bufferSize * 2];
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
        voiceRouter = GetComponent<NetworkVoiceRouter>();
    }

    public override void OnNetworkSpawn()
    {
        VoicePacketBus.OnVoicePacket += HandleVoicePacket;

        if (IsOwner && SteamManager.Initialized)
        {
            SteamUser.StartVoiceRecording();
        }
    }

    public override void OnNetworkDespawn()
    {
        VoicePacketBus.OnVoicePacket -= HandleVoicePacket;

        if (IsOwner && SteamManager.Initialized)
        {
            SteamUser.StopVoiceRecording();
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
            if (verboseLogs)
            {
                Debug.LogWarning($"[VOICE] GetAvailableVoice failed: {result}");
            }
            return;
        }

        if (compressedSize == 0) return;

        // Step 2: Pull compressed voice
        uint bytesWritten;
        uint bufferSizeToUse = Math.Min(compressedSize, (uint)compressedBuffer.Length);
        result = SteamUser.GetVoice(true, compressedBuffer, bufferSizeToUse, out bytesWritten);

        if (result != EVoiceResult.k_EVoiceResultOK)
        {
            if (verboseLogs)
            {
                Debug.LogWarning($"[VOICE] GetVoice failed: {result}");
            }
            return;
        }

        if (bytesWritten > 0)
        {
            byte[] payload = new byte[bytesWritten];
            Buffer.BlockCopy(compressedBuffer, 0, payload, 0, (int)bytesWritten);
            if (voiceRouter != null)
            {
                voiceRouter.SendVoiceServerRpc(payload, (int)bytesWritten);
            }
        }
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

    private void HandleVoicePacket(ulong sourceNetworkObjectId, byte[] compressed, int size, ulong ownerClientId)
    {
        if (IsOwner || sourceNetworkObjectId != NetworkObjectId)
        {
            return;
        }

        ProcessVoicePacket(compressed, size);
    }

    private void ProcessVoicePacket(byte[] compressed, int size)
    {
        if (compressed == null || size <= 0)
        {
            return;
        }

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
            if (verboseLogs)
            {
                Debug.LogWarning($"[VOICE] Decompress failed: {result}");
            }
            return;
        }

        if (pcmWritten == 0) return;

        int samples = (int)pcmWritten / 2;
        for (int i = 0; i < samples; i++)
        {
            float sample = BitConverter.ToInt16(pcmBuffer, i * 2) / 32768f;
            circularBuffer[writeOffset] = sample;
            writeOffset = (writeOffset + 1) % circularBuffer.Length;

            if (bufferCount < circularBuffer.Length)
            {
                bufferCount++;
            }
            else
            {
                readOffset = (readOffset + 1) % circularBuffer.Length;
            }
        }
    }
}
