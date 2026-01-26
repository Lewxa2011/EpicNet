namespace EpicNet
{
    /// <summary>
    /// Specifies how an EpicView synchronizes data across the network.
    /// </summary>
    public enum ViewSynchronization
    {
        /// <summary>
        /// No automatic synchronization. Use manual RPCs for all data.
        /// </summary>
        Off,

        /// <summary>
        /// Reliable delivery with delta compression. Only changed data is sent.
        /// Best for important state that must arrive in order.
        /// </summary>
        ReliableDeltaCompressed,

        /// <summary>
        /// Unreliable delivery. Packets may be dropped or arrive out of order.
        /// Best for frequently updated, non-critical data like positions.
        /// </summary>
        Unreliable,

        /// <summary>
        /// Unreliable delivery, but only sends when values change.
        /// Reduces bandwidth for infrequently changing data.
        /// </summary>
        UnreliableOnChange
    }
}
