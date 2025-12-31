using UnityEngine;
using System.Collections.Generic;

namespace EpicNet
{
    public static class EpicVCMgr
    {
        public static string CurrentDevice { get; private set; }
        public static bool IsPushToTalk { get; set; } = false;

        public const int SampleRate = 22050;

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

        public static bool IsTransmitting()
        {
            return true;
        }
    }
}