using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;
using VoiceChatIL2Cpp;
using static UnityEngine.AudioClip;

public class Voice : MonoBehaviour
{
    public class VoicePlaybackBuffer
    {
        public AudioSource Source;
        public AudioClip Clip;
        public float[] Buffer;
        public int SampleRate;
        public int WriteIndex;
        public int ReadIndex;
        public int BufferLength;
        public float LastPacketTime;

        public Action<float[]> OnReadCallback;
    }


    public static bool isRecording = false;
    public static bool isDebugging = true;
    public static bool isInit = false;
    public static float voiceChatVolume = 1.5f;

    private static uint _optimalSampleRate = 0;
    private const float VoiceRange = 15f;
    private const float VoiceGain = 1.8f;
    private const int BufferSize = 1024;

    private static Dictionary<CSteamID, VoicePlaybackBuffer> playbackBuffers = new();
    private static Dictionary<CSteamID, SimpleCircularBuffer<byte[]>> voiceBuffers = new();
    private static HashSet<long> receivedSequences = new();

    public static void Initialize()
    {
        _optimalSampleRate = SteamUser.GetVoiceOptimalSampleRate();
        MainThreadDispatcher.RunOnMainThread(EnsureListenerExists);
        isInit = true;

        MelonLogger.Msg($"[Voice] Initialized with sample rate {_optimalSampleRate} Hz");
    }

    public static void SetAllSourceVolume(float value)
    {
        MainThreadDispatcher.RunOnMainThread(() =>
        {
            foreach (var kvp in playbackBuffers)
            {
                var buffer = kvp.Value;
                if (buffer.Source != null)
                    buffer.Source.volume = value;
            }
        });
    }

    public static void StartVoiceRecording()
    {
        if (!isRecording)
        {
            SteamUser.StartVoiceRecording();
            isRecording = true;
            Log("Voice recording started.");
        }
    }

    public static void StopVoiceRecording()
    {
        if (isRecording)
        {
            SteamUser.StopVoiceRecording();
            isRecording = false;
            Log("Voice recording stopped.");
        }
    }

    public static void CaptureAndSendVoiceData()
    {
        if (!isRecording) return;

        var result = SteamUser.GetAvailableVoice(out uint compressedSize);
        if (result != EVoiceResult.k_EVoiceResultOK || compressedSize == 0) return;

        var buffer = new Il2CppStructArray<byte>((long)(compressedSize * 2));
        result = SteamUser.GetVoice(true, buffer, (uint)buffer.Length, out uint bytesWritten);

        if (result != EVoiceResult.k_EVoiceResultOK || bytesWritten == 0) return;

        var voiceData = buffer.Take((int)bytesWritten).ToArray();

        byte[] sequence = BitConverter.GetBytes(DateTime.UtcNow.Ticks);
        byte[] packet = new byte[sequence.Length + voiceData.Length];
        Buffer.BlockCopy(sequence, 0, packet, 0, sequence.Length);
        Buffer.BlockCopy(voiceData, 0, packet, sequence.Length, voiceData.Length);

        var il2cppPacket = new Il2CppStructArray<byte>(packet.Length);
        for (int i = 0; i < packet.Length; i++)
            il2cppPacket[i] = packet[i];

        foreach (var peer in GetConnectedPeers())
            SteamNetworking.SendP2PPacket(peer, il2cppPacket, (uint)il2cppPacket.Length, EP2PSend.k_EP2PSendUnreliableNoDelay);
    }

    public static void ReceiveAndProcessVoiceData()
    {
        while (SteamNetworking.IsP2PPacketAvailable(out uint size))
        {
            var buffer = new Il2CppStructArray<byte>((long)size);
            if (SteamNetworking.ReadP2PPacket(buffer, size, out uint bytesRead, out CSteamID sender) && bytesRead > 8)
            {
                byte[] managedBytes = buffer.Take((int)bytesRead).ToArray();
                long seq = BitConverter.ToInt64(managedBytes, 0);
                if (receivedSequences.Contains(seq)) continue;
                receivedSequences.Add(seq);

                byte[] voiceData = new byte[bytesRead - 8];
                Buffer.BlockCopy(managedBytes, 8, voiceData, 0, voiceData.Length);

                if (!voiceBuffers.ContainsKey(sender))
                    voiceBuffers[sender] = new SimpleCircularBuffer<byte[]>(BufferSize);

                voiceBuffers[sender].Enqueue(voiceData);
                ProcessBufferedPackets(sender);
            }
        }
    }

    private static void ProcessBufferedPackets(CSteamID sender)
    {
        if (!voiceBuffers.TryGetValue(sender, out var buffer)) return;

        while (buffer.Count > 0)
        {
            byte[] compressed = buffer.Dequeue();
            DecompressAndPlay(sender, compressed);
        }
    }

    private static void DecompressAndPlay(CSteamID sender, byte[] compressed)
    {
        var result = SteamUser.DecompressVoice(compressed, (uint)compressed.Length, null, 0, out uint size, _optimalSampleRate);
        if (result != EVoiceResult.k_EVoiceResultBufferTooSmall) return;

        var buffer = new Il2CppStructArray<byte>((long)size);
        result = SteamUser.DecompressVoice(compressed, (uint)compressed.Length, buffer, (uint)buffer.Length, out uint decompressed, _optimalSampleRate);
        if (result != EVoiceResult.k_EVoiceResultOK || decompressed == 0) return;

        byte[] pcmData = buffer.Take((int)decompressed).ToArray();
        PlayVoiceData(sender, pcmData, decompressed);
    }

    public static class VoiceChatSettings
    {
        public static float VoiceChatVolume = 2.0f;
    }

    public static void PlayVoiceData(CSteamID sender, byte[] data, uint size)
    {
        if (size < 2 || data == null) return;
        float now = Time.time;

        int sampleCount = (int)(size / 2);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(data, i * 2);
            samples[i] = Mathf.Clamp(sample / 32768f * VoiceGain, -1f, 1f);
        }

        MainThreadDispatcher.RunOnMainThread(() =>
        {
            if (!playbackBuffers.TryGetValue(sender, out var buffer))
            {
                var obj = new GameObject($"Voice_{sender}");
                var source = obj.AddComponent<AudioSource>();

                int clipSamples = (int)(_optimalSampleRate * 3f); // 3 seconds buffer
                var playbackBuffer = new VoicePlaybackBuffer();
                playbackBuffer.Buffer = new float[clipSamples];
                playbackBuffer.BufferLength = clipSamples;
                playbackBuffer.SampleRate = (int)_optimalSampleRate;
                playbackBuffer.WriteIndex = 0;
                playbackBuffer.ReadIndex = 0;
                playbackBuffer.LastPacketTime = now;

                void PCMReader(float[] data)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        data[i] = playbackBuffer.Buffer[playbackBuffer.ReadIndex];
                        playbackBuffer.ReadIndex = (playbackBuffer.ReadIndex + 1) % playbackBuffer.BufferLength;
                    }
                }

                playbackBuffer.OnReadCallback = PCMReader;

                PCMReaderCallback pcmReaderCallback = DelegateSupport.ConvertDelegate<PCMReaderCallback>(PCMReader);

                var clip = AudioClip.Create(
                    $"VoiceClip_{sender}",
                    clipSamples,
                    1,
                    (int)_optimalSampleRate,
                    true,
                    pcmReaderCallback,
                    null
                );

                source.spatialBlend = 1f;
                source.minDistance = 1f;
                source.maxDistance = 15f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.volume = 1.5f;
                source.loop = true;
                source.clip = clip;
                source.Play();

                playbackBuffer.Source = source;
                playbackBuffer.Clip = clip;
                playbackBuffers[sender] = playbackBuffer;

                var senderObj = FindPlayerObject(sender);
                if (senderObj != null)
                {
                    var player = senderObj.GetComponent<Player>();
                    if (player != null && player.Avatar != null)
                    {
                        source.transform.SetParent(player.Avatar.transform);
                        source.transform.localPosition = Vector3.zero;
                    }
                }
            }

            buffer = playbackBuffers[sender];
            buffer.LastPacketTime = now;

            foreach (var sample in samples)
            {
                buffer.Buffer[buffer.WriteIndex] = sample;
                buffer.WriteIndex = (buffer.WriteIndex + 1) % buffer.BufferLength;
            }
        });
    }

    private static void OnAudioRead(float[] data)
    {
        foreach (var kvp in playbackBuffers)
        {
            var buffer = kvp.Value;
            if (buffer.Source.clip != null && buffer.Source.isPlaying)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = buffer.Buffer[buffer.ReadIndex];
                    buffer.ReadIndex = (buffer.ReadIndex + 1) % buffer.BufferLength;
                }
                break;
            }
        }
    }

    private static void ClearPlaybackBuffers()
    {
        foreach (var buffer in playbackBuffers.Values)
        {
            if (buffer.Source != null)
            {
                buffer.Source.Stop();
                UnityEngine.Object.Destroy(buffer.Source.gameObject);
            }
        }
        playbackBuffers.Clear();
    }

    private static float SilenceTimeout = 0.3f;

    public static void UpdateSilence()
    {
        float now = Time.time;
        MainThreadDispatcher.RunOnMainThread(() =>
        {
            foreach (var kvp in playbackBuffers)
            {
                var buffer = kvp.Value;
                if (now - buffer.LastPacketTime > SilenceTimeout)
                {
                    buffer.Buffer = new float[buffer.BufferLength];
                    buffer.WriteIndex = 0;
                    buffer.ReadIndex = 0;
                }
            }
        });
    }

    private static void FillAudioClipData(VoicePlaybackBuffer buffer, float[] data)
    {
        int samplesToCopy = data.Length;
        for (int i = 0; i < samplesToCopy; i++)
        {
            if (buffer.ReadIndex != buffer.WriteIndex)
            {
                data[i] = buffer.Buffer[buffer.ReadIndex];
                buffer.ReadIndex = (buffer.ReadIndex + 1) % buffer.BufferLength;
            }
            else
            {
                // No new voice data -> fill silence
                data[i] = 0f;
            }
        }
    }

    private static GameObject FindPlayerObject(CSteamID sender)
    {
        foreach (var player in Player.PlayerList)
            if (player.PlayerCode == sender.ToString())
                return player.gameObject;

        return null;
    }

    private static void EnsureListenerExists()
    {
        if (UnityEngine.Object.FindObjectOfType<AudioListener>() == null)
        {
            var listener = new GameObject("AudioListener");
            listener.AddComponent<AudioListener>();
        }
        AudioListener.volume = 1f;
    }

    public static List<CSteamID> GetConnectedPeers()
    {
        var list = new List<CSteamID>();
        if (Core.CurrentLobbyId == CSteamID.Nil) return list;

        int count = SteamMatchmaking.GetNumLobbyMembers(Core.CurrentLobbyId);
        for (int i = 0; i < count; i++)
        {
            var id = SteamMatchmaking.GetLobbyMemberByIndex(Core.CurrentLobbyId, i);
            if (id != SteamUser.GetSteamID())
                list.Add(id);
        }

        return list;
    }

    private static void Log(string msg)
    {
        if (isDebugging) MelonLogger.Msg($"[Voice] {msg}");
    }
}


public class SimpleCircularBuffer<T>
{
    private T[] _buffer;
    private int _head;
    private int _tail;
    private int _count;
    private int _capacity;

    public int Count => _count;

    public SimpleCircularBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new T[capacity];
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    public void Enqueue(T item)
    {
        if (_count == _capacity)
        {
            _tail = (_tail + 1) % _capacity;
        }
        else
        {
            _count++;
        }
        _buffer[_head] = item;
        _head = (_head + 1) % _capacity;
    }

    public T Dequeue()
    {
        if (_count == 0) throw new InvalidOperationException("Buffer is empty");

        T value = _buffer[_tail];
        _tail = (_tail + 1) % _capacity;
        _count--;
        return value;
    }
}