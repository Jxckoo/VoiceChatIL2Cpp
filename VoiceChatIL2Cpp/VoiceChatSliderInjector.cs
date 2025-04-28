using Il2CppScheduleOne.Audio;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI.Settings;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

public static class VoiceChatSliderInjector
{
    public static void InjectSlider()
    {
        try
        {
            var mainMenuAudio = GameObject.Find("MainMenu/Settings/Content/Audio");
            if (mainMenuAudio != null)
            {
                MelonLoader.MelonLogger.Msg("[VoiceChat] Found main menu audio settings.");
                InjectIntoContainer(mainMenuAudio.transform);
            }

            var inGameAudio = GameObject.Find("UI/PauseMenu/Container/SettingsScreen_Ingame/Content/Audio");
            if (inGameAudio != null)
            {
                MelonLoader.MelonLogger.Msg("[VoiceChat] Found in-game audio settings.");
                InjectIntoContainer(inGameAudio.transform);
            }

            if (mainMenuAudio == null && inGameAudio == null)
            {
                MelonLoader.MelonLogger.Warning("[VoiceChat] No settings container found for injection.");
            }
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Error($"[VoiceChat] Failed to inject voice slider: {ex}");
        }
    }

    public static void InjectIntoContainer(Transform container)
    {
        var ambienceSlider = container.GetComponentsInChildren<AudioSlider>(true)
            .FirstOrDefault(slider => slider.AudioType == EAudioType.Ambient);

        if (ambienceSlider == null)
        {
            MelonLoader.MelonLogger.Warning("[VoiceChat] Could not find Ambience slider to clone.");
            return;
        }

        var ambienceSliderParent = ambienceSlider.transform.parent.gameObject;

        var voiceSliderGO = UnityEngine.Object.Instantiate(ambienceSliderParent, ambienceSliderParent.transform.parent);
        voiceSliderGO.name = "VoiceChatSlider";

        var existingAudioSlider = voiceSliderGO.GetComponentInChildren<AudioSlider>();
        if (existingAudioSlider != null)
        {
            UnityEngine.Object.DestroyImmediate(existingAudioSlider);
        }

        var label = voiceSliderGO.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.text = "Voice Chat Volume";
        }
        else
        {
            MelonLoader.MelonLogger.Warning("[VoiceChat] Label not found in cloned slider object.");
        }

        var slider = voiceSliderGO.GetComponentInChildren<Slider>();
        if (slider != null)
        {
            slider.value = Voice.VoiceChatSettings.VoiceChatVolume;
            slider.onValueChanged.RemoveAllListeners();

            slider.onValueChanged = new Slider.SliderEvent();
            slider.onValueChanged.AddListener((Action<float>)(value =>
            {
                Voice.SetAllSourceVolume(value);

                Voice.VoiceChatSettings.VoiceChatVolume = value;
                // MelonLoader.MelonLogger.Msg($"[VoiceChat] Voice chat volume set to: {value}");
            }));
        }
        else
        {
            MelonLoader.MelonLogger.Warning("[VoiceChat] Slider component not found in cloned object.");
        }
    }

}