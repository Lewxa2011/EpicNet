using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Attribute = Epic.OnlineServices.Lobby.Attribute;

namespace EpicNet
{
    /// <summary>
    /// Central networking manager for EpicNet. Handles connections, rooms, players,
    /// RPCs, object instantiation, and host migration using Epic Online Services.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a static class - all networking operations go through static methods.
    /// Call <see cref="Update"/> every frame from a MonoBehaviour to process network messages.
    /// </para>
    /// <para>
    /// Typical usage flow:
    /// 1. <see cref="LoginWithDeviceId"/> - Authenticate with EOS
    /// 2. <see cref="ConnectUsingSettings"/> - Connect to networking services
    /// 3. <see cref="CreateRoom"/> or <see cref="JoinRoom"/> - Enter a room
    /// 4. <see cref="Instantiate"/> - Spawn networked objects
    /// 5. Use <see cref="RPC"/> for remote procedure calls
    /// </para>
    /// </remarks>
    public static class EpicNetwork
    {
        #region Public Properties

        /// <summary>True when connected to EOS networking services.</summary>
        public static bool IsConnected => _isConnected;

        /// <summary>True when logged in with an EOS user ID.</summary>
        public static bool IsLoggedIn => _localUserId != null;

        /// <summary>True when currently in a room/lobby.</summary>
        public static bool InRoom => CurrentRoom != null;

        /// <summary>True if this client is the master client (room host).</summary>
        public static bool IsMasterClient => _isMasterClient;

        /// <summary>The local player instance.</summary>
        public static EpicPlayer LocalPlayer => _localPlayer;

        /// <summary>The current master client. May change during host migration.</summary>
        public static EpicPlayer MasterClient => _masterClient;

        /// <summary>The current room, or null if not in a room.</summary>
        public static EpicRoom CurrentRoom => _currentRoom;

        /// <summary>
        /// List of all players in the current room.
        /// Note: For thread-safe access, use GetPlayerListSnapshot().
        /// </summary>
        public static List<EpicPlayer> PlayerList => _playerList;

        /// <summary>Display name for the local player.</summary>
        public static string NickName { get; set; }

        /// <summary>How many times per second to send observable data. Default: 20Hz.</summary>
        public static float SendRate { get; set; } = 20f;

        /// <summary>Current round-trip ping time in milliseconds.</summary>
        public static int Ping { get; private set; }

        #endregion

        #region Private Fields - State

        private static bool _isConnected;
        private static bool _isMasterClient;
        private static EpicPlayer _localPlayer;
        private static EpicPlayer _masterClient;
        private static EpicRoom _currentRoom;
        private static List<EpicPlayer> _playerList = new List<EpicPlayer>();

        #endregion

        #region Private Fields - Thread Safety

        private static readonly object _playerListLock = new object();
        private static readonly object _networkObjectsLock = new object();
        private static readonly object _bufferedRPCsLock = new object();
        private static readonly object _delayedActionsLock = new object();

        #endregion

        #region Private Fields - EOS Interfaces

        private static LobbyInterface _lobbyInterface;
        private static P2PInterface _p2pInterface;
        private static ConnectInterface _connectInterface;
        private static ProductUserId _localUserId;
        private static string _currentLobbyId;

        #endregion

        #region Private Fields - Network Objects

        private static readonly System.Random _rng = new System.Random();
        private static readonly Dictionary<int, EpicView> _networkObjects = new Dictionary<int, EpicView>();
        private static int _viewIdCounter = 1000;
        private static readonly Dictionary<int, Action<bool>> _ownershipRequestCallbacks = new Dictionary<int, Action<bool>>();
        private static int _ownershipRequestIdCounter = 0;

        #endregion

        #region Private Fields - RPC System

        private static readonly List<BufferedRPC> _bufferedRPCs = new List<BufferedRPC>();
        private static int _rpcIdCounter = 0;
        private static readonly Dictionary<string, MethodInfo> _rpcMethodCache = new Dictionary<string, MethodInfo>();

        #endregion

        #region Private Fields - Synchronization

        private static float _lastSyncTime;
        private static float _syncInterval => SendRate > 0 ? 1f / SendRate : 0.05f;

        #endregion

        #region Private Fields - P2P & Connection

        private static ulong _p2pNotificationId;
        private static readonly Dictionary<ProductUserId, float> _playerPingTimes = new Dictionary<ProductUserId, float>();
        private static readonly List<EpicRoomInfo> _cachedRoomList = new List<EpicRoomInfo>();

        #endregion

        #region Private Fields - Late Joiner & Reconnection

        private static readonly Dictionary<ProductUserId, bool> _pendingInitialState = new Dictionary<ProductUserId, bool>();
        private static float _lastInitialStateSendTime = 0f;
        private static string _lastRoomName;
        private static bool _wasInRoom;
        private static bool _isReconnecting;
        private static float _reconnectAttemptTime;
        private static int _reconnectAttempts;

        #endregion

        #region Private Fields - Kick/Ban & Security

        private static readonly HashSet<string> _bannedPlayerIds = new HashSet<string>();
        private static string _currentRoomPassword;
        private static string _pendingJoinPassword;

        #endregion

        #region Private Fields - Delayed Actions

        private static readonly List<DelayedAction> _delayedActions = new List<DelayedAction>();

        private struct DelayedAction
        {
            public float TriggerTime;
            public Action Callback;
        }

        #endregion

        #region Private Fields - Network Statistics

        private static long _bytesSent;
        private static long _bytesReceived;
        private static int _packetsSent;
        private static int _packetsReceived;
        private static float _statsResetTime;
        private static readonly Queue<float> _recentPings = new Queue<float>();
        private static int _packetsLost;
        private static int _expectedPackets;

        #endregion

        #region Public Properties - Reconnection

        /// <summary>Whether to automatically attempt reconnection if disconnected from a room.</summary>
        public static bool AutoReconnect { get; set; } = true;

        /// <summary>Delay between reconnection attempts in seconds.</summary>
        public static float ReconnectDelay { get; set; } = 2f;

        /// <summary>Maximum number of reconnection attempts before giving up.</summary>
        public static int MaxReconnectAttempts { get; set; } = 5;

        /// <summary>True if currently attempting to reconnect.</summary>
        public static bool IsReconnecting => _isReconnecting;

        #endregion

        #region Constants

        private const double BufferedRpcLifetime = 300.0; // 5 minutes
        private const int MaxPingHistory = 20;
        private const float InitialStateSendDelay = 0.5f;
        private const float BufferedRpcCleanupInterval = 1f;
        private const float OwnershipRequestTimeout = 5f;

        #endregion

        #region Public Static Methods - Thread-Safe Access

        /// <summary>
        /// Gets a thread-safe copy of the player list.
        /// Use this when iterating over players from callbacks or other threads.
        /// </summary>
        public static List<EpicPlayer> GetPlayerListSnapshot()
        {
            lock (_playerListLock)
            {
                return new List<EpicPlayer>(_playerList);
            }
        }

        /// <summary>
        /// Gets a network object by its ViewID.
        /// </summary>
        /// <param name="viewId">The ViewID to look up.</param>
        /// <returns>The EpicView if found, null otherwise.</returns>
        public static EpicView GetNetworkObject(int viewId)
        {
            lock (_networkObjectsLock)
            {
                _networkObjects.TryGetValue(viewId, out EpicView view);
                return view;
            }
        }

        /// <summary>
        /// Gets the count of active network objects.
        /// </summary>
        public static int NetworkObjectCount
        {
            get
            {
                lock (_networkObjectsLock)
                {
                    return _networkObjects.Count;
                }
            }
        }

        #endregion

        // Events
        public static event Action OnConnectedToMaster;
        public static event Action OnJoinedRoom;
        public static event Action OnLeftRoom;
        public static event Action<EpicPlayer> OnPlayerEnteredRoom;
        public static event Action<EpicPlayer> OnPlayerLeftRoom;
        public static event Action<EpicPlayer> OnMasterClientSwitched;
        public static event Action OnLoginSuccess;
        public static event Action<Result> OnLoginFailed;
        public static event Action<List<EpicRoomInfo>> OnRoomListUpdate;
        public static event Action OnReconnecting;
        public static event Action OnReconnected;
        public static event Action OnReconnectFailed;
        public static event Action<string> OnKicked; // string = reason
        public static event Action<string> OnJoinRoomFailed; // string = reason

        /// <summary>
        /// Network statistics
        /// </summary>
        public struct NetworkStats
        {
            public long BytesSent;
            public long BytesReceived;
            public int PacketsSent;
            public int PacketsReceived;
            public float BytesPerSecondSent;
            public float BytesPerSecondReceived;
            public float PacketLossPercent;
            public int AveragePing;
            public float Duration;
        }

        /// <summary>
        /// Get current network statistics
        /// </summary>
        public static NetworkStats GetNetworkStats()
        {
            float duration = Time.time - _statsResetTime;
            float avgPing = 0;
            if (_recentPings.Count > 0)
            {
                float sum = 0;
                foreach (var p in _recentPings) sum += p;
                avgPing = sum / _recentPings.Count;
            }

            return new NetworkStats
            {
                BytesSent = _bytesSent,
                BytesReceived = _bytesReceived,
                PacketsSent = _packetsSent,
                PacketsReceived = _packetsReceived,
                BytesPerSecondSent = duration > 0 ? _bytesSent / duration : 0,
                BytesPerSecondReceived = duration > 0 ? _bytesReceived / duration : 0,
                PacketLossPercent = _expectedPackets > 0 ? (_packetsLost / (float)_expectedPackets) * 100f : 0,
                AveragePing = (int)avgPing,
                Duration = duration
            };
        }

        /// <summary>
        /// Reset network statistics
        /// </summary>
        public static void ResetNetworkStats()
        {
            _bytesSent = 0;
            _bytesReceived = 0;
            _packetsSent = 0;
            _packetsReceived = 0;
            _packetsLost = 0;
            _expectedPackets = 0;
            _recentPings.Clear();
            _statsResetTime = Time.time;
        }

        private struct BufferedRPC
        {
            public int ViewID;
            public string MethodName;
            public object[] Parameters;
            public EpicPlayer Sender;
            public double Timestamp;
        }

        private enum PacketType : byte
        {
            OwnershipTransfer = 1,
            OwnershipRequest = 2,
            OwnershipResponse = 3,
            RPC = 4,
            ObservableData = 5,
            InstantiateObject = 6,
            DestroyObject = 7,
            Ping = 8,
            Pong = 9,
            InitialState = 10,
            Kick = 11
        }

        /// <summary>
        /// Login with Device ID
        /// </summary>
        public static void LoginWithDeviceId(string displayName, Action<bool, string> callback = null)
        {
            var eosManager = EOSManager.Instance;
            if (eosManager == null)
            {
                Debug.LogError("EpicNet: EOSManager not found!");
                callback?.Invoke(false, "EOSManager not found");
                return;
            }

            var platform = eosManager.GetEOSPlatformInterface();
            if (platform == null)
            {
                Debug.LogError("EpicNet: Platform interface not found!");
                callback?.Invoke(false, "Platform interface not found");
                return;
            }

            _connectInterface = platform.GetConnectInterface();

            Debug.Log($"EpicNet: Logging in with Device ID as '{displayName}'...");

#if UNITY_ANDROID || UNITY_IOS
            // On mobile platforms, we need to create the device ID first
            CreateDeviceIdThenLogin(displayName, callback);
#else
            // On desktop platforms, we can try to login directly
            PerformDeviceIdLogin(displayName, callback);
#endif
        }

        private static void CreateDeviceIdThenLogin(string displayName, Action<bool, string> callback)
        {
            var eosManager = EOSManager.Instance;
            var platform = eosManager.GetEOSPlatformInterface();
            var connectInterface = platform.GetConnectInterface();

            var createDeviceIdOptions = new Epic.OnlineServices.Connect.CreateDeviceIdOptions
            {
                DeviceModel = SystemInfo.deviceModel
            };

            connectInterface.CreateDeviceId(ref createDeviceIdOptions, null, (ref Epic.OnlineServices.Connect.CreateDeviceIdCallbackInfo data) =>
            {
                if (data.ResultCode == Result.Success || data.ResultCode == Result.DuplicateNotAllowed)
                {
                    // Device ID created or already exists, proceed with login
                    Debug.Log($"EpicNet: Device ID ready (Result: {data.ResultCode}), proceeding with login...");
                    PerformDeviceIdLogin(displayName, callback);
                }
                else
                {
                    Debug.LogError($"EpicNet: Failed to create device ID: {data.ResultCode}");
                    OnLoginFailed?.Invoke(data.ResultCode);
                    callback?.Invoke(false, $"Failed to create device ID: {data.ResultCode}");
                }
            });
        }

        private static void PerformDeviceIdLogin(string displayName, Action<bool, string> callback)
        {
            var connectLoginOptions = new LoginOptions
            {
                Credentials = new Credentials
                {
                    Type = ExternalCredentialType.DeviceidAccessToken,
                    Token = null
                },
                UserLoginInfo = new UserLoginInfo
                {
                    DisplayName = displayName
                }
            };

            _connectInterface.Login(ref connectLoginOptions, null, (ref LoginCallbackInfo data) =>
            {
                if (data.ResultCode == Result.Success)
                {
                    _localUserId = data.LocalUserId;
                    Debug.Log($"EpicNet: Login successful! User ID: {_localUserId}");
                    OnLoginSuccess?.Invoke();
                    callback?.Invoke(true, "Login successful");
                }
                else if (data.ResultCode == Result.InvalidUser)
                {
                    Debug.Log("EpicNet: Creating new device user...");
                    CreateDeviceUser(data.ContinuanceToken, displayName, callback);
                }
                else
                {
                    Debug.LogError($"EpicNet: Login failed: {data.ResultCode}");
                    OnLoginFailed?.Invoke(data.ResultCode);
                    callback?.Invoke(false, $"Login failed: {data.ResultCode}");
                }
            });
        }

        private static void CreateDeviceUser(ContinuanceToken continuanceToken, string displayName, Action<bool, string> callback)
        {
            if (continuanceToken == null)
            {
                Debug.LogError("EpicNet: No continuance token for user creation");
                callback?.Invoke(false, "No continuance token");
                return;
            }

            var createOptions = new CreateUserOptions
            {
                ContinuanceToken = continuanceToken
            };

            _connectInterface.CreateUser(ref createOptions, null, (ref CreateUserCallbackInfo data) =>
            {
                if (data.ResultCode == Result.Success)
                {
                    _localUserId = data.LocalUserId;
                    Debug.Log($"EpicNet: Device user created! User ID: {_localUserId}");
                    OnLoginSuccess?.Invoke();
                    callback?.Invoke(true, "User created successfully");
                }
                else
                {
                    Debug.LogError($"EpicNet: Failed to create user: {data.ResultCode}");
                    OnLoginFailed?.Invoke(data.ResultCode);
                    callback?.Invoke(false, $"User creation failed: {data.ResultCode}");
                }
            });
        }

        /// <summary>
        /// Logout from EOS Connect
        /// </summary>
        public static void Logout()
        {
            if (InRoom)
            {
                LeaveRoom();
            }

            // Unsubscribe from P2P
            if (_p2pInterface != null && _p2pNotificationId != 0)
            {
                _p2pInterface.RemoveNotifyPeerConnectionRequest(_p2pNotificationId);
                _p2pNotificationId = 0;
            }

            _localUserId = null;
            _isConnected = false;
            _isMasterClient = false;
            _localPlayer = null;
            _masterClient = null;
            _currentRoom = null;
            _currentLobbyId = null;
            lock (_playerListLock)
            {
                _playerList.Clear();
            }
            lock (_networkObjectsLock)
            {
                _networkObjects.Clear();
            }
            _bufferedRPCs.Clear();
            _ownershipRequestCallbacks.Clear();
            _pendingInitialState.Clear();
            _delayedActions.Clear();
            _rpcMethodCache.Clear();

            // Reset reconnection state
            _wasInRoom = false;
            _isReconnecting = false;
            _reconnectAttempts = 0;

            // Reset actor counter for fresh session
            EpicPlayer.ResetActorCounter();

            Debug.Log("EpicNet: Logged out");
        }

        /// <summary>
        /// Connect to EOS services
        /// </summary>
        public static void ConnectUsingSettings()
        {
            if (_isConnected) return;

            if (_localUserId == null)
            {
                Debug.LogError("EpicNet: Not logged in! Call LoginWithDeviceId first!");
                return;
            }

            var eosManager = EOSManager.Instance;
            if (eosManager == null)
            {
                Debug.LogError("EpicNet: EOSManager not found!");
                return;
            }

            _lobbyInterface = eosManager.GetEOSPlatformInterface().GetLobbyInterface();
            _p2pInterface = eosManager.GetEOSPlatformInterface().GetP2PInterface();

            // Subscribe to lobby member updates for host migration
            SubscribeToLobbyUpdates();

            // Subscribe to P2P connection requests
            SubscribeToP2PEvents();

            _isConnected = true;
            _localPlayer = new EpicPlayer(_localUserId, NickName ?? "Player");

            Debug.Log("EpicNet: Connected to EOS");
            OnConnectedToMaster?.Invoke();
        }

        private static void SubscribeToLobbyUpdates()
        {
            var options = new AddNotifyLobbyMemberUpdateReceivedOptions();
            _lobbyInterface.AddNotifyLobbyMemberUpdateReceived(ref options, null, OnLobbyMemberUpdate);

            var memberStatusOptions = new AddNotifyLobbyMemberStatusReceivedOptions();
            _lobbyInterface.AddNotifyLobbyMemberStatusReceived(ref memberStatusOptions, null, OnLobbyMemberStatusReceived);
        }

        private static void SubscribeToP2PEvents()
        {
            var options = new AddNotifyPeerConnectionRequestOptions
            {
                LocalUserId = _localUserId,
                SocketId = null
            };

            _p2pNotificationId = _p2pInterface.AddNotifyPeerConnectionRequest(ref options, null, OnP2PConnectionRequest);
            Debug.Log("EpicNet: Subscribed to P2P connection requests");
        }

        private static void OnP2PConnectionRequest(ref OnIncomingConnectionRequestInfo data)
        {
            Debug.Log($"EpicNet: P2P connection request from {data.RemoteUserId}");

            // Only accept connections from players in our lobby or if we're not in a room yet
            bool isKnownPlayer;
            ProductUserId remoteUserId = data.RemoteUserId;
            lock (_playerListLock)
            {
                isKnownPlayer =
                    _playerList.Any(p => ProductUserId.Equals(p.UserId, remoteUserId)) ||
                    ProductUserId.Equals(_localUserId, remoteUserId) ||
                    !InRoom;
            }

            if (!isKnownPlayer)
            {
                Debug.LogWarning($"EpicNet: Rejecting P2P connection from unknown user {data.RemoteUserId}");
                return;
            }

            var acceptOptions = new AcceptConnectionOptions
            {
                LocalUserId = _localUserId,
                RemoteUserId = data.RemoteUserId,
                SocketId = data.SocketId
            };

            var result = _p2pInterface.AcceptConnection(ref acceptOptions);
            if (result == Result.Success)
            {
                Debug.Log($"EpicNet: Accepted P2P connection from {data.RemoteUserId}");
            }
            else
            {
                Debug.LogWarning($"EpicNet: Failed to accept P2P connection: {result}");
            }
        }

        private static void OnLobbyMemberUpdate(ref LobbyMemberUpdateReceivedCallbackInfo data)
        {
            Debug.Log($"EpicNet: Lobby member update received for {data.TargetUserId}");
        }

        private static void OnLobbyMemberStatusReceived(ref LobbyMemberStatusReceivedCallbackInfo data)
        {
            Debug.Log($"EpicNet: Member status changed: {data.TargetUserId} - {data.CurrentStatus}");

            if (data.CurrentStatus == LobbyMemberStatus.Left)
            {
                HandlePlayerLeft(data.TargetUserId);
            }
            else if (data.CurrentStatus == LobbyMemberStatus.Joined)
            {
                HandlePlayerJoined(data.TargetUserId);
            }
        }

        private static void TrySendPendingInitialStates()
        {
            if (!IsMasterClient || _pendingInitialState.Count == 0) return;

            // Throttle sends to once per second
            if (Time.time - _lastInitialStateSendTime < 1f) return;

            _lastInitialStateSendTime = Time.time;

            // Try to send to all pending players
            var playersToRemove = new List<ProductUserId>();

            foreach (var kvp in _pendingInitialState)
            {
                var userId = kvp.Key;
                EpicPlayer player;
                lock (_playerListLock)
                {
                    player = _playerList.Find(p => p.UserId == userId);
                }

                if (player != null)
                {
                    Debug.Log($"EpicNet: Sending initial state to {player.NickName}");
                    SendInitialStateToPlayer(player);
                    playersToRemove.Add(userId);
                }
            }

            // Remove players we've sent to
            foreach (var userId in playersToRemove)
            {
                _pendingInitialState.Remove(userId);
            }
        }

        private static void HandlePlayerJoined(ProductUserId userId)
        {
            if (userId == _localUserId) return;

            lock (_playerListLock)
            {
                // Check if player already exists in list
                if (_playerList.Any(p => p.UserId == userId)) return;

                var player = new EpicPlayer(userId, $"Player_{userId}");

                // Check if player is banned (master client only)
                if (IsMasterClient && IsPlayerBanned(userId.ToString()))
                {
                    Debug.Log($"EpicNet: Banned player tried to join, kicking: {userId}");
                    // Need to add them temporarily to kick them
                    _playerList.Add(player);
                    _currentRoom.Players.Add(player);
                    KickPlayer(player, "You are banned from this room");
                    return;
                }

                _playerList.Add(player);
                _currentRoom.Players.Add(player);

                Debug.Log($"EpicNet: Player joined: {player.NickName}");

                if (IsMasterClient)
                {
                    _pendingInitialState[userId] = true;
                }
            }

            // Fire event outside lock to prevent deadlocks
            var joinedPlayer = _playerList.FirstOrDefault(p => p.UserId == userId);
            if (joinedPlayer != null)
            {
                OnPlayerEnteredRoom?.Invoke(joinedPlayer);

                if (IsMasterClient)
                {
                    // Immediately try to send (or add a small delay)
                    SendInitialStateToPlayer(joinedPlayer);
                }
            }
        }

        private static void HandlePlayerLeft(ProductUserId userId)
        {
            EpicPlayer player = null;
            bool wasMasterClient = false;

            lock (_playerListLock)
            {
                player = _playerList.FirstOrDefault(p => p.UserId == userId);
                if (player == null) return;

                wasMasterClient = player.IsMasterClient;
                _playerList.Remove(player);
                _currentRoom?.Players.Remove(player);
            }

            Debug.Log($"EpicNet: Player left: {player.NickName}");

            // Clean up network objects owned by this player
            CleanupPlayerObjects(player);

            // Check if the master client left
            if (wasMasterClient)
            {
                PerformHostMigration();
            }

            OnPlayerLeftRoom?.Invoke(player);
        }

        private static void CleanupPlayerObjects(EpicPlayer player)
        {
            var objectsToDestroy = _networkObjects.Values
                .Where(v => v != null && v.Owner?.UserId == player.UserId)
                .ToList();

            foreach (var view in objectsToDestroy)
            {
                if (view != null)
                {
                    // Unregister the object
                    UnregisterNetworkObject(view.ViewID);

                    if (view.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(view.gameObject);
                    }
                }
            }
        }

        private static void PerformHostMigration()
        {
            Debug.Log("EpicNet: Performing host migration...");

            EpicPlayer newMaster = null;

            lock (_playerListLock)
            {
                // Handle empty player list case - promote local player if available
                if (_playerList == null || _playerList.Count == 0)
                {
                    if (_localPlayer != null)
                    {
                        Debug.Log("EpicNet: No other players remaining, promoting local player to master");
                        _localPlayer.IsMasterClient = true;
                        _masterClient = _localPlayer;
                        _isMasterClient = true;
                        // Fire event outside lock
                        newMaster = _masterClient;
                    }
                    else
                    {
                        Debug.LogWarning("EpicNet: No players remaining for host migration");
                        _masterClient = null;
                        _isMasterClient = false;
                    }
                }
                else
                {
                    // Find new master client (player with lowest actor number)
                    newMaster = _playerList.OrderBy(p => p.ActorNumber).FirstOrDefault();

                    if (newMaster != null)
                    {
                        // Clear old master status
                        foreach (var player in _playerList)
                        {
                            player.IsMasterClient = false;
                        }

                        // Assign new master
                        newMaster.IsMasterClient = true;
                        _masterClient = newMaster;
                        _isMasterClient = (newMaster.UserId == _localUserId);
                    }
                    else
                    {
                        Debug.LogWarning("EpicNet: Could not determine new master client");
                        _masterClient = null;
                        _isMasterClient = false;
                        if (_localPlayer != null)
                        {
                            _localPlayer.IsMasterClient = false;
                        }
                    }
                }
            }

            // Perform actions outside lock
            if (newMaster != null)
            {
                if (_isMasterClient)
                {
                    Debug.Log("EpicNet: This client is now the master!");
                    PromoteLobbyOwner();

                    // Take ownership of all unowned objects
                    TakeOwnershipOfOrphanedObjects();
                }
                else
                {
                    Debug.Log($"EpicNet: New master client is {newMaster.NickName}");
                }

                OnMasterClientSwitched?.Invoke(_masterClient);
            }
            else if (_localPlayer != null && _masterClient == _localPlayer)
            {
                // Empty room case - still fire the event
                OnMasterClientSwitched?.Invoke(_masterClient);
            }
        }

        private static void TakeOwnershipOfOrphanedObjects()
        {
            // Use ToList() to avoid modification during iteration
            List<EpicView> views;
            lock (_networkObjectsLock)
            {
                views = _networkObjects.Values.ToList();
            }

            List<ProductUserId> activeUserIds;
            lock (_playerListLock)
            {
                activeUserIds = _playerList.Select(p => p.UserId).ToList();
            }

            foreach (var view in views)
            {
                if (view == null || view.gameObject == null) continue;

                // Scene objects should always belong to master client
                bool isOrphaned = view.Owner == null ||
                                  !activeUserIds.Any(uid => uid == view.Owner.UserId);
                bool isSceneObjectNeedingReassignment = view.IsSceneObject && view.Owner != _localPlayer;

                if (isOrphaned || isSceneObjectNeedingReassignment)
                {
                    view.Owner = _localPlayer;
                    Debug.Log($"EpicNet: Took ownership of {(view.IsSceneObject ? "scene" : "orphaned")} object {view.ViewID}");

                    // Notify other players about the ownership transfer
                    SendOwnershipTransfer(view.ViewID, _localPlayer);
                }
            }
        }

        private static void PromoteLobbyOwner()
        {
            if (string.IsNullOrEmpty(_currentLobbyId)) return;

            var options = new PromoteMemberOptions
            {
                LobbyId = _currentLobbyId,
                LocalUserId = _localUserId,
                TargetUserId = _localUserId
            };

            _lobbyInterface.PromoteMember(ref options, null, (ref PromoteMemberCallbackInfo data) =>
            {
                if (data.ResultCode == Result.Success)
                {
                    Debug.Log("EpicNet: Successfully promoted to lobby owner");
                }
                else
                {
                    Debug.LogWarning($"EpicNet: Failed to promote to lobby owner: {data.ResultCode}");
                }
            });
        }

        public static void SetNickName(string nickName)
        {
            NickName = nickName;
            if (_localPlayer != null)
            {
                _localPlayer.NickName = nickName;
            }
        }

        private static string CreateRoomName()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            char[] result = new char[4];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = chars[_rng.Next(chars.Length)];
            }

            return new string(result);
        }

        /// <summary>
        /// Create a new room
        /// </summary>
        public static void CreateRoom(string roomName, EpicRoomOptions roomOptions = null)
        {
            if (!_isConnected)
            {
                Debug.LogError("EpicNet: Not connected to EOS!");
                return;
            }

            roomOptions = roomOptions ?? new EpicRoomOptions();

            var createOptions = new CreateLobbyOptions
            {
                LocalUserId = _localUserId,
                MaxLobbyMembers = (uint)roomOptions.MaxPlayers,
                PermissionLevel = roomOptions.IsVisible ? LobbyPermissionLevel.Publicadvertised : LobbyPermissionLevel.Inviteonly,
                BucketId = "default"
            };

            _lobbyInterface.CreateLobby(ref createOptions, null, (ref CreateLobbyCallbackInfo data) =>
            {
                OnLobbyCreated(ref data, roomName);

                // Set custom room properties after creation
                if (data.ResultCode == Result.Success)
                {
                    // Store password hash if set
                    if (roomOptions.HasPassword)
                    {
                        _currentRoomPassword = roomOptions.Password;
                        string passwordHash = HashPassword(roomOptions.Password);
                        SetRoomProperties(new Dictionary<string, object> { { "_password", passwordHash } });
                    }

                    if (roomOptions.CustomRoomProperties != null)
                    {
                        SetRoomProperties(roomOptions.CustomRoomProperties);
                    }
                }
            });
        }

        private static string HashPassword(string password)
        {
            // Simple hash for password comparison
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(password);
                byte[] hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// Constant-time string comparison to prevent timing attacks on password checks
        /// </summary>
        private static bool ConstantTimeCompare(string a, string b)
        {
            if (a == null || b == null) return a == b;

            byte[] aBytes = System.Text.Encoding.UTF8.GetBytes(a);
            byte[] bBytes = System.Text.Encoding.UTF8.GetBytes(b);

            // XOR comparison that always takes the same time regardless of match
            int result = aBytes.Length ^ bBytes.Length;
            int minLength = Math.Min(aBytes.Length, bBytes.Length);

            for (int i = 0; i < minLength; i++)
            {
                result |= aBytes[i] ^ bBytes[i];
            }

            return result == 0;
        }

        /// <summary>
        /// Set custom properties on the current room (Master Client only)
        /// </summary>
        public static void SetRoomProperties(Dictionary<string, object> properties)
        {
            if (!InRoom)
            {
                Debug.LogError("EpicNet: Not in a room!");
                return;
            }

            if (!IsMasterClient)
            {
                Debug.LogError("EpicNet: Only the master client can set room properties!");
                return;
            }

            foreach (var kvp in properties)
            {
                var attributeData = new AttributeData
                {
                    Key = kvp.Key,
                    Value = ConvertToAttributeValue(kvp.Value)
                };

                var modifyOptions = new UpdateLobbyModificationOptions
                {
                    LobbyId = _currentLobbyId,
                    LocalUserId = _localUserId
                };

                var result = _lobbyInterface.UpdateLobbyModification(ref modifyOptions, out LobbyModification lobbyModification);

                if (result != Result.Success)
                {
                    Debug.LogError($"EpicNet: Failed to create lobby modification: {result}");
                    continue;
                }

                var addAttributeOptions = new LobbyModificationAddAttributeOptions
                {
                    Attribute = attributeData,
                    Visibility = LobbyAttributeVisibility.Public
                };

                result = lobbyModification.AddAttribute(ref addAttributeOptions);

                if (result != Result.Success)
                {
                    Debug.LogError($"EpicNet: Failed to add attribute: {result}");
                    continue;
                }

                var updateOptions = new UpdateLobbyOptions
                {
                    LobbyModificationHandle = lobbyModification
                };

                _lobbyInterface.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo callbackInfo) =>
                {
                    lobbyModification.Release();
                    if (callbackInfo.ResultCode == Result.Success)
                    {
                        _currentRoom.CustomProperties[kvp.Key] = kvp.Value;
                        Debug.Log($"EpicNet: Room property '{kvp.Key}' set successfully");
                    }
                    else
                    {
                        Debug.LogError($"EpicNet: Failed to update room property '{kvp.Key}': {callbackInfo.ResultCode}");
                    }
                });
            }
        }

        private static AttributeDataValue ConvertToAttributeValue(object value)
        {
            var attributeValue = new AttributeDataValue();

            switch (value)
            {
                case bool boolVal:
                    attributeValue.AsBool = boolVal;
                    break;
                case int intVal:
                    attributeValue.AsInt64 = intVal;
                    break;
                case long longVal:
                    attributeValue.AsInt64 = longVal;
                    break;
                case double doubleVal:
                    attributeValue.AsDouble = doubleVal;
                    break;
                case float floatVal:
                    attributeValue.AsDouble = floatVal;
                    break;
                case string stringVal:
                    attributeValue.AsUtf8 = stringVal;
                    break;
                default:
                    attributeValue.AsUtf8 = value?.ToString() ?? "";
                    break;
            }

            return attributeValue;
        }

        /// <summary>
        /// Get list of available rooms
        /// </summary>
        public static void GetRoomList()
        {
            if (!_isConnected)
            {
                Debug.LogError("EpicNet: Not connected to EOS!");
                return;
            }

            var searchOptions = new CreateLobbySearchOptions
            {
                MaxResults = 50
            };

            LobbySearch outSearchHandle = default;
            _lobbyInterface.CreateLobbySearch(ref searchOptions, out outSearchHandle);

            if (outSearchHandle == null)
            {
                Debug.LogError("EpicNet: Failed to create lobby search!");
                return;
            }

            var findOptions = new LobbySearchFindOptions
            {
                LocalUserId = _localUserId
            };

            outSearchHandle.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo data) =>
            {
                if (data.ResultCode != Result.Success)
                {
                    Debug.LogError($"EpicNet: Lobby search failed: {data.ResultCode}");
                    outSearchHandle.Release();
                    return;
                }

                _cachedRoomList.Clear();
                var countOptions = new LobbySearchGetSearchResultCountOptions();
                uint resultCount = outSearchHandle.GetSearchResultCount(ref countOptions);

                for (uint i = 0; i < resultCount; i++)
                {
                    var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                    var result = outSearchHandle.CopySearchResultByIndex(ref copyOptions, out LobbyDetails lobbyDetails);

                    if (result == Result.Success)
                    {
                        var roomInfo = CreateRoomInfoFromLobbyDetails(lobbyDetails);
                        if (roomInfo != null)
                        {
                            _cachedRoomList.Add(roomInfo);
                        }
                        lobbyDetails.Release();
                    }
                }

                outSearchHandle.Release();
                OnRoomListUpdate?.Invoke(_cachedRoomList);
                Debug.Log($"EpicNet: Found {_cachedRoomList.Count} rooms");
            });
        }

        private static EpicRoomInfo CreateRoomInfoFromLobbyDetails(LobbyDetails lobbyDetails)
        {
            var infoOptions = new LobbyDetailsCopyInfoOptions();
            var result = lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? lobbyInfo);

            if (result != Result.Success || !lobbyInfo.HasValue)
                return null;

            var roomInfo = new EpicRoomInfo
            {
                Name = lobbyInfo.Value.LobbyId,
                PlayerCount = (int)(lobbyInfo.Value.MaxMembers - lobbyInfo.Value.AvailableSlots),
                MaxPlayers = (int)lobbyInfo.Value.MaxMembers,
                IsOpen = true
            };

            var opts = new LobbyDetailsCopyAttributeByKeyOptions
            {
                AttrKey = "RoomName"
            };

            if (lobbyDetails.CopyAttributeByKey(ref opts, out Attribute? attr) == Result.Success &&
                attr.HasValue &&
                attr.Value.Data.Value.Value.ValueType == AttributeType.String)
            {
                roomInfo.Name = attr.Value.Data.Value.Value.AsUtf8;
            }

            return roomInfo;
        }

        /// <summary>
        /// Join a random room
        /// </summary>
        public static void JoinRandomRoom(bool createIfNoneAvailable = true)
        {
            if (!_isConnected)
            {
                Debug.LogError("EpicNet: Not connected to EOS!");
                return;
            }

            var searchOptions = new CreateLobbySearchOptions
            {
                MaxResults = 10
            };

            LobbySearch outSearchHandle = default;
            _lobbyInterface.CreateLobbySearch(ref searchOptions, out outSearchHandle);
            if (outSearchHandle == null)
            {
                Debug.LogError("EpicNet: Failed to create lobby search!");
                return;
            }

            var findOptions = new LobbySearchFindOptions
            {
                LocalUserId = _localUserId
            };

            outSearchHandle.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo data) =>
            {
                if (data.ResultCode != Result.Success)
                {
                    Debug.LogError($"EpicNet: Lobby search failed: {data.ResultCode}");
                    outSearchHandle.Release();

                    if (createIfNoneAvailable)
                    {
                        Debug.Log("EpicNet: Creating a new room since none are available");
                        CreateRoom(CreateRoomName());
                    }

                    return;
                }

                var countOptions = new LobbySearchGetSearchResultCountOptions();
                uint resultCount = outSearchHandle.GetSearchResultCount(ref countOptions);

                if (resultCount > 0)
                {
                    var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
                    var result = outSearchHandle.CopySearchResultByIndex(ref copyOptions, out LobbyDetails lobbyDetails);

                    if (result == Result.Success)
                    {
                        JoinLobby(lobbyDetails);
                        lobbyDetails.Release();
                    }
                }
                else
                {
                    Debug.Log("EpicNet: No rooms available");

                    if (createIfNoneAvailable)
                    {
                        Debug.Log("EpicNet: Creating a new room since none are available");
                        CreateRoom(CreateRoomName());
                    }
                }

                outSearchHandle.Release();
            });
        }

        /// <summary>
        /// Join a specific room by name with password
        /// </summary>
        public static void JoinRoom(string roomName, string password, bool createFallback = true)
        {
            _pendingJoinPassword = password;
            JoinRoomInternal(roomName, createFallback);
        }

        /// <summary>
        /// Join a specific room by name
        /// </summary>
        public static void JoinRoom(string roomName, bool createFallback = true)
        {
            _pendingJoinPassword = null;
            JoinRoomInternal(roomName, createFallback);
        }

        private static void JoinRoomInternal(string roomName, bool createFallback)
        {
            if (!_isConnected)
            {
                Debug.LogError("EpicNet: Not connected to EOS!");
                return;
            }

            if (string.IsNullOrEmpty(roomName))
            {
                Debug.LogError("EpicNet: Room name is null or empty!");
                return;
            }

            var searchOptions = new CreateLobbySearchOptions
            {
                MaxResults = 10
            };

            LobbySearch lobbySearch = default;
            _lobbyInterface.CreateLobbySearch(ref searchOptions, out lobbySearch);

            if (lobbySearch == null)
            {
                Debug.LogError("EpicNet: Failed to create lobby search!");
                return;
            }

            var attributeData = new AttributeData
            {
                Key = "RoomName",
                Value = new AttributeDataValue
                {
                    AsUtf8 = roomName
                }
            };

            var setParamOptions = new LobbySearchSetParameterOptions
            {
                Parameter = attributeData,
                ComparisonOp = ComparisonOp.Equal
            };

            var setResult = lobbySearch.SetParameter(ref setParamOptions);
            if (setResult != Result.Success)
            {
                Debug.LogError($"EpicNet: Failed to set lobby search parameter: {setResult}");
                return;
            }

            var findOptions = new LobbySearchFindOptions
            {
                LocalUserId = _localUserId
            };

            lobbySearch.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo data) =>
            {
                if (data.ResultCode != Result.Success)
                {
                    Debug.LogError($"EpicNet: Lobby search failed: {data.ResultCode}");
                    lobbySearch.Release();
                    return;
                }

                var countOptions = new LobbySearchGetSearchResultCountOptions();
                uint resultCount = lobbySearch.GetSearchResultCount(ref countOptions);

                if (resultCount == 0)
                {
                    Debug.Log($"EpicNet: No room found with name '{roomName}'");
                    lobbySearch.Release();
                    if (createFallback) CreateRoom(roomName);
                    return;
                }

                var copyOptions = new LobbySearchCopySearchResultByIndexOptions
                {
                    LobbyIndex = 0
                };

                var copyResult = lobbySearch.CopySearchResultByIndex(ref copyOptions, out LobbyDetails lobbyDetails);
                if (copyResult != Result.Success)
                {
                    Debug.LogError($"EpicNet: Failed to copy lobby result: {copyResult}");
                    lobbySearch.Release();
                    return;
                }

                Debug.Log($"EpicNet: Joining room '{roomName}'");
                JoinLobby(lobbyDetails);
                lobbyDetails.Release();
                lobbySearch.Release();
            });
        }

        /// <summary>
        /// Leave the current room
        /// </summary>
        public static void LeaveRoom()
        {
            if (_currentRoom == null) return;

            var leaveOptions = new LeaveLobbyOptions
            {
                LocalUserId = _localUserId,
                LobbyId = _currentLobbyId
            };

            _lobbyInterface.LeaveLobby(ref leaveOptions, null, OnLobbyLeft);
        }

        /// <summary>
        /// Instantiate a networked object (uses pooling if enabled)
        /// </summary>
        public static GameObject Instantiate(string prefabName, Vector3 position, Quaternion rotation)
        {
            if (!InRoom)
            {
                Debug.LogError("EpicNet: Cannot instantiate while not in a room!");
                return null;
            }

            if (_localPlayer == null)
            {
                Debug.LogError("EpicNet: Cannot instantiate - local player is not initialized!");
                return null;
            }

            int viewId = GenerateViewID();
            GameObject obj;

            // Try to get from pool first
            if (EpicPool.Enabled)
            {
                obj = EpicPool.Get(prefabName, position, rotation);
            }
            else
            {
                GameObject prefab = Resources.Load<GameObject>(prefabName);
                if (prefab == null)
                {
                    Debug.LogError($"EpicNet: Prefab '{prefabName}' not found in Resources!");
                    return null;
                }
                obj = UnityEngine.Object.Instantiate(prefab, position, rotation);
            }

            if (obj == null) return null;

            var view = obj.GetComponent<EpicView>();
            if (view != null)
            {
                view.ViewID = viewId;
                view.Owner = _localPlayer;
                view.PrefabName = prefabName;
                RegisterNetworkObject(view);

                // Send instantiation message to other players
                SendInstantiateMessage(viewId, prefabName, position, rotation, _localPlayer);
            }

            return obj;
        }

        /// <summary>
        /// Destroy a networked object (returns to pool if enabled)
        /// </summary>
        public static void Destroy(GameObject obj)
        {
            var view = obj.GetComponent<EpicView>();
            if (view != null)
            {
                if (!view.IsMine)
                {
                    Debug.LogWarning("EpicNet: Cannot destroy object you don't own!");
                    return;
                }

                SendDestroyMessage(view.ViewID);
                UnregisterNetworkObject(view.ViewID);
            }

            // Return to pool if enabled, otherwise destroy
            if (EpicPool.Enabled && view != null && !string.IsNullOrEmpty(view.PrefabName))
            {
                EpicPool.Return(obj);
            }
            else
            {
                UnityEngine.Object.Destroy(obj);
            }
        }

        private static void SendInstantiateMessage(int viewId, string prefabName, Vector3 position, Quaternion rotation, EpicPlayer owner)
        {
            var data = SerializeInstantiateMessage(viewId, prefabName, position, rotation, owner);

            List<EpicPlayer> playerSnapshot;
            lock (_playerListLock)
            {
                playerSnapshot = new List<EpicPlayer>(_playerList);
            }

            foreach (var player in playerSnapshot)
            {
                if (player.UserId != _localUserId)
                {
                    SendP2PPacket(player.UserId, data);
                }
            }
        }

        private static void SendDestroyMessage(int viewId)
        {
            var data = SerializeDestroyMessage(viewId);

            List<EpicPlayer> playerSnapshot;
            lock (_playerListLock)
            {
                playerSnapshot = new List<EpicPlayer>(_playerList);
            }

            foreach (var player in playerSnapshot)
            {
                if (player.UserId != _localUserId)
                {
                    SendP2PPacket(player.UserId, data);
                }
            }
        }

        // Continuation of EpicNetwork.cs

        /// <summary>
        /// Call an RPC on a networked object
        /// </summary>
        public static void RPC(EpicView view, string methodName, RpcTarget target, params object[] parameters)
        {
            if (!InRoom)
            {
                Debug.LogError("EpicNet: Cannot send RPC while not in a room!");
                return;
            }

            int rpcId = _rpcIdCounter++;
            var rpcData = SerializeRPC(view.ViewID, methodName, parameters, rpcId, target);

            // Buffer if needed
            if (target == RpcTarget.AllBuffered || target == RpcTarget.OthersBuffered || target == RpcTarget.AllBufferedViaServer)
            {
                lock (_bufferedRPCsLock)
                {
                    _bufferedRPCs.Add(new BufferedRPC
                    {
                        ViewID = view.ViewID,
                        MethodName = methodName,
                        Parameters = parameters,
                        Sender = _localPlayer,
                        Timestamp = Time.timeAsDouble
                    });
                }
            }

            // Determine if unreliable
            bool unreliable = target == RpcTarget.AllUnreliable ||
                              target == RpcTarget.OthersUnreliable ||
                              target == RpcTarget.MasterClientUnreliable;

            // Send to targets
            switch (target)
            {
                case RpcTarget.All:
                case RpcTarget.AllBuffered:
                case RpcTarget.AllViaServer:
                case RpcTarget.AllBufferedViaServer:
                case RpcTarget.AllUnreliable:
                    // Execute locally
                    ExecuteRPC(view.ViewID, methodName, parameters, _localPlayer);
                    // Send to others
                    SendRPCToOthers(rpcData, !unreliable);
                    break;

                case RpcTarget.Others:
                case RpcTarget.OthersBuffered:
                case RpcTarget.OthersUnreliable:
                    SendRPCToOthers(rpcData, !unreliable);
                    break;

                case RpcTarget.MasterClient:
                case RpcTarget.MasterClientUnreliable:
                    if (IsMasterClient)
                    {
                        ExecuteRPC(view.ViewID, methodName, parameters, _localPlayer);
                    }
                    else
                    {
                        // Snapshot the master client to avoid TOCTOU race condition
                        var master = _masterClient;
                        if (master != null && master.UserId != null)
                        {
                            SendP2PPacket(master.UserId, rpcData, !unreliable);
                        }
                        else
                        {
                            Debug.LogWarning($"EpicNet: Cannot send RPC '{methodName}' to MasterClient - master client is null");
                        }
                    }
                    break;
            }
        }

        private static void SendRPCToOthers(byte[] rpcData, bool reliable = true)
        {
            List<EpicPlayer> playerSnapshot;
            lock (_playerListLock)
            {
                playerSnapshot = new List<EpicPlayer>(_playerList);
            }

            foreach (var player in playerSnapshot)
            {
                if (player.UserId != _localUserId)
                {
                    SendP2PPacket(player.UserId, rpcData, reliable);
                }
            }
        }

        /// <summary>
        /// Call an RPC on a networked object targeting a specific player
        /// </summary>
        public static void RPC(EpicView view, string methodName, EpicPlayer targetPlayer, params object[] parameters)
        {
            RPC(view, methodName, targetPlayer, true, parameters);
        }

        /// <summary>
        /// Call an RPC on a networked object targeting a specific player with reliability option
        /// </summary>
        public static void RPC(EpicView view, string methodName, EpicPlayer targetPlayer, bool reliable, params object[] parameters)
        {
            if (!InRoom)
            {
                Debug.LogError("EpicNet: Cannot send RPC while not in a room!");
                return;
            }

            if (targetPlayer == null)
            {
                Debug.LogError("EpicNet: Target player is null!");
                return;
            }

            int rpcId = _rpcIdCounter++;
            var rpcData = SerializeRPC(view.ViewID, methodName, parameters, rpcId, RpcTarget.Others);

            // If targeting self, execute locally
            if (targetPlayer.UserId == _localUserId)
            {
                ExecuteRPC(view.ViewID, methodName, parameters, _localPlayer);
            }
            else
            {
                SendP2PPacket(targetPlayer.UserId, rpcData, reliable);
            }
        }

        private static void ExecuteRPC(int viewId, string methodName, object[] parameters, EpicPlayer sender)
        {
            EpicView view;
            lock (_networkObjectsLock)
            {
                if (!_networkObjects.TryGetValue(viewId, out view))
                {
                    Debug.LogWarning($"[EpicNet] Cannot execute RPC '{methodName}' - View {viewId} not found");
                    return;
                }
            }

            if (view == null || view.gameObject == null)
            {
                Debug.LogWarning($"[EpicNet] Cannot execute RPC '{methodName}' - View {viewId} was destroyed");
                return;
            }

            var components = view.GetComponents<MonoBehaviour>();
            foreach (var component in components)
            {
                if (component == null) continue;

                var componentType = component.GetType();
                string cacheKey = $"{componentType.FullName}:{methodName}";

                MethodInfo method;
                if (!_rpcMethodCache.TryGetValue(cacheKey, out method))
                {
                    // Cache miss - find method via reflection
                    var methods = componentType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (var m in methods)
                    {
                        if (m.Name == methodName && m.GetCustomAttribute<EpicRPC>() != null)
                        {
                            method = m;
                            _rpcMethodCache[cacheKey] = method;
                            break;
                        }
                    }
                }

                if (method != null)
                {
                    // Security validation - check if sender is allowed to call this RPC
                    var rpcAttribute = method.GetCustomAttribute<EpicRPC>();
                    if (rpcAttribute != null && !ValidateRpcSecurity(rpcAttribute.Security, sender, view))
                    {
                        Debug.LogWarning($"[EpicNet] RPC '{methodName}' blocked - sender {sender?.NickName ?? "null"} failed security check");
                        return;
                    }

                    try
                    {
                        // Add sender info if method expects it
                        var methodParams = method.GetParameters();
                        object[] invokeParams = parameters;

                        if (methodParams.Length > 0 && methodParams[methodParams.Length - 1].ParameterType == typeof(EpicMessageInfo))
                        {
                            var messageInfo = new EpicMessageInfo
                            {
                                Sender = sender,
                                Timestamp = Time.timeAsDouble
                            };

                            invokeParams = new object[parameters.Length + 1];
                            Array.Copy(parameters, invokeParams, parameters.Length);
                            invokeParams[invokeParams.Length - 1] = messageInfo;
                        }

                        method.Invoke(component, invokeParams);
                        return;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[EpicNet] Error executing RPC '{methodName}': {e.Message}");
                    }
                }
            }

            Debug.LogWarning($"[EpicNet] RPC method '{methodName}' not found on view {viewId}");
        }

        /// <summary>
        /// Validates if a sender is allowed to call an RPC based on its security level.
        /// </summary>
        private static bool ValidateRpcSecurity(RpcSecurityLevel security, EpicPlayer sender, EpicView view)
        {
            if (sender == null) return false;

            switch (security)
            {
                case RpcSecurityLevel.Anyone:
                    return true;

                case RpcSecurityLevel.OwnerOnly:
                    return view.Owner != null && sender == view.Owner;

                case RpcSecurityLevel.MasterClientOnly:
                    return sender.IsMasterClient;

                case RpcSecurityLevel.OwnerOrMasterClient:
                    return sender.IsMasterClient || (view.Owner != null && sender == view.Owner);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Update loop - call this from MonoBehaviour Update
        /// </summary>
        public static void Update()
        {
            // Process delayed callbacks
            ProcessDelayedActions();

            // Handle auto-reconnection when not in room
            if (!InRoom)
            {
                TryAutoReconnect();
                return;
            }

            // Process P2P messages
            ProcessP2PMessages();

            // Try to send pending initial states (for late joiners)
            TrySendPendingInitialStates();

            // Cleanup expired buffered RPCs
            CleanupExpiredBufferedRPCs();

            // Sync observables at the configured rate
            if (Time.time - _lastSyncTime >= _syncInterval)
            {
                SyncObservables();
                _lastSyncTime = Time.time;
            }
        }

        private static float _lastBufferedRPCCleanup = 0f;

        private static void CleanupExpiredBufferedRPCs()
        {
            // Only cleanup once per second to avoid overhead
            if (Time.time - _lastBufferedRPCCleanup < BufferedRpcCleanupInterval) return;
            _lastBufferedRPCCleanup = Time.time;

            double currentTime = Time.timeAsDouble;
            lock (_bufferedRPCsLock)
            {
                _bufferedRPCs.RemoveAll(rpc => currentTime - rpc.Timestamp > BufferedRpcLifetime);
            }
        }

        private static void ProcessDelayedActions()
        {
            lock (_delayedActionsLock)
            {
                for (int i = _delayedActions.Count - 1; i >= 0; i--)
                {
                    if (Time.time >= _delayedActions[i].TriggerTime)
                    {
                        var action = _delayedActions[i];
                        _delayedActions.RemoveAt(i);

                        // Invoke outside the lock to prevent deadlocks
                        try
                        {
                            action.Callback?.Invoke();
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[EpicNet] Error in delayed action: {e.Message}");
                        }
                    }
                }
            }
        }

        private static void SyncObservables()
        {
            // Create thread-safe snapshots
            List<EpicView> views;
            lock (_networkObjectsLock)
            {
                views = _networkObjects.Values.ToList();
            }

            List<EpicPlayer> playerSnapshot;
            lock (_playerListLock)
            {
                playerSnapshot = new List<EpicPlayer>(_playerList);
            }

            foreach (var view in views)
            {
                // Check for null view, destroyed gameObject, or not owned by us
                if (view == null || view.gameObject == null || !view.IsMine) continue;

                IEpicObservable[] observables;
                try
                {
                    observables = view.GetComponents<IEpicObservable>();
                }
                catch (MissingReferenceException)
                {
                    // Object was destroyed during iteration - remove from tracking
                    lock (_networkObjectsLock)
                    {
                        _networkObjects.Remove(view.ViewID);
                    }
                    continue;
                }
                catch (NullReferenceException)
                {
                    // Object reference became null
                    continue;
                }

                if (observables == null || observables.Length == 0) continue;

                // Create write stream
                var stream = new EpicStream(true);
                var messageInfo = new EpicMessageInfo
                {
                    Sender = _localPlayer,
                    Timestamp = Time.timeAsDouble
                };

                // Serialize all observables
                foreach (var observable in observables)
                {
                    if (observable == null) continue;
                    observable.OnEpicSerializeView(stream, messageInfo);
                }

                // Send to other players
                if (stream.HasData())
                {
                    byte[] data = SerializeObservableData(view.ViewID, stream);

                    foreach (var player in playerSnapshot)
                    {
                        if (player.UserId != _localUserId)
                        {
                            SendP2PPacket(player.UserId, data);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Process incoming P2P packets
        /// </summary>
        public static void ProcessP2PMessages()
        {
            if (_p2pInterface == null || !InRoom) return;

            var getNextOptions = new GetNextReceivedPacketSizeOptions
            {
                LocalUserId = _localUserId,
                RequestedChannel = null
            };

            uint nextPacketSize;
            var result = _p2pInterface.GetNextReceivedPacketSize(ref getNextOptions, out nextPacketSize);

            while (result == Result.Success && nextPacketSize > 0)
            {
                var receiveOptions = new ReceivePacketOptions
                {
                    LocalUserId = _localUserId,
                    MaxDataSizeBytes = nextPacketSize,
                    RequestedChannel = null
                };

                byte[] data = new byte[nextPacketSize];
                ProductUserId peerId = null;
                SocketId socketId = new SocketId();
                byte outChannel;
                uint bytesWritten;

                var receiveResult = _p2pInterface.ReceivePacket(ref receiveOptions, ref peerId, ref socketId, out outChannel, new ArraySegment<byte>(data), out bytesWritten);

                if (receiveResult == Result.Success)
                {
                    _bytesReceived += bytesWritten;
                    _packetsReceived++;
                    ProcessReceivedPacket(peerId, data);
                }

                // Check for next packet
                result = _p2pInterface.GetNextReceivedPacketSize(ref getNextOptions, out nextPacketSize);
            }
        }

        private static void ProcessReceivedPacket(ProductUserId senderId, byte[] data)
        {
            if (data.Length < 1) return;

            PacketType packetType = (PacketType)data[0];

            switch (packetType)
            {
                case PacketType.OwnershipTransfer:
                    HandleOwnershipTransferReceived(senderId, data);
                    break;
                case PacketType.OwnershipRequest:
                    HandleOwnershipRequestReceived(senderId, data);
                    break;
                case PacketType.OwnershipResponse:
                    HandleOwnershipResponseReceived(senderId, data);
                    break;
                case PacketType.RPC:
                    HandleRPCReceived(senderId, data);
                    break;
                case PacketType.ObservableData:
                    HandleObservableDataReceived(senderId, data);
                    break;
                case PacketType.InstantiateObject:
                    HandleInstantiateReceived(senderId, data);
                    break;
                case PacketType.DestroyObject:
                    HandleDestroyReceived(senderId, data);
                    break;
                case PacketType.Ping:
                    HandlePingReceived(senderId, data);
                    break;
                case PacketType.Pong:
                    HandlePongReceived(senderId, data);
                    break;
                case PacketType.InitialState:
                    HandleInitialStateReceived(senderId, data);
                    break;
                case PacketType.Kick:
                    HandleKickReceived(senderId, data);
                    break;
            }
        }

        private static void HandleOwnershipTransferReceived(ProductUserId senderId, byte[] data)
        {
            if (data.Length < 9) return;

            int viewId = BitConverter.ToInt32(data, 1);
            int ownerIdLength = BitConverter.ToInt32(data, 5);

            // Validate length before reading string
            if (data.Length < 9 + ownerIdLength)
            {
                Debug.LogWarning("[EpicNet] Malformed ownership transfer packet - ignoring");
                return;
            }

            string ownerIdString = System.Text.Encoding.UTF8.GetString(data, 9, ownerIdLength);

            EpicView view;
            lock (_networkObjectsLock)
            {
                _networkObjects.TryGetValue(viewId, out view);
            }

            if (view == null) return;

            // Security validation - only the current owner or master client can transfer ownership
            EpicPlayer sender;
            lock (_playerListLock)
            {
                sender = _playerList.Find(p => p.UserId != null && p.UserId.Equals(senderId));
            }

            bool isCurrentOwner = view.Owner != null && view.Owner.UserId != null && view.Owner.UserId.Equals(senderId);
            bool isMaster = sender != null && sender.IsMasterClient;

            // For Fixed ownership, only master can force transfer
            if (view.OwnershipTransfer == OwnershipOption.Fixed && !isMaster)
            {
                Debug.LogWarning($"[EpicNet] Ownership transfer rejected for ViewID {viewId} - ownership is Fixed");
                return;
            }

            // For Request ownership, only the owner can approve (already validated by the request flow)
            // For Takeover, anyone can take ownership

            if (!isCurrentOwner && !isMaster && view.OwnershipTransfer != OwnershipOption.Takeover)
            {
                Debug.LogWarning($"[EpicNet] Ownership transfer rejected for ViewID {viewId} - sender is not authorized");
                return;
            }

            EpicPlayer newOwner;
            lock (_playerListLock)
            {
                newOwner = _playerList.Find(p => p.UserId != null && p.UserId.ToString() == ownerIdString);
            }

            if (newOwner != null)
            {
                view.Owner = newOwner;
                Debug.Log($"[EpicNet] View {viewId} ownership transferred to {newOwner.NickName}");
            }
        }

        private static void HandleOwnershipRequestReceived(ProductUserId senderId, byte[] data)
        {
            if (data.Length < 9) return;

            int viewId = BitConverter.ToInt32(data, 1);
            int requestId = BitConverter.ToInt32(data, 5);

            EpicView view;
            lock (_networkObjectsLock)
            {
                _networkObjects.TryGetValue(viewId, out view);
            }

            if (view != null)
            {
                EpicPlayer requester;
                lock (_playerListLock)
                {
                    requester = _playerList.Find(p => p.UserId == senderId);
                }

                if (requester != null)
                {
                    view.HandleOwnershipRequest(requester, (approved) =>
                    {
                        byte[] response = SerializeOwnershipResponse(requestId, approved);
                        SendP2PPacket(senderId, response);
                    });
                }
            }
        }

        private static void HandleOwnershipResponseReceived(ProductUserId senderId, byte[] data)
        {
            if (data.Length < 6) return;

            int requestId = BitConverter.ToInt32(data, 1);
            bool approved = data[5] == 1;

            if (_ownershipRequestCallbacks.TryGetValue(requestId, out Action<bool> callback))
            {
                callback?.Invoke(approved);
                _ownershipRequestCallbacks.Remove(requestId);
            }
        }

        private static void HandleRPCReceived(ProductUserId senderId, byte[] data)
        {
            // Deserialize RPC: [PacketType(1)][ViewID(4)][MethodNameLength(4)][MethodName][ParamCount(4)][Params...]
            if (data.Length < 13) return;

            int offset = 1;
            int viewId = BitConverter.ToInt32(data, offset);
            offset += 4;

            int methodNameLength = BitConverter.ToInt32(data, offset);
            offset += 4;

            string methodName = System.Text.Encoding.UTF8.GetString(data, offset, methodNameLength);
            offset += methodNameLength;

            int paramCount = BitConverter.ToInt32(data, offset);
            offset += 4;

            object[] parameters = new object[paramCount];
            for (int i = 0; i < paramCount; i++)
            {
                parameters[i] = DeserializeParameter(data, ref offset);
            }

            EpicPlayer sender;
            lock (_playerListLock)
            {
                sender = _playerList.Find(p => p.UserId == senderId);
            }

            if (sender != null)
            {
                ExecuteRPC(viewId, methodName, parameters, sender);
            }
        }

        private static void HandleObservableDataReceived(ProductUserId senderId, byte[] data)
        {
            // Deserialize: [PacketType(1)][ViewID(4)][StreamData...]
            if (data.Length < 5) return;

            int viewId = BitConverter.ToInt32(data, 1);

            EpicView view;
            lock (_networkObjectsLock)
            {
                _networkObjects.TryGetValue(viewId, out view);
            }

            if (view != null)
            {
                // Check for destroyed objects
                if (view.gameObject == null) return;

                var stream = new EpicStream(false);
                DeserializeObservableStream(data, 5, stream);

                EpicPlayer sender;
                lock (_playerListLock)
                {
                    sender = _playerList.Find(p => p.UserId == senderId);
                }

                var messageInfo = new EpicMessageInfo
                {
                    Sender = sender,
                    Timestamp = Time.timeAsDouble
                };

                IEpicObservable[] observables;
                try
                {
                    observables = view.GetComponents<IEpicObservable>();
                }
                catch (MissingReferenceException)
                {
                    // Object destroyed
                    return;
                }
                catch (NullReferenceException)
                {
                    // Object reference became null
                    return;
                }

                foreach (var observable in observables)
                {
                    if (observable == null) continue;
                    observable.OnEpicSerializeView(stream, messageInfo);
                }
            }
        }

        private static void HandleInstantiateReceived(ProductUserId senderId, byte[] data)
        {
            // Deserialize: [PacketType(1)][ViewID(4)][PrefabNameLength(4)][PrefabName][Position(12)][Rotation(16)][OwnerIdLength(4)][OwnerId]
            if (data.Length < 45) return;

            int offset = 1;
            int viewId = BitConverter.ToInt32(data, offset);
            offset += 4;

            int prefabNameLength = BitConverter.ToInt32(data, offset);
            offset += 4;

            string prefabName = System.Text.Encoding.UTF8.GetString(data, offset, prefabNameLength);
            offset += prefabNameLength;

            Vector3 position = new Vector3(
                BitConverter.ToSingle(data, offset),
                BitConverter.ToSingle(data, offset + 4),
                BitConverter.ToSingle(data, offset + 8)
            );
            offset += 12;

            Quaternion rotation = new Quaternion(
                BitConverter.ToSingle(data, offset),
                BitConverter.ToSingle(data, offset + 4),
                BitConverter.ToSingle(data, offset + 8),
                BitConverter.ToSingle(data, offset + 12)
            );
            offset += 16;

            int ownerIdLength = BitConverter.ToInt32(data, offset);
            offset += 4;

            string ownerIdString = System.Text.Encoding.UTF8.GetString(data, offset, ownerIdLength);

            // Instantiate the object (use pool if enabled)
            GameObject obj;
            if (EpicPool.Enabled)
            {
                obj = EpicPool.Get(prefabName, position, rotation);
            }
            else
            {
                GameObject prefab = Resources.Load<GameObject>(prefabName);
                if (prefab == null) return;
                obj = UnityEngine.Object.Instantiate(prefab, position, rotation);
            }

            if (obj != null)
            {
                var view = obj.GetComponent<EpicView>();

                if (view != null)
                {
                    view.ViewID = viewId;

                    // Try to find owner in player list
                    EpicPlayer owner;
                    lock (_playerListLock)
                    {
                        owner = _playerList.Find(p => p.UserId.ToString() == ownerIdString);

                        // Fallback: use sender as owner if owner not found
                        if (owner == null)
                        {
                            owner = _playerList.Find(p => p.UserId == senderId);
                            if (owner == null)
                            {
                                Debug.LogWarning($"EpicNet: Owner not found for instantiated object {viewId}, using null owner");
                            }
                        }
                    }

                    view.Owner = owner;
                    view.PrefabName = prefabName;
                    RegisterNetworkObject(view);
                }
            }
        }

        private static void HandleDestroyReceived(ProductUserId senderId, byte[] data)
        {
            if (data.Length < 5) return;

            int viewId = BitConverter.ToInt32(data, 1);

            EpicView view;
            lock (_networkObjectsLock)
            {
                _networkObjects.TryGetValue(viewId, out view);
            }

            if (view != null)
            {
                // Security validation - verify sender owns the object or is master client
                EpicPlayer sender;
                lock (_playerListLock)
                {
                    sender = _playerList.Find(p => p.UserId != null && p.UserId.Equals(senderId));
                }

                bool isOwner = view.Owner != null && view.Owner.UserId != null && view.Owner.UserId.Equals(senderId);
                bool isMaster = sender != null && sender.IsMasterClient;

                if (!isOwner && !isMaster)
                {
                    Debug.LogWarning($"[EpicNet] Destroy request for ViewID {viewId} rejected - sender is not owner or master");
                    return;
                }

                UnregisterNetworkObject(viewId);
                if (view.gameObject != null)
                {
                    // Return to pool if enabled
                    if (EpicPool.Enabled && !string.IsNullOrEmpty(view.PrefabName))
                    {
                        EpicPool.Return(view.gameObject);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(view.gameObject);
                    }
                }
            }
        }

        private static void HandlePingReceived(ProductUserId senderId, byte[] data)
        {
            // Send pong back
            byte[] pongData = new byte[1];
            pongData[0] = (byte)PacketType.Pong;
            SendP2PPacket(senderId, pongData);
        }

        private static void HandlePongReceived(ProductUserId senderId, byte[] data)
        {
            if (_playerPingTimes.TryGetValue(senderId, out float pingTime))
            {
                float latency = Time.time - pingTime;
                Ping = (int)(latency * 1000f);
                _playerPingTimes.Remove(senderId);

                // Track ping history for averages
                _recentPings.Enqueue(Ping);
                while (_recentPings.Count > 20) // Keep last 20 pings
                {
                    _recentPings.Dequeue();
                }
            }
        }

        private static void HandleKickReceived(ProductUserId senderId, byte[] data)
        {
            // Security validation - verify kick came from master client
            if (_masterClient == null || _masterClient.UserId == null ||
                senderId == null || !_masterClient.UserId.Equals(senderId))
            {
                Debug.LogWarning("[EpicNet] Received kick from non-master client - ignoring potential attack");
                return;
            }

            string reason = "";
            if (data.Length > 5)
            {
                int reasonLength = BitConverter.ToInt32(data, 1);
                if (data.Length >= 5 + reasonLength)
                {
                    reason = System.Text.Encoding.UTF8.GetString(data, 5, reasonLength);
                }
            }

            Debug.Log($"EpicNet: Kicked from room: {reason}");

            // Disable auto-reconnect for kicks
            _wasInRoom = false;
            _isReconnecting = false;

            OnKicked?.Invoke(reason);
            LeaveRoom();
        }

        private static void HandleInitialStateReceived(ProductUserId senderId, byte[] data)
        {
            // Deserialize initial state with all network objects and buffered RPCs
            int offset = 1;

            // Object count
            int objectCount = BitConverter.ToInt32(data, offset);
            offset += 4;

            for (int i = 0; i < objectCount; i++)
            {
                // Each object: ViewID, PrefabName, Position, Rotation, OwnerId
                int viewId = BitConverter.ToInt32(data, offset);
                offset += 4;

                int prefabNameLength = BitConverter.ToInt32(data, offset);
                offset += 4;

                string prefabName = System.Text.Encoding.UTF8.GetString(data, offset, prefabNameLength);
                offset += prefabNameLength;

                Vector3 position = new Vector3(
                    BitConverter.ToSingle(data, offset),
                    BitConverter.ToSingle(data, offset + 4),
                    BitConverter.ToSingle(data, offset + 8)
                );
                offset += 12;

                Quaternion rotation = new Quaternion(
                    BitConverter.ToSingle(data, offset),
                    BitConverter.ToSingle(data, offset + 4),
                    BitConverter.ToSingle(data, offset + 8),
                    BitConverter.ToSingle(data, offset + 12)
                );
                offset += 16;

                int ownerIdLength = BitConverter.ToInt32(data, offset);
                offset += 4;

                string ownerIdString = System.Text.Encoding.UTF8.GetString(data, offset, ownerIdLength);
                offset += ownerIdLength;

                // Instantiate using pool if enabled
                GameObject obj;
                if (EpicPool.Enabled)
                {
                    obj = EpicPool.Get(prefabName, position, rotation);
                }
                else
                {
                    GameObject prefab = Resources.Load<GameObject>(prefabName);
                    if (prefab == null) continue;
                    obj = UnityEngine.Object.Instantiate(prefab, position, rotation);
                }

                if (obj != null)
                {
                    var view = obj.GetComponent<EpicView>();

                    if (view != null)
                    {
                        view.ViewID = viewId;

                        // Try to find owner in player list, fallback to sender
                        EpicPlayer owner;
                        lock (_playerListLock)
                        {
                            owner = _playerList.Find(p => p.UserId.ToString() == ownerIdString);
                            if (owner == null)
                            {
                                owner = _playerList.Find(p => p.UserId == senderId);
                            }
                        }

                        view.Owner = owner;
                        if (owner == null)
                        {
                            Debug.LogWarning($"EpicNet: Could not find owner for late-joined object {viewId}, assigning to master client");
                            view.Owner = _masterClient;
                        }
                        view.PrefabName = prefabName;
                        RegisterNetworkObject(view);
                    }
                }
            }

            // Buffered RPCs
            int rpcCount = BitConverter.ToInt32(data, offset);
            offset += 4;

            for (int i = 0; i < rpcCount; i++)
            {
                int viewId = BitConverter.ToInt32(data, offset);
                offset += 4;

                int methodNameLength = BitConverter.ToInt32(data, offset);
                offset += 4;

                string methodName = System.Text.Encoding.UTF8.GetString(data, offset, methodNameLength);
                offset += methodNameLength;

                int paramCount = BitConverter.ToInt32(data, offset);
                offset += 4;

                object[] parameters = new object[paramCount];
                for (int j = 0; j < paramCount; j++)
                {
                    parameters[j] = DeserializeParameter(data, ref offset);
                }

                EpicPlayer sender;
                lock (_playerListLock)
                {
                    sender = _playerList.Find(p => p.UserId == senderId);
                }
                ExecuteRPC(viewId, methodName, parameters, sender);
            }

            Debug.Log($"EpicNet: Received initial state - {objectCount} objects, {rpcCount} buffered RPCs");
        }

        private static void SendInitialStateToPlayer(EpicPlayer player)
        {
            if (player == null || player.UserId == null)
            {
                Debug.LogWarning("EpicNet: Cannot send initial state - invalid player");
                return;
            }

            byte[] stateData = SerializeInitialState();

            if (stateData.Length > 1) // More than just the packet type byte
            {
                SendP2PPacket(player.UserId, stateData);
                Debug.Log($"EpicNet: Sent initial state to {player.NickName} ({stateData.Length} bytes, {_networkObjects.Count} objects, {_bufferedRPCs.Count} RPCs)");
            }
            else
            {
                Debug.Log($"EpicNet: No initial state to send (empty room)");
            }
        }

        /// <summary>
        /// Send ownership transfer notification to all players
        /// </summary>
        internal static void SendOwnershipTransfer(int viewId, EpicPlayer newOwner)
        {
            if (!InRoom || newOwner == null || newOwner.UserId == null) return;

            byte[] data = SerializeOwnershipTransfer(viewId, newOwner);

            List<EpicPlayer> playerSnapshot;
            lock (_playerListLock)
            {
                playerSnapshot = new List<EpicPlayer>(_playerList);
            }

            foreach (var player in playerSnapshot)
            {
                if (player.UserId != _localUserId)
                {
                    SendP2PPacket(player.UserId, data);
                }
            }
        }

        /// <summary>
        /// Send ownership request to the owner
        /// </summary>
        internal static void SendOwnershipRequest(int viewId, EpicPlayer owner, Action<bool> callback)
        {
            if (!InRoom || owner == null || owner.UserId == null)
            {
                callback?.Invoke(false);
                return;
            }

            // Thread-safe request ID generation with overflow handling
            int requestId;
            lock (_ownershipRequestCallbacks)
            {
                // Reset counter if it gets too high to prevent overflow
                if (_ownershipRequestIdCounter > int.MaxValue - 1000)
                {
                    _ownershipRequestIdCounter = 0;
                }
                requestId = _ownershipRequestIdCounter++;
                _ownershipRequestCallbacks[requestId] = callback;
            }

            byte[] data = SerializeOwnershipRequest(viewId, requestId);
            SendP2PPacket(owner.UserId, data);

            // Timeout after 5 seconds
            DelayedCallback(5f, () =>
            {
                lock (_ownershipRequestCallbacks)
                {
                    if (_ownershipRequestCallbacks.TryGetValue(requestId, out var cb))
                    {
                        cb?.Invoke(false);
                        _ownershipRequestCallbacks.Remove(requestId);
                    }
                }
            });
        }

        // Continuation of EpicNetwork.cs - Serialization & Helper Methods

        #region Serialization Methods

        private static byte[] SerializeOwnershipTransfer(int viewId, EpicPlayer newOwner)
        {
            byte[] ownerIdBytes = System.Text.Encoding.UTF8.GetBytes(newOwner.UserId.ToString());
            byte[] data = new byte[1 + 4 + 4 + ownerIdBytes.Length];

            data[0] = (byte)PacketType.OwnershipTransfer;
            BitConverter.GetBytes(viewId).CopyTo(data, 1);
            BitConverter.GetBytes(ownerIdBytes.Length).CopyTo(data, 5);
            ownerIdBytes.CopyTo(data, 9);

            return data;
        }

        private static byte[] SerializeOwnershipRequest(int viewId, int requestId)
        {
            byte[] data = new byte[1 + 4 + 4];

            data[0] = (byte)PacketType.OwnershipRequest;
            BitConverter.GetBytes(viewId).CopyTo(data, 1);
            BitConverter.GetBytes(requestId).CopyTo(data, 5);

            return data;
        }

        private static byte[] SerializeOwnershipResponse(int requestId, bool approved)
        {
            byte[] data = new byte[1 + 4 + 1];

            data[0] = (byte)PacketType.OwnershipResponse;
            BitConverter.GetBytes(requestId).CopyTo(data, 1);
            data[5] = approved ? (byte)1 : (byte)0;

            return data;
        }

        private static byte[] SerializeRPC(int viewId, string methodName, object[] parameters, int rpcId, RpcTarget target)
        {
            using (var stream = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(stream))
            {
                writer.Write((byte)PacketType.RPC);
                writer.Write(viewId);
                writer.Write(methodName.Length);
                writer.Write(System.Text.Encoding.UTF8.GetBytes(methodName));
                writer.Write(parameters.Length);

                foreach (var param in parameters)
                {
                    SerializeParameter(writer, param);
                }

                return stream.ToArray();
            }
        }

        private static void SerializeParameter(System.IO.BinaryWriter writer, object param)
        {
            if (param == null)
            {
                writer.Write((byte)0); // null
                return;
            }

            Type type = param.GetType();

            if (type == typeof(int))
            {
                writer.Write((byte)1);
                writer.Write((int)param);
            }
            else if (type == typeof(float))
            {
                writer.Write((byte)2);
                writer.Write((float)param);
            }
            else if (type == typeof(string))
            {
                writer.Write((byte)3);
                string str = (string)param;
                writer.Write(str.Length);
                writer.Write(System.Text.Encoding.UTF8.GetBytes(str));
            }
            else if (type == typeof(bool))
            {
                writer.Write((byte)4);
                writer.Write((bool)param);
            }
            else if (type == typeof(Vector3))
            {
                writer.Write((byte)5);
                Vector3 v = (Vector3)param;
                writer.Write(v.x);
                writer.Write(v.y);
                writer.Write(v.z);
            }
            else if (type == typeof(Quaternion))
            {
                writer.Write((byte)6);
                Quaternion q = (Quaternion)param;
                writer.Write(q.x);
                writer.Write(q.y);
                writer.Write(q.z);
                writer.Write(q.w);
            }
            else if (type == typeof(byte[]))
            {
                writer.Write((byte)7);
                byte[] bytes = (byte[])param;
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
            else
            {
                // Fallback to string representation
                writer.Write((byte)3);
                string str = param.ToString();
                writer.Write(str.Length);
                writer.Write(System.Text.Encoding.UTF8.GetBytes(str));
            }
        }

        private static object DeserializeParameter(byte[] data, ref int offset)
        {
            // Validate we have at least 1 byte for type
            if (offset >= data.Length)
            {
                Debug.LogWarning("EpicNet: Buffer underflow in DeserializeParameter - no type byte");
                return null;
            }

            byte typeId = data[offset++];

            switch (typeId)
            {
                case 0: // null
                    return null;

                case 1: // int
                    if (offset + 4 > data.Length)
                    {
                        Debug.LogWarning("EpicNet: Buffer underflow reading int");
                        return null;
                    }
                    int intVal = BitConverter.ToInt32(data, offset);
                    offset += 4;
                    return intVal;

                case 2: // float
                    if (offset + 4 > data.Length)
                    {
                        Debug.LogWarning("EpicNet: Buffer underflow reading float");
                        return null;
                    }
                    float floatVal = BitConverter.ToSingle(data, offset);
                    offset += 4;
                    return floatVal;

                case 3: // string
                    if (offset + 4 > data.Length)
                    {
                        Debug.LogWarning("EpicNet: Buffer underflow reading string length");
                        return null;
                    }
                    int strLength = BitConverter.ToInt32(data, offset);
                    offset += 4;
                    // Use subtraction to avoid integer overflow on bounds check
                    if (strLength < 0 || strLength > data.Length - offset)
                    {
                        Debug.LogWarning($"EpicNet: Invalid string length {strLength} or buffer underflow");
                        return null;
                    }
                    string strVal = System.Text.Encoding.UTF8.GetString(data, offset, strLength);
                    offset += strLength;
                    return strVal;

                case 4: // bool
                    if (offset >= data.Length)
                    {
                        Debug.LogWarning("EpicNet: Buffer underflow reading bool");
                        return null;
                    }
                    bool boolVal = data[offset++] == 1;
                    return boolVal;

                case 5: // Vector3
                    if (offset + 12 > data.Length)
                    {
                        Debug.LogWarning("EpicNet: Buffer underflow reading Vector3");
                        return null;
                    }
                    Vector3 vec = new Vector3(
                        BitConverter.ToSingle(data, offset),
                        BitConverter.ToSingle(data, offset + 4),
                        BitConverter.ToSingle(data, offset + 8)
                    );
                    offset += 12;
                    return vec;

                case 6: // Quaternion
                    if (offset + 16 > data.Length)
                    {
                        Debug.LogWarning("EpicNet: Buffer underflow reading Quaternion");
                        return null;
                    }
                    Quaternion quat = new Quaternion(
                        BitConverter.ToSingle(data, offset),
                        BitConverter.ToSingle(data, offset + 4),
                        BitConverter.ToSingle(data, offset + 8),
                        BitConverter.ToSingle(data, offset + 12)
                    );
                    offset += 16;
                    return quat;

                case 7: // byte[]
                    if (offset + 4 > data.Length)
                    {
                        Debug.LogWarning("EpicNet: Buffer underflow reading byte[] length");
                        return null;
                    }
                    int byteLength = BitConverter.ToInt32(data, offset);
                    offset += 4;
                    // Use subtraction to avoid integer overflow on bounds check
                    if (byteLength < 0 || byteLength > data.Length - offset)
                    {
                        Debug.LogWarning($"EpicNet: Invalid byte[] length {byteLength} or buffer underflow");
                        return null;
                    }
                    byte[] byteArray = new byte[byteLength];
                    Array.Copy(data, offset, byteArray, 0, byteLength);
                    offset += byteLength;
                    return byteArray;

                default:
                    Debug.LogWarning($"EpicNet: Unknown parameter type {typeId}");
                    return null;
            }
        }

        private static byte[] SerializeObservableData(int viewId, EpicStream stream)
        {
            using (var memStream = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(memStream))
            {
                writer.Write((byte)PacketType.ObservableData);
                writer.Write(viewId);

                // Write stream data
                var dataList = stream.GetDataList();
                writer.Write(dataList.Count);

                foreach (var item in dataList)
                {
                    SerializeParameter(writer, item);
                }

                return memStream.ToArray();
            }
        }

        private static void DeserializeObservableStream(byte[] data, int startOffset, EpicStream stream)
        {
            int offset = startOffset;
            int itemCount = BitConverter.ToInt32(data, offset);
            offset += 4;

            for (int i = 0; i < itemCount; i++)
            {
                object item = DeserializeParameter(data, ref offset);
                stream.EnqueueData(item);
            }
        }

        private static byte[] SerializeInstantiateMessage(int viewId, string prefabName, Vector3 position, Quaternion rotation, EpicPlayer owner)
        {
            using (var stream = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(stream))
            {
                writer.Write((byte)PacketType.InstantiateObject);
                writer.Write(viewId);
                writer.Write(prefabName.Length);
                writer.Write(System.Text.Encoding.UTF8.GetBytes(prefabName));
                writer.Write(position.x);
                writer.Write(position.y);
                writer.Write(position.z);
                writer.Write(rotation.x);
                writer.Write(rotation.y);
                writer.Write(rotation.z);
                writer.Write(rotation.w);

                byte[] ownerIdBytes = System.Text.Encoding.UTF8.GetBytes(owner.UserId.ToString());
                writer.Write(ownerIdBytes.Length);
                writer.Write(ownerIdBytes);

                return stream.ToArray();
            }
        }

        private static byte[] SerializeDestroyMessage(int viewId)
        {
            byte[] data = new byte[5];
            data[0] = (byte)PacketType.DestroyObject;
            BitConverter.GetBytes(viewId).CopyTo(data, 1);
            return data;
        }

        private static byte[] SerializeInitialState()
        {
            using (var stream = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(stream))
            {
                writer.Write((byte)PacketType.InitialState);

                // Get thread-safe snapshot of network objects
                List<EpicView> validObjects;
                lock (_networkObjectsLock)
                {
                    validObjects = _networkObjects.Values
                        .Where(v => v != null && v.Owner != null && v.Owner.UserId != null && !string.IsNullOrEmpty(v.PrefabName))
                        .ToList();
                }

                // Serialize all valid network objects
                writer.Write(validObjects.Count);

                foreach (var view in validObjects)
                {
                    // Skip if the view was destroyed since we took the snapshot
                    if (view == null || view.gameObject == null) continue;

                    writer.Write(view.ViewID);
                    writer.Write(view.PrefabName.Length);
                    writer.Write(System.Text.Encoding.UTF8.GetBytes(view.PrefabName));
                    writer.Write(view.transform.position.x);
                    writer.Write(view.transform.position.y);
                    writer.Write(view.transform.position.z);
                    writer.Write(view.transform.rotation.x);
                    writer.Write(view.transform.rotation.y);
                    writer.Write(view.transform.rotation.z);
                    writer.Write(view.transform.rotation.w);

                    byte[] ownerIdBytes = System.Text.Encoding.UTF8.GetBytes(view.Owner.UserId.ToString());
                    writer.Write(ownerIdBytes.Length);
                    writer.Write(ownerIdBytes);
                }

                // Get thread-safe snapshot of buffered RPCs
                List<BufferedRPC> bufferedRpcSnapshot;
                lock (_bufferedRPCsLock)
                {
                    bufferedRpcSnapshot = new List<BufferedRPC>(_bufferedRPCs);
                }

                writer.Write(bufferedRpcSnapshot.Count);

                foreach (var rpc in bufferedRpcSnapshot)
                {
                    writer.Write(rpc.ViewID);
                    writer.Write(rpc.MethodName.Length);
                    writer.Write(System.Text.Encoding.UTF8.GetBytes(rpc.MethodName));
                    writer.Write(rpc.Parameters.Length);

                    foreach (var param in rpc.Parameters)
                    {
                        SerializeParameter(writer, param);
                    }
                }

                return stream.ToArray();
            }
        }

        #endregion

        #region Internal Helper Methods

        private static void SendP2PPacket(ProductUserId targetUserId, byte[] data, bool reliable = true)
        {
            var sendOptions = new SendPacketOptions
            {
                LocalUserId = _localUserId,
                RemoteUserId = targetUserId,
                SocketId = new SocketId { SocketName = "EpicNet" },
                Channel = (byte)(reliable ? 0 : 1),
                Data = new ArraySegment<byte>(data),
                AllowDelayedDelivery = reliable,
                Reliability = reliable ? PacketReliability.ReliableOrdered : PacketReliability.UnreliableUnordered
            };

            var result = _p2pInterface.SendPacket(ref sendOptions);
            if (result == Result.Success)
            {
                _bytesSent += data.Length;
                _packetsSent++;
            }
            else
            {
                Debug.LogWarning($"EpicNet: Failed to send P2P packet: {result}");
            }
        }

        private static void DelayedCallback(float delay, Action callback)
        {
            lock (_delayedActionsLock)
            {
                _delayedActions.Add(new DelayedAction
                {
                    TriggerTime = Time.time + delay,
                    Callback = callback
                });
            }
        }

        private static void OnLobbyCreated(ref CreateLobbyCallbackInfo data, string roomName)
        {
            if (data.ResultCode == Result.Success)
            {
                _currentLobbyId = data.LobbyId;
                _currentRoom = new EpicRoom(data.LobbyId);
                _isMasterClient = true;
                _lastRoomName = roomName;
                _wasInRoom = true;

                _localPlayer.IsMasterClient = true;
                _masterClient = _localPlayer;
                lock (_playerListLock)
                {
                    _playerList.Add(_localPlayer);
                    _currentRoom.Players.Add(_localPlayer);
                }

                // Set room name as attribute
                SetRoomProperties(new Dictionary<string, object> { { "RoomName", roomName } });

                // Handle reconnection success
                if (_isReconnecting)
                {
                    _isReconnecting = false;
                    _reconnectAttempts = 0;
                    Debug.Log("EpicNet: Reconnection successful!");
                    OnReconnected?.Invoke();
                }

                Debug.Log($"EpicNet: Room created: {roomName}");
                OnJoinedRoom?.Invoke();
            }
            else
            {
                Debug.LogError($"EpicNet: Failed to create room: {data.ResultCode}");
                HandleReconnectFailure();
            }
        }

        private static void JoinLobby(LobbyDetails lobbyDetails)
        {
            var infoOptions = new LobbyDetailsCopyInfoOptions();
            var result = lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? lobbyInfo);

            if (result != Result.Success || !lobbyInfo.HasValue) return;

            // Check for password protection
            var passwordAttrOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = "_password" };
            if (lobbyDetails.CopyAttributeByKey(ref passwordAttrOptions, out Attribute? passwordAttr) == Result.Success && passwordAttr.HasValue)
            {
                string storedHash = passwordAttr.Value.Data.Value.Value.AsUtf8;

                if (!string.IsNullOrEmpty(storedHash))
                {
                    // Room has a password
                    if (string.IsNullOrEmpty(_pendingJoinPassword))
                    {
                        Debug.LogError("EpicNet: Room requires a password");
                        OnJoinRoomFailed?.Invoke("Room requires a password");
                        HandleReconnectFailure();
                        return;
                    }

                    string providedHash = HashPassword(_pendingJoinPassword);
                    if (!ConstantTimeCompare(providedHash, storedHash))
                    {
                        Debug.LogError("EpicNet: Incorrect room password");
                        OnJoinRoomFailed?.Invoke("Incorrect password");
                        HandleReconnectFailure();
                        return;
                    }
                }
            }

            var joinOptions = new JoinLobbyOptions
            {
                LocalUserId = _localUserId,
                LobbyDetailsHandle = lobbyDetails
            };

            _lobbyInterface.JoinLobby(ref joinOptions, null, OnLobbyJoined);
        }

        private static void OnLobbyJoined(ref JoinLobbyCallbackInfo data)
        {
            if (data.ResultCode == Result.Success)
            {
                _currentLobbyId = data.LobbyId;
                _currentRoom = new EpicRoom(data.LobbyId);
                _wasInRoom = true;

                // Initialize master client state as false - FetchLobbyMembers will correct if needed
                _isMasterClient = false;
                _localPlayer.IsMasterClient = false;
                _masterClient = null;

                lock (_playerListLock)
                {
                    _playerList.Add(_localPlayer);
                    _currentRoom.Players.Add(_localPlayer);
                }

                // Copy lobby details to fetch members using the correct method
                var copyOptions = new CopyLobbyDetailsHandleOptions
                {
                    LobbyId = data.LobbyId,
                    LocalUserId = _localUserId
                };

                var result = _lobbyInterface.CopyLobbyDetailsHandle(ref copyOptions, out LobbyDetails lobbyDetails);
                if (result == Result.Success && lobbyDetails != null)
                {
                    FetchLobbyMembers(lobbyDetails);
                    lobbyDetails.Release();
                }
                else
                {
                    Debug.LogWarning($"EpicNet: Could not copy lobby details: {result}. Master client status may be incorrect.");
                }

                // Handle reconnection success
                if (_isReconnecting)
                {
                    _isReconnecting = false;
                    _reconnectAttempts = 0;
                    Debug.Log("EpicNet: Reconnection successful!");
                    OnReconnected?.Invoke();
                }

                Debug.Log($"EpicNet: Joined room: {data.LobbyId}");
                OnJoinedRoom?.Invoke();
            }
            else
            {
                Debug.LogError($"EpicNet: Failed to join room: {data.ResultCode}");
                HandleReconnectFailure();
            }
        }

        private static void FetchLobbyMembers(LobbyDetails lobbyDetails)
        {
            if (lobbyDetails == null)
            {
                Debug.LogError("EpicNet: Cannot fetch members - no lobby details");
                return;
            }

            // First, get the lobby owner ID
            var infoOptions = new LobbyDetailsCopyInfoOptions();
            var infoResult = lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? lobbyInfo);
            ProductUserId ownerId = null;

            if (infoResult == Result.Success && lobbyInfo.HasValue)
            {
                ownerId = lobbyInfo.Value.LobbyOwnerUserId;

                // Check if we (local player) are the lobby owner
                if (ownerId == _localUserId)
                {
                    _isMasterClient = true;
                    _localPlayer.IsMasterClient = true;
                    _masterClient = _localPlayer;
                    Debug.Log("EpicNet: Local player is the lobby owner (master client)");
                }
            }

            var countOptions = new LobbyDetailsGetMemberCountOptions();
            uint memberCount = lobbyDetails.GetMemberCount(ref countOptions);

            lock (_playerListLock)
            {
                for (uint i = 0; i < memberCount; i++)
                {
                    var memberOptions = new LobbyDetailsGetMemberByIndexOptions
                    {
                        MemberIndex = i
                    };

                    ProductUserId memberId = lobbyDetails.GetMemberByIndex(ref memberOptions);

                    // Skip ourselves (already added)
                    if (memberId == _localUserId) continue;

                    // Add other players
                    var player = new EpicPlayer(memberId, $"Player_{memberId}");

                    // Check if this member is the lobby owner
                    if (ownerId != null && memberId == ownerId)
                    {
                        player.IsMasterClient = true;
                        _masterClient = player;
                        Debug.Log($"EpicNet: Found master client: {player.NickName}");
                    }

                    _playerList.Add(player);
                    _currentRoom.Players.Add(player);
                }

                // If we didn't find a master, set the first player as master
                if (_masterClient == null && _playerList.Count > 0)
                {
                    var firstPlayer = _playerList[0];
                    firstPlayer.IsMasterClient = true;
                    _masterClient = firstPlayer;

                    // Update _isMasterClient if the first player is us
                    if (firstPlayer.UserId == _localUserId)
                    {
                        _isMasterClient = true;
                    }

                    Debug.Log($"EpicNet: No owner found, using first player as master: {firstPlayer.NickName}");
                }
            }
        }

        private static void OnLobbyLeft(ref LeaveLobbyCallbackInfo data)
        {
            if (data.ResultCode == Result.Success)
            {
                // Clear room players before nulling the room
                lock (_playerListLock)
                {
                    if (_currentRoom != null)
                    {
                        _currentRoom.Players.Clear();
                    }
                    _playerList.Clear();
                }

                _currentRoom = null;
                _currentLobbyId = null;
                _isMasterClient = false;
                _masterClient = null;

                // Clear local player's master status
                if (_localPlayer != null)
                {
                    _localPlayer.IsMasterClient = false;
                }

                lock (_networkObjectsLock)
                {
                    _networkObjects.Clear();
                }
                _bufferedRPCs.Clear();
                _pendingInitialState.Clear();

                // Reset actor counter for next room
                EpicPlayer.ResetActorCounter();

                Debug.Log("EpicNet: Left room");
                OnLeftRoom?.Invoke();
            }
        }

        private static int GenerateViewID()
        {
            // Use high bits for player actor number, low bits for counter
            // This ensures different players never generate the same ViewID
            int actorPart = (_localPlayer?.ActorNumber ?? 1) << 20; // Supports up to 4095 actors
            int counterPart = (_viewIdCounter++) & 0xFFFFF; // Supports ~1M objects per player
            return actorPart | counterPart;
        }

        private static void RegisterNetworkObject(EpicView view)
        {
            lock (_networkObjectsLock)
            {
                _networkObjects[view.ViewID] = view;
            }
        }

        /// <summary>
        /// Register a scene object and assign it a view ID
        /// Scene objects are automatically owned by the master client
        /// </summary>
        public static int RegisterSceneObject(EpicView view)
        {
            int viewId = GenerateViewID();
            view.ViewID = viewId;
            lock (_networkObjectsLock)
            {
                _networkObjects[viewId] = view;
            }
            return viewId;
        }

        public static void UnregisterNetworkObject(int viewId)
        {
            lock (_networkObjectsLock)
            {
                _networkObjects.Remove(viewId);
            }
        }

        /// <summary>
        /// Set custom properties for the local player
        /// </summary>
        public static void SetLocalPlayerProperties(Dictionary<string, object> properties)
        {
            if (!InRoom)
            {
                Debug.LogError("EpicNet: Not in a room!");
                return;
            }

            foreach (var kvp in properties)
            {
                var attributeData = new AttributeData
                {
                    Key = kvp.Key,
                    Value = ConvertToAttributeValue(kvp.Value)
                };

                var modifyOptions = new UpdateLobbyModificationOptions
                {
                    LobbyId = _currentLobbyId,
                    LocalUserId = _localUserId
                };

                var result = _lobbyInterface.UpdateLobbyModification(ref modifyOptions, out LobbyModification lobbyModification);

                if (result != Result.Success)
                {
                    Debug.LogError($"EpicNet: Failed to create lobby modification: {result}");
                    continue;
                }

                var addMemberAttributeOptions = new LobbyModificationAddMemberAttributeOptions
                {
                    Attribute = attributeData,
                    Visibility = LobbyAttributeVisibility.Public
                };

                result = lobbyModification.AddMemberAttribute(ref addMemberAttributeOptions);

                if (result != Result.Success)
                {
                    Debug.LogError($"EpicNet: Failed to add member attribute: {result}");
                    continue;
                }

                var updateOptions = new UpdateLobbyOptions
                {
                    LobbyModificationHandle = lobbyModification
                };

                _lobbyInterface.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo callbackInfo) =>
                {
                    lobbyModification.Release();
                    if (callbackInfo.ResultCode == Result.Success)
                    {
                        _localPlayer.CustomProperties[kvp.Key] = kvp.Value;
                        Debug.Log($"EpicNet: Player property '{kvp.Key}' set successfully");
                    }
                    else
                    {
                        Debug.LogError($"EpicNet: Failed to update player property '{kvp.Key}': {callbackInfo.ResultCode}");
                    }
                });
            }
        }

        /// <summary>
        /// Send a ping to measure latency
        /// </summary>
        public static void SendPing(EpicPlayer targetPlayer)
        {
            byte[] pingData = new byte[1];
            pingData[0] = (byte)PacketType.Ping;
            _playerPingTimes[targetPlayer.UserId] = Time.time;
            SendP2PPacket(targetPlayer.UserId, pingData);
        }

        /// <summary>
        /// Kick a player from the room (Master Client only)
        /// </summary>
        public static void KickPlayer(EpicPlayer player, string reason = "")
        {
            if (!IsMasterClient)
            {
                Debug.LogWarning("EpicNet: Only master client can kick players");
                return;
            }

            if (player == null || player.UserId == _localUserId)
            {
                Debug.LogWarning("EpicNet: Invalid kick target");
                return;
            }

            Debug.Log($"EpicNet: Kicking player {player.NickName}: {reason}");

            // Send kick notification
            byte[] reasonBytes = System.Text.Encoding.UTF8.GetBytes(reason ?? "");
            byte[] data = new byte[1 + 4 + reasonBytes.Length];
            data[0] = (byte)PacketType.Kick;
            BitConverter.GetBytes(reasonBytes.Length).CopyTo(data, 1);
            reasonBytes.CopyTo(data, 5);

            SendP2PPacket(player.UserId, data);

            // Remove from lobby via EOS
            var kickOptions = new KickMemberOptions
            {
                LobbyId = _currentLobbyId,
                LocalUserId = _localUserId,
                TargetUserId = player.UserId
            };

            _lobbyInterface.KickMember(ref kickOptions, null, (ref KickMemberCallbackInfo callbackInfo) =>
            {
                if (callbackInfo.ResultCode == Result.Success)
                {
                    Debug.Log($"EpicNet: Successfully kicked {player.NickName}");
                }
                else
                {
                    Debug.LogWarning($"EpicNet: Failed to kick player: {callbackInfo.ResultCode}");
                }
            });
        }

        /// <summary>
        /// Ban a player from rejoining the room (Master Client only, session-based)
        /// </summary>
        public static void BanPlayer(EpicPlayer player, string reason = "")
        {
            if (!IsMasterClient)
            {
                Debug.LogWarning("EpicNet: Only master client can ban players");
                return;
            }

            if (player == null)
            {
                Debug.LogWarning("EpicNet: Invalid ban target");
                return;
            }

            string playerId = player.UserId.ToString();
            _bannedPlayerIds.Add(playerId);
            Debug.Log($"EpicNet: Banned player {player.NickName}");

            // Also kick them
            KickPlayer(player, reason);
        }

        /// <summary>
        /// Unban a player by their user ID string
        /// </summary>
        public static void UnbanPlayer(string playerId)
        {
            _bannedPlayerIds.Remove(playerId);
            Debug.Log($"EpicNet: Unbanned player {playerId}");
        }

        /// <summary>
        /// Check if a player is banned by their user ID string
        /// </summary>
        public static bool IsPlayerBanned(string playerId)
        {
            return _bannedPlayerIds.Contains(playerId);
        }

        /// <summary>
        /// Clear all bans
        /// </summary>
        public static void ClearBans()
        {
            _bannedPlayerIds.Clear();
            Debug.Log("EpicNet: All bans cleared");
        }

        /// <summary>
        /// Manually trigger reconnection to the last room
        /// </summary>
        public static void Reconnect()
        {
            if (!_wasInRoom || string.IsNullOrEmpty(_lastRoomName))
            {
                Debug.LogWarning("EpicNet: No previous room to reconnect to");
                return;
            }

            if (InRoom)
            {
                Debug.LogWarning("EpicNet: Already in a room");
                return;
            }

            _isReconnecting = true;
            _reconnectAttempts = 0;
            OnReconnecting?.Invoke();
            Debug.Log($"EpicNet: Attempting to reconnect to room '{_lastRoomName}'...");
            JoinRoom(_lastRoomName, true);
        }

        private static void TryAutoReconnect()
        {
            if (!AutoReconnect || !_wasInRoom || string.IsNullOrEmpty(_lastRoomName))
                return;

            if (InRoom || _isReconnecting)
                return;

            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                Debug.LogWarning("EpicNet: Max reconnection attempts reached");
                _isReconnecting = false;
                _wasInRoom = false;
                OnReconnectFailed?.Invoke();
                return;
            }

            if (Time.time - _reconnectAttemptTime < ReconnectDelay)
                return;

            _isReconnecting = true;
            _reconnectAttempts++;
            _reconnectAttemptTime = Time.time;

            OnReconnecting?.Invoke();
            Debug.Log($"EpicNet: Auto-reconnecting to room '{_lastRoomName}' (attempt {_reconnectAttempts}/{MaxReconnectAttempts})...");
            JoinRoom(_lastRoomName, true);
        }

        private static void HandleReconnectFailure()
        {
            if (!_isReconnecting) return;

            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                _isReconnecting = false;
                _wasInRoom = false;
                Debug.LogError("EpicNet: Reconnection failed after max attempts");
                OnReconnectFailed?.Invoke();
            }
            else
            {
                // Will retry on next Update cycle
                _reconnectAttemptTime = Time.time;
                Debug.Log($"EpicNet: Reconnection attempt failed, will retry...");
            }
        }

        /// <summary>
        /// Cancel any ongoing reconnection attempts
        /// </summary>
        public static void CancelReconnect()
        {
            _isReconnecting = false;
            _reconnectAttempts = 0;
            Debug.Log("EpicNet: Reconnection cancelled");
        }

        #endregion
    }
}