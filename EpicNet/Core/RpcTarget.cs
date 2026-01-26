namespace EpicNet
{
    /// <summary>
    /// Specifies the target recipients for an RPC call.
    /// </summary>
    public enum RpcTarget
    {
        /// <summary>
        /// Send to all players including the sender (reliable).
        /// </summary>
        All,

        /// <summary>
        /// Send to all players except the sender (reliable).
        /// </summary>
        Others,

        /// <summary>
        /// Send only to the master client (reliable).
        /// </summary>
        MasterClient,

        /// <summary>
        /// Send to all players and buffer for late joiners (reliable).
        /// </summary>
        AllBuffered,

        /// <summary>
        /// Send to all except sender and buffer for late joiners (reliable).
        /// </summary>
        OthersBuffered,

        /// <summary>
        /// Send to all players via the master client relay (reliable).
        /// </summary>
        AllViaServer,

        /// <summary>
        /// Send to all via master client and buffer for late joiners (reliable).
        /// </summary>
        AllBufferedViaServer,

        /// <summary>
        /// Send to all players including sender (unreliable, no ordering guarantee).
        /// Use for frequent, non-critical updates like position sync.
        /// </summary>
        AllUnreliable,

        /// <summary>
        /// Send to all except sender (unreliable, no ordering guarantee).
        /// Use for frequent, non-critical updates like position sync.
        /// </summary>
        OthersUnreliable,

        /// <summary>
        /// Send only to master client (unreliable, no ordering guarantee).
        /// </summary>
        MasterClientUnreliable
    }
}