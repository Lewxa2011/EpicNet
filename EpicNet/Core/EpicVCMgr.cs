
using UnityEngine;
using System.Collections.Generic;

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

        public const SampleRate sr = SampleRate.TwentyFourKHz;

        public const float VoiceActivationLevel = 0.07f;

        public static void Initialize()
        {
            if (Microphone.devices.Length > 0)
            {
                CurrentDevice = Microphone.devices[0];
                Debug.Log($"EpicNet VC: Initialized with device {CurrentDevice}");
            }
            else
            {
                Debug.LogError("EpicNet VC: No microphone detected!");
            }
        }
    }
}