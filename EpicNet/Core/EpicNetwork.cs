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
    /// Main network manager with host migration support
    /// </summary>
    public static class EpicNetwork
    {
        public static bool IsConnected => _isConnected;
        public static bool IsLoggedIn => _localUserId != null;
        public static bool InRoom => CurrentRoom != null;
        public static bool IsMasterClient => _isMasterClient;
        public static EpicPlayer LocalPlayer => _localPlayer;
        public static EpicPlayer MasterClient => _masterClient;
        public static EpicRoom CurrentRoom => _currentRoom;
        public static List<EpicPlayer> PlayerList => _playerList;
        public static string NickName { get; set; }
        public static float SendRate { get; set; } = 20f; // Hz
        public static int Ping { get; private set; }

        private static bool _isConnected;
        private static bool _isMasterClient;
        private static EpicPlayer _localPlayer;
        private static EpicPlayer _masterClient;
        private static EpicRoom _currentRoom;
        private static List<EpicPlayer> _playerList = new List<EpicPlayer>();

        private static LobbyInterface _lobbyInterface;
        private static P2PInterface _p2pInterface;
        private static ConnectInterface _connectInterface;
        private static ProductUserId _localUserId;
        private static string _currentLobbyId;

        private static readonly System.Random _rng = new System.Random();
        private static Dictionary<int, EpicView> _networkObjects = new Dictionary<int, EpicView>();
        private static int _viewIdCounter = 1000;
        private static Dictionary<int, Action<bool>> _ownershipRequestCallbacks = new Dictionary<int, Action<bool>>();
        private static int _ownershipRequestIdCounter = 0;

        // RPC System
        private static Dictionary<string, MethodInfo> _rpcMethods = new Dictionary<string, MethodInfo>();
        private static List<BufferedRPC> _bufferedRPCs = new List<BufferedRPC>();
        private static int _rpcIdCounter = 0;

        // Observable sync
        private static float _lastSyncTime;
        private static float _syncInterval => 1f / SendRate;

        // P2P Connection management
        private static ulong _p2pNotificationId;
        private static Dictionary<ProductUserId, float> _playerPingTimes = new Dictionary<ProductUserId, float>();

        // Room listing
        private static List<EpicRoomInfo> _cachedRoomList = new List<EpicRoomInfo>();

        // Late joiner management
        private static Dictionary<ProductUserId, bool> _pendingInitialState = new Dictionary<ProductUserId, bool>();
        private static float _lastInitialStateSendTime = 0f;

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
            InitialState = 10
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
            _localPlayer = null;
            _masterClient = null;
            _networkObjects.Clear();
            _bufferedRPCs.Clear();
            _ownershipRequestCallbacks.Clear();
            _pendingInitialState.Clear();

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
                var player = _playerList.Find(p => p.UserId == userId);

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

            var player = new EpicPlayer(userId, $"Player_{userId}");
            _playerList.Add(player);

            Debug.Log($"EpicNet: Player joined: {player.NickName}");
            OnPlayerEnteredRoom?.Invoke(player);

            if (IsMasterClient)
            {
                _pendingInitialState[userId] = true;
                // Immediately try to send (or add a small delay)
                SendInitialStateToPlayer(player);
            }
        }

        private static void HandlePlayerLeft(ProductUserId userId)
        {
            var player = _playerList.FirstOrDefault(p => p.UserId == userId);
            if (player == null) return;

            _playerList.Remove(player);
            Debug.Log($"EpicNet: Player left: {player.NickName}");

            // Clean up network objects owned by this player
            CleanupPlayerObjects(player);

            // Check if the master client left
            if (player.IsMasterClient)
            {
                PerformHostMigration();
            }

            OnPlayerLeftRoom?.Invoke(player);
        }

        private static void CleanupPlayerObjects(EpicPlayer player)
        {
            var objectsToDestroy = _networkObjects.Values
                .Where(v => v.Owner?.UserId == player.UserId)
                .ToList();

            foreach (var view in objectsToDestroy)
            {
                if (view != null && view.gameObject != null)
                {
                    UnityEngine.Object.Destroy(view.gameObject);
                }
            }
        }

        private static void PerformHostMigration()
        {
            Debug.Log("EpicNet: Performing host migration...");

            // Find new master client (player with lowest actor number)
            var newMaster = _playerList.OrderBy(p => p.ActorNumber).FirstOrDefault();

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
        }

        private static void TakeOwnershipOfOrphanedObjects()
        {
            foreach (var view in _networkObjects.Values)
            {
                if (view.Owner == null || !_playerList.Any(p => p.UserId == view.Owner.UserId))
                {
                    view.Owner = _localPlayer;
                    Debug.Log($"EpicNet: Took ownership of orphaned object {view.ViewID}");
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
                if (data.ResultCode == Result.Success && roomOptions.CustomRoomProperties != null)
                {
                    SetRoomProperties(roomOptions.CustomRoomProperties);
                }
            });
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
                    }
                }

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
                PlayerCount = (int)lobbyInfo.Value.AvailableSlots,
                MaxPlayers = (int)lobbyInfo.Value.MaxMembers,
                IsOpen = true
            };

            // Retrieve custom RoomName attribute
            var attributeOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = "RoomName" };
            if (lobbyDetails.CopyAttributeByKey(ref attributeOptions, out Attribute? attr) == Result.Success && attr.HasValue)
            {
                roomInfo.Name = attr.Value.Data.Value.Value.ToString();
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
            });
        }

        /// <summary>
        /// Join a specific room by name
        /// </summary>
        public static void JoinRoom(string roomName)
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
                    return;
                }

                var countOptions = new LobbySearchGetSearchResultCountOptions();
                uint resultCount = lobbySearch.GetSearchResultCount(ref countOptions);

                if (resultCount == 0)
                {
                    Debug.Log($"EpicNet: No room found with name '{roomName}'");
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
                    return;
                }

                Debug.Log($"EpicNet: Joining room '{roomName}'");
                JoinLobby(lobbyDetails);
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
        /// Instantiate a networked object
        /// </summary>
        public static GameObject Instantiate(string prefabName, Vector3 position, Quaternion rotation)
        {
            if (!InRoom)
            {
                Debug.LogError("EpicNet: Cannot instantiate while not in a room!");
                return null;
            }

            GameObject prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Debug.LogError($"EpicNet: Prefab '{prefabName}' not found in Resources!");
                return null;
            }

            int viewId = GenerateViewID();
            GameObject obj = UnityEngine.Object.Instantiate(prefab, position, rotation);

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
        /// Destroy a networked object
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

            UnityEngine.Object.Destroy(obj);
        }

        private static void SendInstantiateMessage(int viewId, string prefabName, Vector3 position, Quaternion rotation, EpicPlayer owner)
        {
            var data = SerializeInstantiateMessage(viewId, prefabName, position, rotation, owner);

            foreach (var player in _playerList)
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

            foreach (var player in _playerList)
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
                _bufferedRPCs.Add(new BufferedRPC
                {
                    ViewID = view.ViewID,
                    MethodName = methodName,
                    Parameters = parameters,
                    Sender = _localPlayer,
                    Timestamp = Time.timeAsDouble
                });
            }

            // Send to targets
            switch (target)
            {
                case RpcTarget.All:
                case RpcTarget.AllBuffered:
                case RpcTarget.AllViaServer:
                case RpcTarget.AllBufferedViaServer:
                    // Execute locally
                    ExecuteRPC(view.ViewID, methodName, parameters, _localPlayer);
                    // Send to others
                    SendRPCToOthers(rpcData);
                    break;

                case RpcTarget.Others:
                case RpcTarget.OthersBuffered:
                    SendRPCToOthers(rpcData);
                    break;

                case RpcTarget.MasterClient:
                    if (IsMasterClient)
                    {
                        ExecuteRPC(view.ViewID, methodName, parameters, _localPlayer);
                    }
                    else if (_masterClient != null)
                    {
                        SendP2PPacket(_masterClient.UserId, rpcData);
                    }
                    break;
            }
        }

        private static void SendRPCToOthers(byte[] rpcData)
        {
            foreach (var player in _playerList)
            {
                if (player.UserId != _localUserId)
                {
                    SendP2PPacket(player.UserId, rpcData);
                }
            }
        }

        private static void ExecuteRPC(int viewId, string methodName, object[] parameters, EpicPlayer sender)
        {
            if (!_networkObjects.TryGetValue(viewId, out EpicView view))
            {
                Debug.LogWarning($"EpicNet: Cannot execute RPC - View {viewId} not found");
                return;
            }

            var components = view.GetComponents<MonoBehaviour>();
            foreach (var component in components)
            {
                var methods = component.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (var method in methods)
                {
                    if (method.Name == methodName && method.GetCustomAttribute<EpicRPC>() != null)
                    {
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
                            Debug.LogError($"EpicNet: Error executing RPC {methodName}: {e.Message}");
                        }
                    }
                }
            }

            Debug.LogWarning($"EpicNet: RPC method {methodName} not found on view {viewId}");
        }

        /// <summary>
        /// Update loop - call this from MonoBehaviour Update
        /// </summary>
        public static void Update()
        {
            if (!InRoom) return;

            // Process P2P messages
            ProcessP2PMessages();

            // Try to send pending initial states (for late joiners)
            TrySendPendingInitialStates();

            // Sync observables at the configured rate
            if (Time.time - _lastSyncTime >= _syncInterval)
            {
                SyncObservables();
                _lastSyncTime = Time.time;
            }
        }

        private static void SyncObservables()
        {
            foreach (var view in _networkObjects.Values)
            {
                if (view == null || !view.IsMine) continue;

                var observables = view.GetComponents<IEpicObservable>();
                if (observables.Length == 0) continue;

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
                    observable.OnEpicSerializeView(stream, messageInfo);
                }

                // Send to other players
                if (stream.HasData())
                {
                    byte[] data = SerializeObservableData(view.ViewID, stream);

                    foreach (var player in _playerList)
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
            }
        }

        private static void HandleOwnershipTransferReceived(ProductUserId senderId, byte[] data)
        {
            if (data.Length < 9) return;

            int viewId = BitConverter.ToInt32(data, 1);
            int ownerIdLength = BitConverter.ToInt32(data, 5);
            string ownerIdString = System.Text.Encoding.UTF8.GetString(data, 9, ownerIdLength);

            if (_networkObjects.TryGetValue(viewId, out EpicView view))
            {
                var newOwner = _playerList.Find(p => p.UserId.ToString() == ownerIdString);
                if (newOwner != null)
                {
                    view.Owner = newOwner;
                    Debug.Log($"EpicNet: View {viewId} ownership transferred to {newOwner.NickName}");
                }
            }
        }

        private static void HandleOwnershipRequestReceived(ProductUserId senderId, byte[] data)
        {
            if (data.Length < 9) return;

            int viewId = BitConverter.ToInt32(data, 1);
            int requestId = BitConverter.ToInt32(data, 5);

            if (_networkObjects.TryGetValue(viewId, out EpicView view))
            {
                var requester = _playerList.Find(p => p.UserId == senderId);
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

            var sender = _playerList.Find(p => p.UserId == senderId);
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

            if (_networkObjects.TryGetValue(viewId, out EpicView view))
            {
                var stream = new EpicStream(false);
                DeserializeObservableStream(data, 5, stream);

                var messageInfo = new EpicMessageInfo
                {
                    Sender = _playerList.Find(p => p.UserId == senderId),
                    Timestamp = Time.timeAsDouble
                };

                var observables = view.GetComponents<IEpicObservable>();
                foreach (var observable in observables)
                {
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

            // Instantiate the object
            GameObject prefab = Resources.Load<GameObject>(prefabName);
            if (prefab != null)
            {
                GameObject obj = UnityEngine.Object.Instantiate(prefab, position, rotation);
                var view = obj.GetComponent<EpicView>();

                if (view != null)
                {
                    view.ViewID = viewId;
                    view.Owner = _playerList.Find(p => p.UserId.ToString() == ownerIdString);
                    view.PrefabName = prefabName;
                    RegisterNetworkObject(view);
                }
            }
        }

        private static void HandleDestroyReceived(ProductUserId senderId, byte[] data)
        {
            if (data.Length < 5) return;

            int viewId = BitConverter.ToInt32(data, 1);

            if (_networkObjects.TryGetValue(viewId, out EpicView view))
            {
                UnregisterNetworkObject(viewId);
                if (view != null && view.gameObject != null)
                {
                    UnityEngine.Object.Destroy(view.gameObject);
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
            }
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

                // Instantiate
                GameObject prefab = Resources.Load<GameObject>(prefabName);
                if (prefab != null)
                {
                    GameObject obj = UnityEngine.Object.Instantiate(prefab, position, rotation);
                    var view = obj.GetComponent<EpicView>();

                    if (view != null)
                    {
                        view.ViewID = viewId;
                        view.Owner = _playerList.Find(p => p.UserId.ToString() == ownerIdString);
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

                var sender = _playerList.Find(p => p.UserId == senderId);
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
            if (!InRoom) return;

            byte[] data = SerializeOwnershipTransfer(viewId, newOwner);

            foreach (var player in _playerList)
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
            if (!InRoom) return;

            int requestId = _ownershipRequestIdCounter++;
            _ownershipRequestCallbacks[requestId] = callback;

            byte[] data = SerializeOwnershipRequest(viewId, requestId);
            SendP2PPacket(owner.UserId, data);

            // Timeout after 5 seconds
            DelayedCallback(5f, () =>
            {
                if (_ownershipRequestCallbacks.ContainsKey(requestId))
                {
                    _ownershipRequestCallbacks[requestId]?.Invoke(false);
                    _ownershipRequestCallbacks.Remove(requestId);
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
            var stream = new System.IO.MemoryStream();
            var writer = new System.IO.BinaryWriter(stream);

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
            byte typeId = data[offset++];

            switch (typeId)
            {
                case 0: // null
                    return null;

                case 1: // int
                    int intVal = BitConverter.ToInt32(data, offset);
                    offset += 4;
                    return intVal;

                case 2: // float
                    float floatVal = BitConverter.ToSingle(data, offset);
                    offset += 4;
                    return floatVal;

                case 3: // string
                    int strLength = BitConverter.ToInt32(data, offset);
                    offset += 4;
                    string strVal = System.Text.Encoding.UTF8.GetString(data, offset, strLength);
                    offset += strLength;
                    return strVal;

                case 4: // bool
                    bool boolVal = data[offset++] == 1;
                    return boolVal;

                case 5: // Vector3
                    Vector3 vec = new Vector3(
                        BitConverter.ToSingle(data, offset),
                        BitConverter.ToSingle(data, offset + 4),
                        BitConverter.ToSingle(data, offset + 8)
                    );
                    offset += 12;
                    return vec;

                case 6: // Quaternion
                    Quaternion quat = new Quaternion(
                        BitConverter.ToSingle(data, offset),
                        BitConverter.ToSingle(data, offset + 4),
                        BitConverter.ToSingle(data, offset + 8),
                        BitConverter.ToSingle(data, offset + 12)
                    );
                    offset += 16;
                    return quat;

                case 7: // byte[]
                    int byteLength = BitConverter.ToInt32(data, offset);
                    offset += 4;
                    byte[] byteArray = new byte[byteLength];
                    Array.Copy(data, offset, byteArray, 0, byteLength);
                    offset += byteLength;
                    return byteArray;

                default:
                    return null;
            }
        }

        private static byte[] SerializeObservableData(int viewId, EpicStream stream)
        {
            var memStream = new System.IO.MemoryStream();
            var writer = new System.IO.BinaryWriter(memStream);

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
            var stream = new System.IO.MemoryStream();
            var writer = new System.IO.BinaryWriter(stream);

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

        private static byte[] SerializeDestroyMessage(int viewId)
        {
            byte[] data = new byte[5];
            data[0] = (byte)PacketType.DestroyObject;
            BitConverter.GetBytes(viewId).CopyTo(data, 1);
            return data;
        }

        private static byte[] SerializeInitialState()
        {
            var stream = new System.IO.MemoryStream();
            var writer = new System.IO.BinaryWriter(stream);

            writer.Write((byte)PacketType.InitialState);

            // Serialize all network objects
            writer.Write(_networkObjects.Count);

            foreach (var kvp in _networkObjects)
            {
                var view = kvp.Value;
                if (view == null) continue;

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

            // Serialize buffered RPCs
            writer.Write(_bufferedRPCs.Count);

            foreach (var rpc in _bufferedRPCs)
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

        #endregion

        #region Internal Helper Methods

        private static void SendP2PPacket(ProductUserId targetUserId, byte[] data)
        {
            var sendOptions = new SendPacketOptions
            {
                LocalUserId = _localUserId,
                RemoteUserId = targetUserId,
                SocketId = new SocketId { SocketName = "EpicNet" },
                Channel = 0,
                Data = new ArraySegment<byte>(data),
                AllowDelayedDelivery = true,
                Reliability = PacketReliability.ReliableOrdered
            };

            var result = _p2pInterface.SendPacket(ref sendOptions);
            if (result != Result.Success)
            {
                Debug.LogWarning($"EpicNet: Failed to send P2P packet: {result}");
            }
        }

        private static void DelayedCallback(float delay, Action callback)
        {
            var go = new GameObject("DelayedCallback");
            var mb = go.AddComponent<DelayedCallbackHelper>();
            mb.Initialize(delay, callback);
        }

        private class DelayedCallbackHelper : MonoBehaviour
        {
            private float _delay;
            private Action _callback;
            private float _startTime;

            public void Initialize(float delay, Action callback)
            {
                _delay = delay;
                _callback = callback;
                _startTime = Time.time;
            }

            private void Update()
            {
                if (Time.time - _startTime >= _delay)
                {
                    _callback?.Invoke();
                    Destroy(gameObject);
                }
            }
        }

        private static void OnLobbyCreated(ref CreateLobbyCallbackInfo data, string roomName)
        {
            if (data.ResultCode == Result.Success)
            {
                _currentLobbyId = data.LobbyId;
                _currentRoom = new EpicRoom(data.LobbyId, roomName);
                _isMasterClient = true;

                _localPlayer.IsMasterClient = true;
                _masterClient = _localPlayer;
                _playerList.Add(_localPlayer);

                // Set room name as attribute
                SetRoomProperties(new Dictionary<string, object> { { "RoomName", roomName } });

                Debug.Log($"EpicNet: Room created: {roomName}");
                OnJoinedRoom?.Invoke();
            }
            else
            {
                Debug.LogError($"EpicNet: Failed to create room: {data.ResultCode}");
            }
        }

        private static void JoinLobby(LobbyDetails lobbyDetails)
        {
            var infoOptions = new LobbyDetailsCopyInfoOptions();
            var result = lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? lobbyInfo);

            if (result != Result.Success || !lobbyInfo.HasValue) return;

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
                _currentRoom = new EpicRoom(data.LobbyId, "Room");
                _isMasterClient = false;

                _localPlayer.IsMasterClient = false;
                _playerList.Add(_localPlayer);

                // Copy lobby details to fetch members
                var copyOptions = new CopyLobbyDetailsHandleByInviteIdOptions
                {
                    InviteId = data.LobbyId
                };

                var result = _lobbyInterface.CopyLobbyDetailsHandleByInviteId(ref copyOptions, out LobbyDetails lobbyDetails);
                if (result == Result.Success && lobbyDetails != null)
                {
                    FetchLobbyMembers(lobbyDetails);
                }

                Debug.Log($"EpicNet: Joined room: {data.LobbyId}");
                OnJoinedRoom?.Invoke();
            }
            else
            {
                Debug.LogError($"EpicNet: Failed to join room: {data.ResultCode}");
            }
        }

        private static void FetchLobbyMembers(LobbyDetails lobbyDetails)
        {
            if (lobbyDetails == null)
            {
                Debug.LogError("EpicNet: Cannot fetch members - no lobby details");
                return;
            }

            var countOptions = new LobbyDetailsGetMemberCountOptions();
            uint memberCount = lobbyDetails.GetMemberCount(ref countOptions);

            for (uint i = 0; i < memberCount; i++)
            {
                var memberOptions = new LobbyDetailsGetMemberByIndexOptions
                {
                    MemberIndex = i
                };

                ProductUserId memberId = lobbyDetails.GetMemberByIndex(ref memberOptions);

                // Skip ourselves
                if (memberId == _localUserId) continue;

                // Add other players
                var player = new EpicPlayer(memberId, $"Player_{memberId}");

                // Check if this member is the lobby owner
                var infoOptions = new LobbyDetailsCopyInfoOptions();
                var infoResult = lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? lobbyInfo);

                if (infoResult == Result.Success && lobbyInfo.HasValue)
                {
                    ProductUserId ownerId = lobbyInfo.Value.LobbyOwnerUserId;

                    if (memberId == ownerId)
                    {
                        player.IsMasterClient = true;
                        _masterClient = player;
                        Debug.Log($"EpicNet: Found master client: {player.NickName}");
                    }
                }

                _playerList.Add(player);
            }

            // If we didn't find a master, set the first player as master
            if (_masterClient == null && _playerList.Count > 0)
            {
                var firstPlayer = _playerList[0];
                firstPlayer.IsMasterClient = true;
                _masterClient = firstPlayer;
            }
        }

        private static void OnLobbyLeft(ref LeaveLobbyCallbackInfo data)
        {
            if (data.ResultCode == Result.Success)
            {
                _currentRoom = null;
                _currentLobbyId = null;
                _isMasterClient = false;
                _masterClient = null;
                _playerList.Clear();
                _networkObjects.Clear();
                _bufferedRPCs.Clear();
                _pendingInitialState.Clear(); // ADD THIS LINE

                Debug.Log("EpicNet: Left room");
                OnLeftRoom?.Invoke();
            }
        }

        private static int GenerateViewID() => _viewIdCounter++;

        private static void RegisterNetworkObject(EpicView view)
        {
            _networkObjects[view.ViewID] = view;
        }

        public static void UnregisterNetworkObject(int viewId)
        {
            _networkObjects.Remove(viewId);
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

        #endregion
    }
}