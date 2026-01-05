using UnityEngine;
using System;
using System.Collections;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace EpicNet
{
    public static class EpicVCMgr
    {
        public enum SampleRate : int
        {
            EightKHz = 8000,
            TwelveKHz = 12000,
            SixteenKHz = 16000,
            TwentyFourKHz = 24000,
            FortyEightKHz = 48000
        }

        public static string CurrentDevice { get; private set; }
        public static bool IsInitialized { get; private set; }
        public static bool HasMicrophonePermission { get; private set; }

        public const SampleRate sr = SampleRate.TwentyFourKHz;

        public const float VoiceActivationLevel = 0.07f;

        public static event Action OnInitialized;
        public static event Action OnPermissionDenied;

        public static void Initialize()
        {
#if UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += OnPermissionGranted;
                callbacks.PermissionDenied += OnPermissionDeniedCallback;
                callbacks.PermissionDeniedAndDontAskAgain += OnPermissionDeniedCallback;

                Permission.RequestUserPermission(Permission.Microphone, callbacks);
                return;
            }

            HasMicrophonePermission = true;
#else
            HasMicrophonePermission = true;
#endif
            InitializeMicrophone();
        }

#if UNITY_ANDROID
        private static void OnPermissionGranted(string permission)
        {
            if (permission == Permission.Microphone)
            {
                HasMicrophonePermission = true;
                Debug.Log("EpicNet VC: Microphone permission granted");
                InitializeMicrophone();
            }
        }

        private static void OnPermissionDeniedCallback(string permission)
        {
            if (permission == Permission.Microphone)
            {
                HasMicrophonePermission = false;
                Debug.LogWarning("EpicNet VC: Microphone permission denied - voice chat will be disabled");
                OnPermissionDenied?.Invoke();
            }
        }
#endif

        private static void InitializeMicrophone()
        {
            if (Microphone.devices.Length > 0)
            {
                CurrentDevice = Microphone.devices[0];
                IsInitialized = true;
                Debug.Log($"EpicNet VC: Initialized with device {CurrentDevice}");
                OnInitialized?.Invoke();
            }
            else
            {
                Debug.LogError("EpicNet VC: No microphone detected!");
            }
        }
    }
}