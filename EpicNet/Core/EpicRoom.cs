using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace EpicNet
{
    /// <summary>
    /// Represents a room/lobby - equivalent to Photon.Realtime.Room
    /// </summary>
    public class EpicRoom
    {
        public string Name { get; private set; }
        public int PlayerCount => Players?.Count ?? 0;
        public int MaxPlayers { get; set; }
        public bool IsOpen { get; set; }
        public bool IsVisible { get; set; }
        public Dictionary<string, object> CustomProperties { get; private set; }
        public List<EpicPlayer> Players { get; private set; }

        public EpicRoom(string name)
        {
            Name = name;
            CustomProperties = new Dictionary<string, object>();
            Players = new List<EpicPlayer>();
            IsOpen = true;
            IsVisible = true;
            MaxPlayers = 10;
        }

        public void SetCustomProperties(Dictionary<string, object> properties)
        {
            foreach (var kvp in properties)
            {
                CustomProperties[kvp.Key] = kvp.Value;
            }
        }
    }
}