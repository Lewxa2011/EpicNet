using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

        // Events
        public static event Action OnConnectedToMaster;
        public static event Action OnJoinedRoom;
        public static event Action OnLeftRoom;
        public static event Action<EpicPlayer> OnPlayerEnteredRoom;
        public static event Action<EpicPlayer> OnPlayerLeftRoom;
        public static event Action<EpicPlayer> OnMasterClientSwitched;
        public static event Action OnLoginSuccess;
        public static event Action<Result> OnLoginFailed;

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

            _localUserId = null;
            _isConnected = false;
            _localPlayer = null;
            _masterClient = null;
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

        private static void HandlePlayerJoined(ProductUserId userId)
        {
            // Don't add ourselves again
            if (userId == _localUserId) return;

            var player = new EpicPlayer(userId, $"Player_{userId}");
            _playerList.Add(player);

            Debug.Log($"EpicNet: Player joined: {player.NickName}");
            OnPlayerEnteredRoom?.Invoke(player);
        }

        private static void HandlePlayerLeft(ProductUserId userId)
        {
            var player = _playerList.FirstOrDefault(p => p.UserId == userId);
            if (player == null) return;

            _playerList.Remove(player);
            Debug.Log($"EpicNet: Player left: {player.NickName}");

            // Check if the master client left
            if (player.IsMasterClient)
            {
                PerformHostMigration();
            }

            OnPlayerLeftRoom?.Invoke(player);
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
                }
                else
                {
                    Debug.Log($"EpicNet: New master client is {newMaster.NickName}");
                }

                OnMasterClientSwitched?.Invoke(_masterClient);
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
                OnLobbyCreated(ref data);

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

            GameObject obj = GameObject.Instantiate(prefab, position, rotation);

            var view = obj.GetComponent<EpicView>();
            if (view != null)
            {
                view.ViewID = GenerateViewID();
                view.Owner = _localPlayer;
                RegisterNetworkObject(view);
            }

            return obj;
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

        private static void DelayedCallback(float delay, Action callback)
        {
            // Simple delayed callback - in production use a coroutine manager
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

        /// <summary>
        /// Process incoming P2P packets (call this from Update or with EOS tick)
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
            }
        }

        private static void HandleOwnershipTransferReceived(ProductUserId senderId, byte[] data)
        {
            // Deserialize: [PacketType(1)][ViewID(4)][OwnerUserIdLength(4)][OwnerUserId]
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
            // Deserialize: [PacketType(1)][ViewID(4)][RequestID(4)]
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
                        // Send response back
                        byte[] response = SerializeOwnershipResponse(requestId, approved);
                        SendP2PPacket(senderId, response);
                    });
                }
            }
        }

        private static void HandleOwnershipResponseReceived(ProductUserId senderId, byte[] data)
        {
            // Deserialize: [PacketType(1)][RequestID(4)][Approved(1)]
            if (data.Length < 6) return;

            int requestId = BitConverter.ToInt32(data, 1);
            bool approved = data[5] == 1;

            if (_ownershipRequestCallbacks.TryGetValue(requestId, out Action<bool> callback))
            {
                callback?.Invoke(approved);
                _ownershipRequestCallbacks.Remove(requestId);
            }
        }

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

        private enum PacketType : byte
        {
            OwnershipTransfer = 1,
            OwnershipRequest = 2,
            OwnershipResponse = 3
        }

        #region Internal Methods

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

        private static void OnLobbyCreated(ref CreateLobbyCallbackInfo data)
        {
            if (data.ResultCode == Result.Success)
            {
                _currentLobbyId = data.LobbyId;
                _currentRoom = new EpicRoom(data.LobbyId, NickName ?? "Room");
                _isMasterClient = true;

                _localPlayer.IsMasterClient = true;
                _masterClient = _localPlayer;
                _playerList.Add(_localPlayer);

                Debug.Log($"EpicNet: Room created: {data.LobbyId}");
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

        #endregion
    }
}