using EpicNet;
using UnityEngine;

public class EpicTestingGUI : EpicMonoBehaviourCallbacks
{
    private string _roomName = "TestRoom";
    private string _playerName = "Player";
    private Vector2 _scrollPosition;
    private string _logMessages = "";
    private bool _showDebugLog = true;
    private int _maxPlayers = 10;
    private string _displayName = "";
    private bool _isLoggingIn = false;

    private void Start()
    {
        _playerName = "Player_" + Random.Range(1000, 9999);
        _displayName = _playerName;
        AddLog("EpicNet Testing GUI initialized");
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 520, Screen.height - 20));

        _scrollPosition = GUILayout.BeginScrollView(
            _scrollPosition,
            false,
            true,
            GUILayout.Width(520),
            GUILayout.Height(Screen.height - 20)
        );

        GUILayout.BeginVertical("box");

        // Title
        GUILayout.Label("<size=20><b>EpicNet Testing GUI</b></size>");
        GUILayout.Space(10);

        // EOS Status
        DrawEOSStatus();
        GUILayout.Space(10);

        // Login Section (if not logged in)
        if (!EpicNetwork.IsLoggedIn)
        {
            DrawLoginSection();
            GUILayout.Space(10);
        }

        // Connection Status
        if (EpicNetwork.IsLoggedIn)
        {
            DrawConnectionStatus();
            GUILayout.Space(10);
        }

        // Player Settings
        if (EpicNetwork.IsLoggedIn && !EpicNetwork.IsConnected)
        {
            DrawPlayerSettings();
            GUILayout.Space(10);
        }

        // Connection Controls
        if (EpicNetwork.IsLoggedIn)
        {
            DrawConnectionControls();
            GUILayout.Space(10);
        }

        // Room Controls
        if (EpicNetwork.IsConnected)
        {
            DrawRoomControls();
            GUILayout.Space(10);
        }

        // Room Info
        if (EpicNetwork.InRoom)
        {
            DrawRoomInfo();
            GUILayout.Space(10);
        }

        // Debug Log
        DrawDebugLog();

        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawEOSStatus()
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("<b>EOS Status</b>");

        string loginStatus = EpicNetwork.IsLoggedIn ? "<color=green>Logged In</color>" : "<color=yellow>Not Logged In</color>";
        GUILayout.Label($"Login: {loginStatus}");

        string connStatus = EpicNetwork.IsConnected ? "<color=green>Connected</color>" : "<color=red>Disconnected</color>";
        GUILayout.Label($"Connected: {connStatus}");

        GUILayout.EndVertical();
    }

    private void DrawLoginSection()
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("<b>Device ID Login</b>");

        if (_isLoggingIn)
        {
            GUILayout.Label("<color=yellow>Logging in...</color>");
            GUILayout.EndVertical();
            return;
        }

        GUILayout.Label("<i>Quick login using device ID (no account required)</i>");
        GUILayout.Space(5);

        // Display Name Input
        GUILayout.BeginHorizontal();
        GUILayout.Label("Display Name:", GUILayout.Width(100));
        _displayName = GUILayout.TextField(_displayName, GUILayout.Width(300));
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        // Login Button
        if (GUILayout.Button("Login with Device ID", GUILayout.Height(40)))
        {
            LoginWithDeviceId();
        }

        GUILayout.EndVertical();
    }

    private void DrawConnectionStatus()
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("<b>Network Status</b>");

        string connStatus = EpicNetwork.IsConnected ? "<color=green>Connected</color>" : "<color=red>Disconnected</color>";
        GUILayout.Label($"EpicNet: {connStatus}");

        if (EpicNetwork.IsConnected)
        {
            GUILayout.Label($"Player: {EpicNetwork.LocalPlayer?.NickName ?? "Unknown"}");

            string roomStatus = EpicNetwork.InRoom ? "<color=green>In Room</color>" : "<color=yellow>In Lobby</color>";
            GUILayout.Label($"Room: {roomStatus}");

            if (EpicNetwork.InRoom)
            {
                string masterStatus = EpicNetwork.IsMasterClient ? "<color=blue>Master Client</color>" : "Client";
                GUILayout.Label($"Role: {masterStatus}");
            }
        }

        GUILayout.EndVertical();
    }

    private void DrawPlayerSettings()
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("<b>Player Settings</b>");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Name:", GUILayout.Width(80));
        _playerName = GUILayout.TextField(_playerName, GUILayout.Width(200));
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    private void DrawConnectionControls()
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("<b>Connection Controls</b>");

        GUILayout.BeginHorizontal();

        if (!EpicNetwork.IsConnected)
        {
            if (GUILayout.Button("Connect to EpicNet", GUILayout.Height(40)))
            {
                EpicNetwork.NickName = _playerName;
                EpicNetwork.ConnectUsingSettings();
                AddLog($"Connecting EpicNet as {_playerName}...");
            }
        }
        else
        {
            if (GUILayout.Button("Disconnect from EpicNet", GUILayout.Height(40)))
            {
                if (EpicNetwork.InRoom)
                {
                    EpicNetwork.LeaveRoom();
                }
                AddLog("Disconnected from EpicNet");
            }
        }

        if (GUILayout.Button("Logout from EOS", GUILayout.Height(40), GUILayout.Width(150)))
        {
            EpicNetwork.Logout();
            AddLog("Logged out from EOS");
        }

        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    private void DrawRoomControls()
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("<b>Room Controls</b>");

        if (!EpicNetwork.InRoom)
        {
            // Room Name Input
            GUILayout.BeginHorizontal();
            GUILayout.Label("Room:", GUILayout.Width(80));
            _roomName = GUILayout.TextField(_roomName, GUILayout.Width(200));
            GUILayout.EndHorizontal();

            // Max Players Slider
            GUILayout.BeginHorizontal();
            GUILayout.Label("Max Players:", GUILayout.Width(80));
            _maxPlayers = (int)GUILayout.HorizontalSlider(_maxPlayers, 2, 20, GUILayout.Width(150));
            GUILayout.Label(_maxPlayers.ToString(), GUILayout.Width(50));
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            // Room Actions
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Create Room", GUILayout.Height(35)))
            {
                var options = new EpicRoomOptions
                {
                    MaxPlayers = _maxPlayers,
                    IsVisible = true,
                    IsOpen = true
                };
                EpicNetwork.CreateRoom(_roomName, options);
                AddLog($"Creating room: {_roomName} (Max: {_maxPlayers})");
            }

            if (GUILayout.Button("Join Room", GUILayout.Height(35)))
            {
                EpicNetwork.JoinRoom(_roomName);
                AddLog($"Joining room: {_roomName}");
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Join Random", GUILayout.Height(35)))
            {
                EpicNetwork.JoinRandomRoom();
                AddLog("Searching for random room...");
            }

            if (GUILayout.Button("Refresh Rooms", GUILayout.Height(35)))
            {
                AddLog("Refreshing room list...");
                // TODO: Implement room list refresh
            }

            GUILayout.EndHorizontal();
        }
        else
        {
            // In Room Actions
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Leave Room", GUILayout.Height(40), GUILayout.Width(150)))
            {
                EpicNetwork.LeaveRoom();
                AddLog("Leaving room...");
            }

            if (EpicNetwork.IsMasterClient)
            {
                if (GUILayout.Button("Close Room", GUILayout.Height(40)))
                {
                    if (EpicNetwork.CurrentRoom != null)
                    {
                        EpicNetwork.CurrentRoom.IsOpen = false;
                        AddLog("Room closed to new players");
                    }
                }
            }

            GUILayout.EndHorizontal();

            // Test Actions
            GUILayout.Space(5);
            GUILayout.Label("<b>Test Actions</b>");

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Spawn Test Object", GUILayout.Height(30)))
            {
                Vector3 randomPos = new Vector3(
                    Random.Range(-5f, 5f),
                    Random.Range(0f, 2f),
                    Random.Range(-5f, 5f)
                );

                EpicNetwork.Instantiate("TestObjectPrefab", randomPos, Quaternion.identity);

                AddLog($"Spawned object at {randomPos}");
            }

            if (GUILayout.Button("Test RPC", GUILayout.Height(30)))
            {
                AddLog("Sending test RPC to all players");
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();
    }

    private void DrawRoomInfo()
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("<b>Room Information</b>");

        if (EpicNetwork.CurrentRoom != null)
        {
            GUILayout.Label($"Name: {EpicNetwork.CurrentRoom.Name}");
            GUILayout.Label($"Players: {EpicNetwork.CurrentRoom.PlayerCount}/{EpicNetwork.CurrentRoom.MaxPlayers}");
            GUILayout.Label($"Open: {EpicNetwork.CurrentRoom.IsOpen}");
            GUILayout.Label($"Visible: {EpicNetwork.CurrentRoom.IsVisible}");

            GUILayout.Space(5);
            GUILayout.Label("<b>Players in Room:</b>");

            if (EpicNetwork.PlayerList != null && EpicNetwork.PlayerList.Count > 0)
            {
                foreach (var player in EpicNetwork.PlayerList)
                {
                    string prefix = player.IsLocal ? "[YOU] " : "";
                    string master = player.IsMasterClient ? " [MASTER]" : "";
                    string color = player.IsLocal ? "yellow" : "white";

                    GUILayout.Label($"<color={color}>{prefix}{player.NickName}{master}</color>");
                }
            }
            else
            {
                GUILayout.Label("<i>No players</i>");
            }
        }

        GUILayout.EndVertical();
    }

    private void DrawDebugLog()
    {
        GUILayout.BeginVertical("box");

        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>Debug Log</b>");
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Clear", GUILayout.Width(60)))
        {
            _logMessages = "";
        }

        _showDebugLog = GUILayout.Toggle(_showDebugLog, "Show", GUILayout.Width(60));
        GUILayout.EndHorizontal();

        if (_showDebugLog)
        {
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
            GUILayout.Label(_logMessages);
            GUILayout.EndScrollView();
        }

        GUILayout.EndVertical();
    }

    private void LoginWithDeviceId()
    {
        if (string.IsNullOrEmpty(_displayName))
        {
            AddLog("<color=red>[ERROR]</color> Display name cannot be empty!");
            return;
        }

        _isLoggingIn = true;
        AddLog($"<color=cyan>[LOGIN]</color> Logging in with Device ID as '{_displayName}'...");

        EpicNetwork.LoginWithDeviceId(_displayName, (success, message) =>
        {
            _isLoggingIn = false;

            if (success)
            {
                AddLog("<color=green>[SUCCESS]</color> Login successful!");
                AddLog("<color=green>[READY]</color> Ready to connect to EpicNet!");
            }
            else
            {
                AddLog($"<color=red>[ERROR]</color> Login failed: {message}");
            }
        });
    }

    // Callback Overrides
    public override void OnConnectedToMaster()
    {
        AddLog("<color=green>[SUCCESS]</color> Connected to EpicNet");
    }

    public override void OnJoinedRoom()
    {
        AddLog($"<color=green>[SUCCESS]</color> Joined room: {EpicNetwork.CurrentRoom?.Name}");
        AddLog($"Players in room: {EpicNetwork.PlayerList?.Count ?? 0}");
    }

    public override void OnLeftRoom()
    {
        AddLog("<color=yellow>[INFO]</color> Left the room");
    }

    public override void OnPlayerEnteredRoom(EpicPlayer newPlayer)
    {
        AddLog($"<color=cyan>[PLAYER JOIN]</color> {newPlayer.NickName} entered the room");
    }

    public override void OnPlayerLeftRoom(EpicPlayer otherPlayer)
    {
        AddLog($"<color=orange>[PLAYER LEFT]</color> {otherPlayer.NickName} left the room");
    }

    public override void OnMasterClientSwitched(EpicPlayer newMaster)
    {
        string status = EpicNetwork.IsMasterClient ? "You are now" : "Someone else is now";
        AddLog($"<color=blue>[MASTER SWITCH]</color> {status} the master client");
    }

    private void AddLog(string message)
    {
        string timestamp = System.DateTime.Now.ToString("HH:mm:ss");
        _logMessages += $"[{timestamp}] {message}\n";

        // Keep only last 100 lines
        string[] lines = _logMessages.Split('\n');
        if (lines.Length > 100)
        {
            _logMessages = string.Join("\n", lines, lines.Length - 100, 100);
        }

        // Auto-scroll to bottom
        _scrollPosition.y = float.MaxValue;

        Debug.Log($"EpicNet: {message}");
    }
}