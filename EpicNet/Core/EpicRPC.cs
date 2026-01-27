using System;

namespace EpicNet
{
    /// <summary>
    /// Security level for RPC calls - controls who can invoke the RPC.
    /// </summary>
    public enum RpcSecurityLevel
    {
        /// <summary>
        /// Anyone can call this RPC. Default for most gameplay RPCs.
        /// </summary>
        Anyone,

        /// <summary>
        /// Only the owner of the object can call this RPC.
        /// Good for actions that should only come from the controlling player.
        /// </summary>
        OwnerOnly,

        /// <summary>
        /// Only the master client can call this RPC.
        /// Good for authoritative game state changes.
        /// </summary>
        MasterClientOnly,

        /// <summary>
        /// Only the owner OR the master client can call this RPC.
        /// Allows both player control and host override.
        /// </summary>
        OwnerOrMasterClient
    }

    /// <summary>
    /// Marks a method as callable via Remote Procedure Call (RPC).
    /// Methods with this attribute can be invoked across the network using
    /// <see cref="EpicView.RPC"/> or <see cref="EpicNetwork.RPC"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RPC methods can have any visibility (public, private, protected) but must be
    /// instance methods on a MonoBehaviour attached to a GameObject with an EpicView.
    /// </para>
    /// <para>
    /// Supported parameter types: int, float, string, bool, Vector3, Quaternion, byte[].
    /// </para>
    /// <para>
    /// Optionally, add <see cref="EpicMessageInfo"/> as the last parameter to receive
    /// information about the sender.
    /// </para>
    /// <para>
    /// Use the <see cref="Security"/> property to restrict who can call the RPC.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Anyone can call this RPC
    /// [EpicRPC]
    /// private void PlaySound(string soundName) { }
    ///
    /// // Only the object owner can call this
    /// [EpicRPC(Security = RpcSecurityLevel.OwnerOnly)]
    /// private void TakeDamage(float damage) { }
    ///
    /// // Only the master client can call this
    /// [EpicRPC(Security = RpcSecurityLevel.MasterClientOnly)]
    /// private void StartRound() { }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class EpicRPC : Attribute
    {
        /// <summary>
        /// The security level for this RPC. Default is <see cref="RpcSecurityLevel.Anyone"/>.
        /// </summary>
        public RpcSecurityLevel Security { get; set; } = RpcSecurityLevel.Anyone;
    }
}
