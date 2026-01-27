using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Base class for MonoBehaviours that need to respond to network events.
    /// Inherit from this class and override the virtual methods to handle events.
    /// Similar to Photon's MonoBehaviourPunCallbacks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Callbacks are automatically subscribed when the component is enabled and
    /// unsubscribed when disabled, ensuring no memory leaks.
    /// </para>
    /// <para>
    /// For non-MonoBehaviour classes, subscribe directly to events on <see cref="EpicNetwork"/>
    /// (e.g., <see cref="EpicNetwork.OnJoinedRoom"/>).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public class GameManager : EpicMonoBehaviourCallbacks
    /// {
    ///     public override void OnJoinedRoom()
    ///     {
    ///         Debug.Log("Joined room: " + EpicNetwork.CurrentRoom.Name);
    ///         SpawnPlayer();
    ///     }
    ///
    ///     public override void OnPlayerEnteredRoom(EpicPlayer newPlayer)
    ///     {
    ///         Debug.Log("Player joined: " + newPlayer.NickName);
    ///     }
    /// }
    /// </code>
    /// </example>
    public abstract class EpicMonoBehaviourCallbacks : MonoBehaviour
    {
        /// <summary>
        /// Subscribes to all network events when the component is enabled.
        /// Override and call base.OnEnable() if you need custom enable logic.
        /// </summary>
        protected virtual void OnEnable()
        {
            EpicNetwork.OnConnectedToMaster += OnConnectedToMaster;
            EpicNetwork.OnJoinedRoom += OnJoinedRoom;
            EpicNetwork.OnLeftRoom += OnLeftRoom;
            EpicNetwork.OnPlayerEnteredRoom += OnPlayerEnteredRoom;
            EpicNetwork.OnPlayerLeftRoom += OnPlayerLeftRoom;
            EpicNetwork.OnMasterClientSwitched += OnMasterClientSwitched;
            EpicNetwork.OnRoomListUpdate += OnRoomListUpdate;
            EpicNetwork.OnReconnecting += OnReconnecting;
            EpicNetwork.OnReconnected += OnReconnected;
            EpicNetwork.OnReconnectFailed += OnReconnectFailed;
            EpicNetwork.OnKicked += OnKicked;
            EpicNetwork.OnJoinRoomFailed += OnJoinRoomFailed;
        }

        /// <summary>
        /// Unsubscribes from all network events when the component is disabled.
        /// Override and call base.OnDisable() if you need custom disable logic.
        /// </summary>
        protected virtual void OnDisable()
        {
            EpicNetwork.OnConnectedToMaster -= OnConnectedToMaster;
            EpicNetwork.OnJoinedRoom -= OnJoinedRoom;
            EpicNetwork.OnLeftRoom -= OnLeftRoom;
            EpicNetwork.OnPlayerEnteredRoom -= OnPlayerEnteredRoom;
            EpicNetwork.OnPlayerLeftRoom -= OnPlayerLeftRoom;
            EpicNetwork.OnMasterClientSwitched -= OnMasterClientSwitched;
            EpicNetwork.OnRoomListUpdate -= OnRoomListUpdate;
            EpicNetwork.OnReconnecting -= OnReconnecting;
            EpicNetwork.OnReconnected -= OnReconnected;
            EpicNetwork.OnReconnectFailed -= OnReconnectFailed;
            EpicNetwork.OnKicked -= OnKicked;
            EpicNetwork.OnJoinRoomFailed -= OnJoinRoomFailed;
        }

        /// <summary>
        /// Called after successfully connecting to EOS services.
        /// At this point you can create or join rooms.
        /// </summary>
        public virtual void OnConnectedToMaster() { }

        /// <summary>
        /// Called after successfully joining or creating a room.
        /// Access the room via <see cref="EpicNetwork.CurrentRoom"/>.
        /// </summary>
        public virtual void OnJoinedRoom() { }

        /// <summary>
        /// Called after leaving the current room.
        /// </summary>
        public virtual void OnLeftRoom() { }

        /// <summary>
        /// Called when another player joins the room.
        /// </summary>
        /// <param name="newPlayer">The player who joined.</param>
        public virtual void OnPlayerEnteredRoom(EpicPlayer newPlayer) { }

        /// <summary>
        /// Called when another player leaves the room.
        /// </summary>
        /// <param name="otherPlayer">The player who left.</param>
        public virtual void OnPlayerLeftRoom(EpicPlayer otherPlayer) { }

        /// <summary>
        /// Called when the master client changes (due to host migration or promotion).
        /// Check <see cref="EpicNetwork.IsMasterClient"/> to see if you are now the master.
        /// </summary>
        /// <param name="newMaster">The new master client.</param>
        public virtual void OnMasterClientSwitched(EpicPlayer newMaster) { }

        /// <summary>
        /// Called when the room list is updated after calling <see cref="EpicNetwork.GetRoomList"/>.
        /// </summary>
        /// <param name="roomList">The current list of available rooms.</param>
        public virtual void OnRoomListUpdate(System.Collections.Generic.List<EpicRoomInfo> roomList) { }

        /// <summary>
        /// Called when the client is attempting to reconnect to a room.
        /// </summary>
        public virtual void OnReconnecting() { }

        /// <summary>
        /// Called when reconnection to a room succeeds.
        /// </summary>
        public virtual void OnReconnected() { }

        /// <summary>
        /// Called when reconnection attempts have failed.
        /// </summary>
        public virtual void OnReconnectFailed() { }

        /// <summary>
        /// Called when the local player is kicked from the room by the master client.
        /// </summary>
        /// <param name="reason">The reason provided for the kick, if any.</param>
        public virtual void OnKicked(string reason) { }

        /// <summary>
        /// Called when joining a room fails.
        /// </summary>
        /// <param name="reason">The reason for the failure.</param>
        public virtual void OnJoinRoomFailed(string reason) { }
    }
}