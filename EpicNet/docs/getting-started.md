# Getting Started with EpicNet

This guide walks you through setting up EpicNet in your Unity project.

## Prerequisites

1. **Unity 2021.3+** - EpicNet is tested with Unity 2021.3 LTS and later
2. **EOS Plugin for Unity** - Install the [PlayEveryWare EOS Plugin](https://github.com/PlayEveryWare/eos_plugin_for_unity)
3. **EOS Developer Account** - Create a product at [dev.epicgames.com](https://dev.epicgames.com)
4. **Concentus Unity** (for voice chat) - Install [com.adrenak.concentus-unity](https://github.com/adrenak/concentus-unity) via UPM

## Installation

1. Copy the `EpicNet` folder into your project's `Assets` folder
2. Ensure the EOS Plugin is configured with your product credentials
3. Add your networked prefabs to a `Resources` folder

## Project Setup

### 1. Configure EOS

In the EOS Plugin settings (`Tools > EpicOnlineServices > Configuration`), configure:
- Product ID
- Sandbox ID
- Deployment ID
- Client Credentials

### 2. Create a Network Manager

Create a GameObject with a script that inherits from `EpicMonoBehaviourCallbacks`:

```csharp
using EpicNet;
using UnityEngine;

public class NetworkManager : EpicMonoBehaviourCallbacks
{
    public static NetworkManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Set your player name
        EpicNetwork.NickName = "Player_" + Random.Range(1000, 9999);

        // Login with device ID (automatic on mobile)
        EpicNetwork.LoginWithDeviceId(EpicNetwork.NickName, OnLoginComplete);
    }

    void Update()
    {
        // IMPORTANT: Call this every frame
        EpicNetwork.Update();
    }

    void OnLoginComplete(bool success, string message)
    {
        if (success)
        {
            Debug.Log("Login successful!");
            EpicNetwork.ConnectUsingSettings();
        }
        else
        {
            Debug.LogError($"Login failed: {message}");
        }
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to EOS services");
        // Now you can create/join rooms
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined room: {EpicNetwork.CurrentRoom.Name}");
        Debug.Log($"Players: {EpicNetwork.CurrentRoom.PlayerCount}");
    }

    public override void OnPlayerEnteredRoom(EpicPlayer player)
    {
        Debug.Log($"{player.NickName} joined the room");
    }

    public override void OnPlayerLeftRoom(EpicPlayer player)
    {
        Debug.Log($"{player.NickName} left the room");
    }
}
```

### 3. Create a Networked Prefab

1. Create a prefab for your player/object
2. Add an `EpicView` component
3. Add `EpicTransformView` for automatic position sync
4. Place the prefab in a `Resources` folder

```
Assets/
├── Resources/
│   └── PlayerPrefab.prefab  ← Must be here!
└── EpicNet/
    └── ...
```

### 4. Spawn Network Objects

```csharp
// Spawn a network object (owned by local player)
GameObject player = EpicNetwork.Instantiate("PlayerPrefab", spawnPosition, Quaternion.identity);

// Destroy a network object
EpicNetwork.Destroy(player);
```

## Basic Workflow

```
1. LoginWithDeviceId()     → Authenticate with EOS
         ↓
2. ConnectUsingSettings()  → Initialize P2P networking
         ↓
3. OnConnectedToMaster()   → Ready to join/create rooms
         ↓
4. CreateRoom() / JoinRoom() → Enter a game session
         ↓
5. OnJoinedRoom()          → Spawn objects, start gameplay
         ↓
6. LeaveRoom()             → Exit when done
```

## Room Operations

### Create a Room

```csharp
var options = new EpicRoomOptions
{
    MaxPlayers = 4,
    IsVisible = true,
    IsOpen = true,
    Password = null, // or "secret123" for password-protected
    CustomRoomProperties = new Dictionary<string, object>
    {
        { "GameMode", "Deathmatch" },
        { "Map", "Arena1" }
    },
    CustomRoomPropertiesForLobby = new[] { "GameMode", "Map" }
};

EpicNetwork.CreateRoom("MyRoom", options);
```

### Join a Room

```csharp
// Join specific room
EpicNetwork.JoinRoom("MyRoom");

// Join with password
EpicNetwork.JoinRoom("MyRoom", password: "secret123");

// Join or create if doesn't exist
EpicNetwork.JoinRoom("MyRoom", createFallback: true);

// Join random available room
EpicNetwork.JoinRandomRoom();
```

### List Available Rooms

```csharp
EpicNetwork.GetRoomList();

public override void OnRoomListUpdate(List<EpicRoomInfo> rooms)
{
    foreach (var room in rooms)
    {
        Debug.Log($"{room.Name} ({room.PlayerCount}/{room.MaxPlayers})");
    }
}
```

## Next Steps

- [Network Objects & Views](guides/network-objects.md) - Learn about EpicView
- [RPCs](guides/rpcs.md) - Send messages between players
- [State Synchronization](guides/synchronization.md) - Sync custom data
- [Voice Chat](guides/voice-chat.md) - Add voice communication
