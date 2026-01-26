namespace EpicNet
{
    /// <summary>
    /// Contains metadata about a received network message, including the sender
    /// and the timestamp when it was sent.
    /// </summary>
    /// <remarks>
    /// This struct is passed to RPC methods and <see cref="IEpicObservable.OnEpicSerializeView"/>
    /// to provide context about the incoming data.
    /// </remarks>
    public struct EpicMessageInfo
    {
        /// <summary>
        /// The player who sent this message. May be null if the sender has disconnected.
        /// </summary>
        public EpicPlayer Sender { get; set; }

        /// <summary>
        /// The local timestamp (in seconds since game start) when this message was received.
        /// Use for lag compensation or interpolation timing.
        /// </summary>
        public double Timestamp { get; set; }
    }
}