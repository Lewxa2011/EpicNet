using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// RPC target options
    /// </summary>
    public enum RpcTarget
    {
        All,
        Others,
        MasterClient,
        AllBuffered,
        OthersBuffered,
        AllViaServer,
        AllBufferedViaServer,
        // Unreliable variants - use for frequent, non-critical updates
        AllUnreliable,
        OthersUnreliable,
        MasterClientUnreliable
    }
}