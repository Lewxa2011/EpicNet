using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace EpicNet
{
    /// <summary>
    /// Interface for observable components
    /// </summary>
    public interface IEpicObservable
    {
        void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info);
    }
}