using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace EpicNet
{
    /// <summary>
    /// Attribute to mark methods as RPC callable
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class EpicRPC : Attribute { }
}
