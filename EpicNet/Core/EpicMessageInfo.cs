using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace EpicNet
{
    /// <summary>
    /// Message info for network messages
    /// </summary>
    public struct EpicMessageInfo
    {
        public EpicPlayer Sender { get; set; }
        public double Timestamp { get; set; }
    }
}