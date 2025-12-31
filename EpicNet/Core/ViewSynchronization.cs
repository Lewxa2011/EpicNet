using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace EpicNet
{
    /// <summary>
    /// View synchronization modes
    /// </summary>
    public enum ViewSynchronization
    {
        Off,
        ReliableDeltaCompressed,
        Unreliable,
        UnreliableOnChange
    }
}
