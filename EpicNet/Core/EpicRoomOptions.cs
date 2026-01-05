using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace EpicNet
{
    /// <summary>
    /// Room creation options - equivalent to RoomOptions in PUN2
    /// </summary>
    public class EpicRoomOptions
    {
        public int MaxPlayers { get; set; } = 20;
        public bool IsVisible { get; set; } = true;
        public bool IsOpen { get; set; } = true;
        public string Password { get; set; } = null;
        public Dictionary<string, object> CustomRoomProperties { get; set; }
        public string[] CustomRoomPropertiesForLobby { get; set; }

        public bool HasPassword => !string.IsNullOrEmpty(Password);

        public EpicRoomOptions()
        {
            CustomRoomProperties = new Dictionary<string, object>();
        }
    }
}