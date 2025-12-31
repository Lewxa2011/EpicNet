using UnityEngine;
using System.Collections.Generic;

namespace EpicNet
{
    /// <summary>
    /// MonoBehaviour callbacks for network events - equivalent to IConnectionCallbacks, IMatchmakingCallbacks, etc.
    /// </summary>
    public abstract class EpicMonoBehaviourCallbacks : MonoBehaviour
    {
        protected virtual void OnEnable()
        {
            EpicNetwork.OnConnectedToMaster += OnConnectedToMaster;
            EpicNetwork.OnJoinedRoom += OnJoinedRoom;
            EpicNetwork.OnLeftRoom += OnLeftRoom;
            EpicNetwork.OnPlayerEnteredRoom += OnPlayerEnteredRoom;
            EpicNetwork.OnPlayerLeftRoom += OnPlayerLeftRoom;
            EpicNetwork.OnMasterClientSwitched += OnMasterClientSwitched;
        }

        protected virtual void OnDisable()
        {
            EpicNetwork.OnConnectedToMaster -= OnConnectedToMaster;
            EpicNetwork.OnJoinedRoom -= OnJoinedRoom;
            EpicNetwork.OnLeftRoom -= OnLeftRoom;
            EpicNetwork.OnPlayerEnteredRoom -= OnPlayerEnteredRoom;
            EpicNetwork.OnPlayerLeftRoom -= OnPlayerLeftRoom;
            EpicNetwork.OnMasterClientSwitched -= OnMasterClientSwitched;
        }

        public virtual void OnConnectedToMaster() { }
        public virtual void OnJoinedRoom() { }
        public virtual void OnLeftRoom() { }
        public virtual void OnPlayerEnteredRoom(EpicPlayer newPlayer) { }
        public virtual void OnPlayerLeftRoom(EpicPlayer otherPlayer) { }
        public virtual void OnMasterClientSwitched(EpicPlayer newMaster) { }
    }
}