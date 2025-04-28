using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using UnityEngine;

[RegisterTypeInIl2Cpp]
public class VoiceSource : MonoBehaviour
{
    public Voice.VoicePlaybackBuffer PlaybackBuffer;

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (PlaybackBuffer == null || PlaybackBuffer.Buffer == null) return;

        for (int i = 0; i < data.Length; i += channels)
        {
            data[i] = PlaybackBuffer.Buffer[PlaybackBuffer.ReadIndex];
            if (channels == 2)
                data[i + 1] = data[i]; // Duplicate for stereo

            PlaybackBuffer.ReadIndex = (PlaybackBuffer.ReadIndex + 1) % PlaybackBuffer.BufferLength;
        }
    }
}