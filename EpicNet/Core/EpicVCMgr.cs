using UnityEngine;
using System;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace EpicNet
{
    /// <summary>
    /// Manages voice chat initialization and microphone permissions.
    /// Call <see cref="Initialize"/> before using voice chat features.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On Android, this manager handles runtime microphone permission requests.
    /// On other platforms, microphone access is assumed to be available.
    /// </para>
    /// <para>
    /// Subscribe to <see cref="OnInitialized"/> to know when voice chat is ready,
    /// and <see cref="OnPermissionDenied"/> if the user denies microphone access.
    /// </para>
    /// </remarks>
    public static class EpicVCMgr
    {
        #region Types

        /// <summary>
        /// Audio sample rates supported by the Opus codec.
        /// </summary>
        public enum SampleRate : int
        {
            /// <summary>8 kHz - Lowest quality, minimal bandwidth.</summary>
            EightKHz = 8000,
            /// <summary>12 kHz - Low quality.</summary>
            TwelveKHz = 12000,
            /// <summary>16 kHz - Standard telephony quality.</summary>
            SixteenKHz = 16000,
            /// <summary>24 kHz - Good quality, recommended for voice chat.</summary>
            TwentyFourKHz = 24000,
            /// <summary>48 kHz - Highest quality, more bandwidth.</summary>
            FortyEightKHz = 48000
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// The currently selected microphone device name.
        /// </summary>
        public static string CurrentDevice { get; private set; }

        /// <summary>
        /// True when the voice chat system is initialized and ready.
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// True if microphone permission has been granted.
        /// Always true on platforms that don't require runtime permissions.
        /// </summary>
        public static bool HasMicrophonePermission { get; private set; }

        #endregion

        #region Constants

        /// <summary>
        /// The sample rate used for voice chat encoding/decoding.
        /// </summary>
        public const SampleRate sr = SampleRate.TwentyFourKHz;

        /// <summary>
        /// RMS threshold for voice activity detection.
        /// Audio below this level is considered silence and not transmitted.
        /// </summary>
        public const float VoiceActivationLevel = 0.07f;

        #endregion

        #region Events

        /// <summary>
        /// Fired when voice chat is initialized and ready to use.
        /// </summary>
        public static event Action OnInitialized;

        /// <summary>
        /// Fired when the user denies microphone permission (Android only).
        /// </summary>
        public static event Action OnPermissionDenied;

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes the voice chat system and requests microphone permission if needed.
        /// Call this before creating any <see cref="EpicVC"/> components.
        /// </summary>
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

        /// <summary>
        /// Changes the active microphone device.
        /// </summary>
        /// <param name="deviceName">The name of the microphone device to use.</param>
        /// <returns>True if the device was found and set, false otherwise.</returns>
        public static bool SetDevice(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return false;

            foreach (var device in Microphone.devices)
            {
                if (device == deviceName)
                {
                    CurrentDevice = device;
                    Debug.Log($"[EpicNet VC] Switched to microphone: {device}");
                    return true;
                }
            }

            Debug.LogWarning($"[EpicNet VC] Microphone device not found: {deviceName}");
            return false;
        }

        /// <summary>
        /// Gets an array of all available microphone device names.
        /// </summary>
        public static string[] GetAvailableDevices()
        {
            return Microphone.devices;
        }

        #endregion

        #region Private Methods

#if UNITY_ANDROID
        private static void OnPermissionGranted(string permission)
        {
            if (permission == Permission.Microphone)
            {
                HasMicrophonePermission = true;
                Debug.Log("[EpicNet VC] Microphone permission granted");
                InitializeMicrophone();
            }
        }

        private static void OnPermissionDeniedCallback(string permission)
        {
            if (permission == Permission.Microphone)
            {
                HasMicrophonePermission = false;
                Debug.LogWarning("[EpicNet VC] Microphone permission denied - voice chat will be disabled");
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
                Debug.Log($"[EpicNet VC] Initialized with microphone: {CurrentDevice}");
                OnInitialized?.Invoke();
            }
            else
            {
                Debug.LogWarning("[EpicNet VC] No microphone detected - voice chat will be disabled");
            }
        }

        #endregion
    }
}