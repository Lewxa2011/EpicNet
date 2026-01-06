using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace EpicNet
{
    /// <summary>
    /// Represents a player in the network - equivalent to Photon.Realtime.Player
    /// </summary>
    public class EpicPlayer
    {
        public ProductUserId UserId { get; private set; }
        public string NickName { get; set; }
        public int ActorNumber { get; private set; }
        public bool IsMasterClient { get; set; }
        public bool IsLocal => UserId == EpicNetwork.LocalPlayer?.UserId;
        public Dictionary<string, object> CustomProperties { get; private set; }

        public EpicPlayer(ProductUserId userId, string nickName)
        {
            UserId = userId;
            NickName = nickName;
            CustomProperties = new Dictionary<string, object>();
            ActorNumber = GenerateActorNumber();
        }

        private static int _actorCounter = 1;
        private static int GenerateActorNumber() => _actorCounter++;

        /// <summary>
        /// Reset the actor counter (call when leaving a room or logging out)
        /// </summary>
        internal static void ResetActorCounter() => _actorCounter = 1;

        public void SetCustomProperties(Dictionary<string, object> properties)
        {
            foreach (var kvp in properties)
            {
                CustomProperties[kvp.Key] = kvp.Value;
            }
        }

        public override string ToString() => $"Player {NickName} ({ActorNumber})";
    }
}