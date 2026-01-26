using UnityEngine;
using System;

namespace EpicNet
{
    /// <summary>
    /// Core component that identifies a networked object.
    /// Attach this to any GameObject that needs to be synchronized across the network.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every networked object must have exactly one EpicView component.
    /// The EpicView handles ownership, synchronization, and RPC routing.
    /// </para>
    /// <para>
    /// For scene objects (objects already in the scene, not instantiated at runtime),
    /// the master client automatically becomes the owner.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("EpicNet/Epic View")]
    public class EpicView : MonoBehaviour
    {
        #region Public Properties

        /// <summary>
        /// The unique network identifier for this view.
        /// Assigned automatically when instantiated via <see cref="EpicNetwork.Instantiate"/>.
        /// </summary>
        public int ViewID { get; internal set; }

        /// <summary>
        /// The player who owns this networked object.
        /// The owner has authority over this object's state.
        /// </summary>
        public EpicPlayer Owner { get; internal set; }

        /// <summary>
        /// True if the local player owns this object.
        /// Use this to determine whether to process input or send updates.
        /// </summary>
        public bool IsMine => Owner != null && Owner.IsLocal;

        /// <summary>
        /// The prefab name used to instantiate this object (from Resources folder).
        /// Null for scene objects.
        /// </summary>
        public string PrefabName { get; internal set; }

        /// <summary>
        /// True if this object was placed in the scene (not instantiated at runtime).
        /// Scene objects are automatically owned by the master client.
        /// </summary>
        public bool IsSceneObject { get; private set; }

        /// <summary>
        /// The ownership transfer policy for this view.
        /// </summary>
        public OwnershipOption OwnershipTransfer => ownershipTransfer;

        /// <summary>
        /// The synchronization mode for this view.
        /// </summary>
        public ViewSynchronization Synchronization => synchronization;

        #endregion

        #region Serialized Fields

        [SerializeField]
        [Tooltip("Controls how ownership of this object can be transferred between players.")]
        private OwnershipOption ownershipTransfer = OwnershipOption.Takeover;

        [SerializeField]
        [Tooltip("Controls how data is synchronized across the network.")]
        private ViewSynchronization synchronization = ViewSynchronization.ReliableDeltaCompressed;

        #endregion

        #region Private Fields

        private bool _registeredForRoomJoin;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // If we're not in a room yet, this is a scene object
            if (!EpicNetwork.InRoom)
            {
                IsSceneObject = true;
                EpicNetwork.OnJoinedRoom += OnRoomJoined;
                _registeredForRoomJoin = true;
            }
        }

        private void OnRoomJoined()
        {
            if (!IsSceneObject) return;

            // Assign scene objects to the master client
            if (EpicNetwork.MasterClient == null)
            {
                Debug.LogWarning($"[EpicNet] Cannot assign scene object '{gameObject.name}' - no master client yet");
                return;
            }

            Owner = EpicNetwork.MasterClient;
            ViewID = EpicNetwork.RegisterSceneObject(this);

            Debug.Log($"[EpicNet] Scene object '{gameObject.name}' assigned to master client with ViewID {ViewID}");
        }

        private void OnDestroy()
        {
            if (_registeredForRoomJoin)
            {
                EpicNetwork.OnJoinedRoom -= OnRoomJoined;
            }
            if (ViewID != 0)
            {
                EpicNetwork.UnregisterNetworkObject(ViewID);
            }
        }

        #endregion

        #region RPC Methods

        /// <summary>
        /// Calls a Remote Procedure Call on this view.
        /// </summary>
        /// <param name="methodName">
        /// The name of the method to call. Must have the <see cref="EpicRPC"/> attribute.
        /// </param>
        /// <param name="target">Which players should receive this RPC.</param>
        /// <param name="parameters">Parameters to pass to the method.</param>
        /// <example>
        /// <code>
        /// // Calling the RPC
        /// view.RPC("TakeDamage", RpcTarget.All, 25f);
        ///
        /// // The method being called
        /// [EpicRPC]
        /// void TakeDamage(float damage) { health -= damage; }
        /// </code>
        /// </example>
        public void RPC(string methodName, RpcTarget target, params object[] parameters)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                Debug.LogError("[EpicNet] RPC method name cannot be null or empty");
                return;
            }
            EpicNetwork.RPC(this, methodName, target, parameters);
        }

        /// <summary>
        /// Calls an RPC on this view targeting a specific player (reliable delivery).
        /// </summary>
        /// <param name="methodName">The name of the method to call.</param>
        /// <param name="targetPlayer">The specific player to receive this RPC.</param>
        /// <param name="parameters">Parameters to pass to the method.</param>
        public void RPC(string methodName, EpicPlayer targetPlayer, params object[] parameters)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                Debug.LogError("[EpicNet] RPC method name cannot be null or empty");
                return;
            }
            if (targetPlayer == null)
            {
                Debug.LogError("[EpicNet] Target player cannot be null");
                return;
            }
            EpicNetwork.RPC(this, methodName, targetPlayer, parameters);
        }

        /// <summary>
        /// Calls an RPC on this view targeting a specific player with reliability option.
        /// </summary>
        /// <param name="methodName">The name of the method to call.</param>
        /// <param name="targetPlayer">The specific player to receive this RPC.</param>
        /// <param name="reliable">
        /// True for reliable ordered delivery, false for unreliable (faster but may be lost).
        /// </param>
        /// <param name="parameters">Parameters to pass to the method.</param>
        public void RPC(string methodName, EpicPlayer targetPlayer, bool reliable, params object[] parameters)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                Debug.LogError("[EpicNet] RPC method name cannot be null or empty");
                return;
            }
            if (targetPlayer == null)
            {
                Debug.LogError("[EpicNet] Target player cannot be null");
                return;
            }
            EpicNetwork.RPC(this, methodName, targetPlayer, reliable, parameters);
        }

        #endregion

        #region Ownership Methods

        /// <summary>
        /// Transfers ownership of this view to another player.
        /// Only works if <see cref="ownershipTransfer"/> is not <see cref="OwnershipOption.Fixed"/>.
        /// </summary>
        /// <param name="newOwner">The player to transfer ownership to.</param>
        public void TransferOwnership(EpicPlayer newOwner)
        {
            if (ownershipTransfer == OwnershipOption.Fixed)
            {
                Debug.LogWarning("[EpicNet] Cannot transfer ownership - OwnershipOption is Fixed");
                return;
            }

            if (newOwner == null)
            {
                Debug.LogWarning("[EpicNet] Cannot transfer ownership to null player");
                return;
            }

            var oldOwner = Owner;
            Owner = newOwner;

            Debug.Log($"[EpicNet] Ownership of ViewID {ViewID} transferred from {oldOwner?.NickName ?? "none"} to {newOwner.NickName}");

            // Notify network about ownership change
            EpicNetwork.SendOwnershipTransfer(ViewID, newOwner);
        }

        /// <summary>
        /// Requests ownership of this view from the current owner.
        /// Behavior depends on the <see cref="ownershipTransfer"/> setting.
        /// </summary>
        public void RequestOwnership()
        {
            if (IsMine)
            {
                Debug.Log("[EpicNet] Already own this object");
                return;
            }

            if (ownershipTransfer == OwnershipOption.Fixed)
            {
                Debug.LogWarning("[EpicNet] Cannot request ownership - OwnershipOption is Fixed");
                return;
            }

            if (ownershipTransfer == OwnershipOption.Takeover)
            {
                // Takeover allows immediate ownership transfer
                TransferOwnership(EpicNetwork.LocalPlayer);
            }
            else if (ownershipTransfer == OwnershipOption.Request)
            {
                // Request requires approval from current owner
                if (Owner == null)
                {
                    Debug.LogWarning("[EpicNet] Current owner is null - taking ownership");
                    TransferOwnership(EpicNetwork.LocalPlayer);
                    return;
                }

                Debug.Log($"[EpicNet] Requesting ownership of ViewID {ViewID} from {Owner.NickName}");
                EpicNetwork.SendOwnershipRequest(ViewID, Owner, OnOwnershipRequestResponse);
            }
        }

        private void OnOwnershipRequestResponse(bool approved)
        {
            if (approved)
            {
                Debug.Log($"[EpicNet] Ownership request approved for ViewID {ViewID}");
                TransferOwnership(EpicNetwork.LocalPlayer);
            }
            else
            {
                Debug.Log($"[EpicNet] Ownership request denied for ViewID {ViewID}");
            }
        }

        /// <summary>
        /// Handles incoming ownership requests. Called on the owner's client.
        /// </summary>
        internal void HandleOwnershipRequest(EpicPlayer requester, Action<bool> responseCallback)
        {
            if (requester == null)
            {
                responseCallback?.Invoke(false);
                return;
            }

            if (!IsMine)
            {
                Debug.LogWarning("[EpicNet] Received ownership request but not the owner");
                responseCallback?.Invoke(false);
                return;
            }

            bool approved = OnOwnershipRequestReceived(requester);

            if (approved)
            {
                TransferOwnership(requester);
            }

            responseCallback?.Invoke(approved);
        }

        /// <summary>
        /// Override this method to implement custom ownership request approval logic.
        /// </summary>
        /// <param name="requester">The player requesting ownership.</param>
        /// <returns>True to approve the request, false to deny.</returns>
        protected virtual bool OnOwnershipRequestReceived(EpicPlayer requester)
        {
            // Default behavior: auto-approve all requests
            Debug.Log($"[EpicNet] Auto-approving ownership request from {requester.NickName}");
            return true;
        }

        #endregion
    }
}