namespace EpicNet
{
    /// <summary>
    /// Specifies how ownership of a networked object can be transferred.
    /// </summary>
    public enum OwnershipOption
    {
        /// <summary>
        /// Ownership cannot be transferred. The original owner retains ownership permanently.
        /// </summary>
        Fixed,

        /// <summary>
        /// Any player can take ownership immediately without requesting permission.
        /// </summary>
        Takeover,

        /// <summary>
        /// Players must request ownership from the current owner, who can approve or deny.
        /// </summary>
        Request
    }
}