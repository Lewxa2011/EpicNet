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
        public int MaxPlayers { get; set; } = 10;
        public bool IsVisible { get; set; } = true;
        public bool IsOpen { get; set; } = true;
        public Dictionary<string, object> CustomRoomProperties { get; set; }
        public string[] CustomRoomPropertiesForLobby { get; set; }

        public EpicRoomOptions()
        {
            CustomRoomProperties = new Dictionary<string, object>();
        }
    }
}