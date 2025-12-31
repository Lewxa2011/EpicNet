using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Main network manager - equivalent to PhotonNetwork in PUN2
    /// </summary>
    public static class EpicNetwork
    {
        public static bool IsConnected => _isConnected;
        public static bool IsLoggedIn => _localUserId != null;
        public static bool InRoom => CurrentRoom != null;
        public static bool IsMasterClient => _isMasterClient;
        public static EpicPlayer LocalPlayer => _localPlayer;
        public static EpicRoom CurrentRoom => _currentRoom;
        public static List<EpicPlayer> PlayerList => _playerList;
        public static string NickName { get; set; }

        private static bool _isConnected;
        private static bool _isMasterClient;
        private static EpicPlayer _localPlayer;
        private static EpicRoom _currentRoom;
        private static List<EpicPlayer> _playerList = new List<EpicPlayer>();

        private static LobbyInterface _lobbyInterface;
        private static P2PInterface _p2pInterface;
        private static ConnectInterface _connectInterface;
        private static ProductUserId _localUserId;

        public static event Action OnConnectedToMaster;
        public static event Action OnJoinedRoom;
        public static event Action OnLeftRoom;
        public static event Action<EpicPlayer> OnPlayerEnteredRoom;
        public static event Action<EpicPlayer> OnPlayerLeftRoom;
        public static event Action OnMasterClientSwitched;
        public static event Action OnLoginSuccess;
        public static event Action<Result> OnLoginFailed;

        /// <summary>
        /// Login with Device ID - simple and automatic
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
            Debug.Log("EpicNet: Logged out");
        }

        /// <summary>
        /// Connect to EOS services (must be logged in first)
        /// </summary>
        public static void ConnectUsingSettings()
        {
            if (_isConnected) return;

            if (_localUserId == null)
            {
                Debug.LogError("EpicNet: Not logged in! Call LoginWithDeviceId first!");
                return;
            }

            // Initialize EOS SDK (assumes EOSManager is already initialized)
            var eosManager = EOSManager.Instance;
            if (eosManager == null)
            {
                Debug.LogError("EpicNet: EOSManager not found!");
                return;
            }

            _lobbyInterface = eosManager.GetEOSPlatformInterface().GetLobbyInterface();
            _p2pInterface = eosManager.GetEOSPlatformInterface().GetP2PInterface();

            _isConnected = true;
            _localPlayer = new EpicPlayer(_localUserId, NickName ?? "Player");

            Debug.Log("EpicNet: Connected to EOS");
            OnConnectedToMaster?.Invoke();
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

            _lobbyInterface.CreateLobby(ref createOptions, null, OnLobbyCreated);
        }

        /// <summary>
        /// Join a random room
        /// </summary>
        public static void JoinRandomRoom(Dictionary<string, object> expectedRoomProperties = null)
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
        
                // Join first matching lobby
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
                LobbyId = _currentRoom.Name
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

            // Load prefab and instantiate
            GameObject prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Debug.LogError($"EpicNet: Prefab '{prefabName}' not found in Resources!");
                return null;
            }

            GameObject obj = GameObject.Instantiate(prefab, position, rotation);

            // Register with network
            var view = obj.GetComponent<EpicView>();
            if (view != null)
            {
                view.ViewID = GenerateViewID();
                RegisterNetworkObject(view);
            }

            return obj;
        }

        /// <summary>
        /// Send RPC to other players
        /// </summary>
        public static void RPC(string methodName, RpcTarget target, params object[] parameters)
        {
            // Serialize and send RPC data via P2P
            byte[] data = SerializeRPC(methodName, parameters);

            foreach (var player in _playerList)
            {
                if (ShouldSendToPlayer(player, target))
                {
                    SendP2PPacket(player.UserId, data);
                }
            }
        }

        #region Internal Methods

        private static void OnLobbyCreated(ref CreateLobbyCallbackInfo data)
        {
            if (data.ResultCode == Result.Success)
            {
                _currentRoom = new EpicRoom(data.LobbyId, NickName ?? "Room");
                _isMasterClient = true;
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
                _currentRoom = new EpicRoom(data.LobbyId, "Room");
                _isMasterClient = false;
                _playerList.Add(_localPlayer);

                Debug.Log($"EpicNet: Joined room: {data.LobbyId}");
                OnJoinedRoom?.Invoke();
            }
            else
            {
                Debug.LogError($"EpicNet: Failed to join room: {data.ResultCode}");
            }
        }

        private static void OnLobbyLeft(ref LeaveLobbyCallbackInfo data)
        {
            if (data.ResultCode == Result.Success)
            {
                _currentRoom = null;
                _isMasterClient = false;
                _playerList.Clear();

                Debug.Log("EpicNet: Left room");
                OnLeftRoom?.Invoke();
            }
        }

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

            _p2pInterface.SendPacket(ref sendOptions);
        }

        private static int _viewIdCounter = 1000;
        private static int GenerateViewID() => _viewIdCounter++;

        private static Dictionary<int, EpicView> _networkObjects = new Dictionary<int, EpicView>();

        private static void RegisterNetworkObject(EpicView view)
        {
            _networkObjects[view.ViewID] = view;
        }

        private static byte[] SerializeRPC(string methodName, object[] parameters)
        {
            // Simple serialization - you'd want to use a proper serializer
            return System.Text.Encoding.UTF8.GetBytes($"RPC:{methodName}");
        }

        private static bool ShouldSendToPlayer(EpicPlayer player, RpcTarget target)
        {
            if (player.UserId == _localUserId) return false;

            switch (target)
            {
                case RpcTarget.All: return true;
                case RpcTarget.Others: return true;
                case RpcTarget.MasterClient: return player.IsMasterClient;
                default: return false;
            }
        }

        #endregion
    }

}
