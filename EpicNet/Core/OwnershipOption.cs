using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace EpicNet
{
    /// <summary>
    /// Ownership transfer options
    /// </summary>
    public enum OwnershipOption
    {
        Fixed,
        Takeover,
        Request
    }
}