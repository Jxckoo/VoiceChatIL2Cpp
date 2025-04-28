using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppScheduleOne.Audio;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Multiplayer;
using Il2CppScheduleOne.UI.Settings;
using Il2CppSteamworks;
using MelonLoader;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using static VoiceChatSliderInjector;

[assembly: MelonInfo(typeof(VoiceChatIL2Cpp.Core), "VoiceChat", "1.0.1", "Jxckooo", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace VoiceChatIL2Cpp
{
    public class Core : MelonMod
    {
        private Task receiveTask;
        private Task sendTask;
        private static CSteamID _currentLobbyId = CSteamID.Nil;

        public static CSteamID CurrentLobbyId
        {
            get => _currentLobbyId;
            set
            {
                if (_currentLobbyId != value)
                {
                    Logger.Log($"Current lobby changed from {_currentLobbyId} to {value}");
                    _currentLobbyId = value;
                    OnCurrentLobbyChanged?.Invoke(value);
                }
            }
        }

        public static event Action<CSteamID> OnCurrentLobbyChanged;

        public override void OnInitializeMelon()
        {
            Logger.Log("Initialized Voice Chat.");
        }

        public override void OnUpdate()
        {
            if(Voice.isInit)
            {
                if (Input.GetKeyDown(KeyCode.C))
                {
                    Voice.StartVoiceRecording();
                }
                if (Input.GetKeyUp(KeyCode.C))
                {
                    Voice.StopVoiceRecording();
                }

                InjectSliderIfNeeded();
            }
        }

        public override void OnLateUpdate()
        {
            if (Voice.isInit)
            {
                if (receiveTask == null || receiveTask.IsCompleted)
                {
                    receiveTask = ReceiveAndProcessVoiceDataAsync();
                }

                Voice.UpdateSilence();

                if (sendTask == null || sendTask.IsCompleted)
                {
                    sendTask = CaptureAndSendVoiceDataAsync();
                }
            }
        }

        private async Task ReceiveAndProcessVoiceDataAsync()
        {
            await Task.Yield();
            await Voice.ReceiveAndProcessVoiceDataAsync();
        }

        private async Task CaptureAndSendVoiceDataAsync()
        {
            await Task.Yield();
            Voice.CaptureAndSendVoiceData();
        }

        private bool hasInjectedMainMenu = false;
        private bool hasInjectedInGame = false;

        private void InjectSliderIfNeeded()
        {
            if ((hasInjectedInGame && !Lobby.Instance.IsInLobby) || (Lobby.Instance.IsInLobby && hasInjectedMainMenu)) return;

            var mainMenuAudio = GameObject.Find("MainMenu/Settings/Content/Audio");
            if (mainMenuAudio != null && !hasInjectedMainMenu)
            {
                Logger.Log("[VoiceChat] Injecting into Main Menu Audio Settings");
                VoiceChatSliderInjector.InjectIntoContainer(mainMenuAudio.transform);
                hasInjectedMainMenu = true;
            }

            var inGameAudio = GameObject.Find("UI/PauseMenu/Container/SettingsScreen_Ingame/Content/Audio");
            if (inGameAudio != null && !hasInjectedInGame)
            {
                Logger.Log("[VoiceChat] Injecting into In-Game Audio Settings");
                VoiceChatSliderInjector.InjectIntoContainer(inGameAudio.transform);
                hasInjectedInGame = true;
            }
        }
    }

    [HarmonyPatch(typeof(Settings), "WriteAudioSettings")]
    public class SettingsManager_Save_Patch
    {
        public static void Postfix(Settings __instance)
        {
            PlayerPrefs.SetFloat("VoiceChatVolume", Voice.VoiceChatSettings.VoiceChatVolume);
            PlayerPrefs.Save();
            Logger.Log("[VoiceChat] Saved VoiceChatVolume to PlayerPrefs.");
        }
    }

    [HarmonyPatch(typeof(Settings), "ReadAudioSettings")]
    public class SettingsManager_Load_Patch
    {
        public static void Postfix(ref Il2CppScheduleOne.DevUtilities.AudioSettings __result)
        {
            if (PlayerPrefs.HasKey("VoiceChatVolume"))
            {
                Voice.VoiceChatSettings.VoiceChatVolume = PlayerPrefs.GetFloat("VoiceChatVolume");
                Logger.Log($"[VoiceChat] Loaded VoiceChatVolume: {Voice.VoiceChatSettings.VoiceChatVolume}");
            }
        }
    }
    
    [HarmonyPatch(typeof(LobbyInterface))]
    public static class LobbyInterfacePatches
    {
        [HarmonyPatch("UpdatePlayers")]
        [HarmonyPostfix]
        public static void ExpandPlayerSlots(LobbyInterface __instance)
        {
            
            try
            {
                if (!Voice.isInit)
                {
                    Logger.Log($"Initalising Voice Chat... Game loaded: {LoadManager.instance.IsGameLoaded}");
                    Voice.Initialize();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Encountered an issue: {ex.Message}");
            }
            
        }
    }
    
    [HarmonyPatch(typeof(Lobby))]
    public static class LobbyTrackingPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch("LeaveLobby")]
        public static void PrefixLeaveLobby(Lobby __instance)
        {
            if (__instance.IsInLobby)
            {
                Logger.Log($"Tracking lobby leave - clearing current lobby ID (was {Core.CurrentLobbyId})");
                Core.CurrentLobbyId = CSteamID.Nil;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnLobbyEntered")]
        public static void PostfixOnLobbyEntered(Lobby __instance, LobbyEnter_t result)
        {

            if ((EChatRoomEnterResponse)result.m_EChatRoomEnterResponse == EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                CSteamID newLobbyId = (CSteamID)result.m_ulSteamIDLobby;
                Core.CurrentLobbyId = newLobbyId;
                Logger.Log($"Tracking lobby join - new lobby ID: {newLobbyId}");
            }
            else
            {
                Logger.Log($"Failed to join lobby (response: {(EChatRoomEnterResponse)result.m_EChatRoomEnterResponse})");
                Core.CurrentLobbyId = CSteamID.Nil;
            }
        }

    }
}