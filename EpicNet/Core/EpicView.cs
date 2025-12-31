using Epic.OnlineServices;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Network view component - equivalent to PhotonView
    /// </summary>
    public class EpicView : MonoBehaviour
    {
        public int ViewID { get; set; }
        public EpicPlayer Owner { get; set; }
        public bool IsMine => Owner?.IsLocal ?? false;

        [SerializeField] private ViewSynchronization synchronization = ViewSynchronization.UnreliableOnChange;
        [SerializeField] private OwnershipOption ownershipTransfer = OwnershipOption.Takeover;

        private List<Component> _observedComponents = new List<Component>();
        private Dictionary<string, MethodInfo> _rpcMethods = new Dictionary<string, MethodInfo>();

        private void Awake()
        {
            // Cache all methods with [EpicRPC] attribute
            CacheRPCMethods();
        }

        /// <summary>
        /// Call an RPC method on this view
        /// </summary>
        public void RPC(string methodName, RpcTarget target, params object[] parameters)
        {
            if (!EpicNetwork.InRoom)
            {
                Debug.LogWarning("EpicNet: Cannot send RPC while not in a room");
                return;
            }

            // Send RPC through network
            EpicNetwork.RPC(methodName, target, parameters);

            // Execute locally if needed
            if (target == RpcTarget.All || target == RpcTarget.AllBuffered)
            {
                ExecuteRPC(methodName, parameters);
            }
        }

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

            Owner = newOwner;
            Debug.Log($"EpicNet: Ownership transferred to {newOwner.NickName}");
        }

        /// <summary>
        /// Request ownership of this view
        /// </summary>
        public void RequestOwnership()
        {
            if (IsMine) return;

            if (ownershipTransfer == OwnershipOption.Takeover)
            {
                TransferOwnership(EpicNetwork.LocalPlayer);
            }
            else
            {
                Debug.Log("EpicNet: Requesting ownership...");
                // Send ownership request to current owner
            }
        }

        private void CacheRPCMethods()
        {
            var components = GetComponents<MonoBehaviour>();
            foreach (var component in components)
            {
                var methods = component.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var method in methods)
                {
                    if (method.GetCustomAttribute<EpicRPC>() != null)
                    {
                        _rpcMethods[method.Name] = method;
                    }
                }
            }
        }

        private void ExecuteRPC(string methodName, object[] parameters)
        {
            if (_rpcMethods.TryGetValue(methodName, out MethodInfo method))
            {
                try
                {
                    method.Invoke(this, parameters);
                }
                catch (Exception e)
                {
                    Debug.LogError($"EpicNet: Error executing RPC {methodName}: {e}");
                }
            }
            else
            {
                Debug.LogWarning($"EpicNet: RPC method '{methodName}' not found on {gameObject.name}");
            }
        }

        public void ObserveComponent(Component component)
        {
            if (!_observedComponents.Contains(component))
            {
                _observedComponents.Add(component);
            }
        }

        private void OnDestroy()
        {
            // Cleanup network registration
        }
    }
}