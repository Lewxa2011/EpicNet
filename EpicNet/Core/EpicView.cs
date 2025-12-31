using UnityEngine;
using System;

namespace EpicNet
{
    /// <summary>
    /// Network view component - represents a networked object
    /// </summary>
    public class EpicView : MonoBehaviour
    {
        public int ViewID { get; set; }
        public EpicPlayer Owner { get; set; }
        public bool IsMine => Owner?.IsLocal ?? false;

        [SerializeField] private OwnershipOption ownershipTransfer = OwnershipOption.Takeover;

        private Action<int, EpicPlayer> _pendingOwnershipRequest;

        /// <summary>
        /// Transfer ownership of this view to another player
        /// </summary>
        public void TransferOwnership(EpicPlayer newOwner)
        {
            if (ownershipTransfer == OwnershipOption.Fixed)
            {
                Debug.LogWarning("EpicNet: Cannot transfer ownership - set to Fixed");
                return;
            }

            var oldOwner = Owner;
            Owner = newOwner;

            Debug.Log($"EpicNet: Ownership transferred from {oldOwner?.NickName} to {newOwner.NickName}");

            // Notify network about ownership change
            EpicNetwork.SendOwnershipTransfer(ViewID, newOwner);
        }

        /// <summary>
        /// Request ownership of this view
        /// </summary>
        public void RequestOwnership()
        {
            if (IsMine) return;

            if (ownershipTransfer == OwnershipOption.Fixed)
            {
                Debug.LogWarning("EpicNet: Cannot request ownership - set to Fixed");
                return;
            }

            if (ownershipTransfer == OwnershipOption.Takeover)
            {
                TransferOwnership(EpicNetwork.LocalPlayer);
            }
            else if (ownershipTransfer == OwnershipOption.Request)
            {
                Debug.Log($"EpicNet: Requesting ownership from {Owner.NickName}...");

                // Send ownership request to the current owner
                EpicNetwork.SendOwnershipRequest(ViewID, Owner, OnOwnershipRequestResponse);
            }
        }

        private void OnOwnershipRequestResponse(bool approved)
        {
            if (approved)
            {
                Debug.Log($"EpicNet: Ownership request approved for ViewID {ViewID}");
                TransferOwnership(EpicNetwork.LocalPlayer);
            }
            else
            {
                Debug.Log($"EpicNet: Ownership request denied for ViewID {ViewID}");
            }
        }

        /// <summary>
        /// Handle incoming ownership request (called on owner's client)
        /// </summary>
        internal void HandleOwnershipRequest(EpicPlayer requester, Action<bool> responseCallback)
        {
            if (!IsMine)
            {
                Debug.LogWarning("EpicNet: Received ownership request but not the owner");
                responseCallback?.Invoke(false);
                return;
            }

            // Auto-approve for now - you can override this behavior
            bool approved = OnOwnershipRequestReceived(requester);

            if (approved)
            {
                TransferOwnership(requester);
            }

            responseCallback?.Invoke(approved);
        }

        /// <summary>
        /// Override this to implement custom ownership request logic
        /// </summary>
        protected virtual bool OnOwnershipRequestReceived(EpicPlayer requester)
        {
            // Default: auto-approve all requests
            Debug.Log($"EpicNet: Auto-approving ownership request from {requester.NickName}");
            return true;
        }

        private void OnDestroy()
        {
            EpicNetwork.UnregisterNetworkObject(ViewID);
        }
    }
}