# EpicNetwork

`public static class EpicNetwork`

The central static manager class for all networking operations. Handles authentication, room management, object spawning, RPCs, and P2P communication.

## Properties

### Connection State

| Property | Type | Description |
|----------|------|-------------|
| `IsConnected` | `bool` | Whether connected to EOS services |
| `IsLoggedIn` | `bool` | Whether logged in with a valid user ID |
| `InRoom` | `bool` | Whether currently in a room/lobby |
| `IsMasterClient` | `bool` | Whether the local player is the room host |

### Players

| Property | Type | Description |
|----------|------|-------------|
| `LocalPlayer` | `EpicPlayer` | The local player instance |
| `MasterClient` | `EpicPlayer` | The current room host |
| `PlayerList` | `List<EpicPlayer>` | All players in the current room |
| `NickName` | `string` | Local player's display name (get/set) |

### Room

| Property | Type | Description |
|----------|------|-------------|
| `CurrentRoom` | `EpicRoom` | The current room instance |

### Network Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SendRate` | `int` | `20` | Observable sync rate in Hz |
| `Ping` | `int` | - | Current round-trip latency in ms |

### Reconnection Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AutoReconnect` | `bool` | `true` | Auto-reconnect on disconnect |
| `ReconnectDelay` | `float` | `2.0f` | Seconds between reconnect attempts |
| `MaxReconnectAttempts` | `int` | `5` | Max reconnection tries |

---

## Events

### Connection Events

```csharp
public static event Action OnConnectedToMaster;
public static event Action OnJoinedRoom;
public static event Action OnLeftRoom;
public static event Action<string> OnJoinRoomFailed;
```

### Player Events

```csharp
public static event Action<EpicPlayer> OnPlayerEnteredRoom;
public static event Action<EpicPlayer> OnPlayerLeftRoom;
public static event Action<EpicPlayer> OnMasterClientSwitched;
```

### Authentication Events

```csharp
public static event Action OnLoginSuccess;
public static event Action<string> OnLoginFailed;
```

### Room List Events

```csharp
public static event Action<List<EpicRoomInfo>> OnRoomListUpdate;
```

### Reconnection Events

```csharp
public static event Action OnReconnecting;
public static event Action OnReconnected;
public static event Action OnReconnectFailed;
```

### Moderation Events

```csharp
public static event Action<string> OnKicked; // reason
```

---

## Methods

### Update Loop

#### Update()

```csharp
public static void Update()
```

**Must be called every frame** in your MonoBehaviour's `Update()`. Processes P2P messages, syncs observables, and handles reconnection.

```csharp
void Update()
{
    EpicNetwork.Update();
}
```

---

### Authentication

#### LoginWithDeviceId

```csharp
public static void LoginWithDeviceId(string displayName, Action<bool, string> callback = null)
```

Authenticates using device-specific credentials. On mobile, uses the device's unique ID. On desktop, generates a persistent device ID.

**Parameters:**
- `displayName` - Player's display name
- `callback` - Optional callback with (success, message)

```csharp
EpicNetwork.LoginWithDeviceId("MyPlayer", (success, msg) =>
{
    if (success)
        EpicNetwork.ConnectUsingSettings();
    else
        Debug.LogError($"Login failed: {msg}");
});
```

#### Logout

```csharp
public static void Logout()
```

Logs out and cleans up all network state.

---

### Connection

#### ConnectUsingSettings

```csharp
public static void ConnectUsingSettings()
```

Initializes P2P networking after successful login. Fires `OnConnectedToMaster` when complete.

---

### Room Management

#### CreateRoom

```csharp
public static void CreateRoom(string roomName, EpicRoomOptions options = null)
```

Creates a new room with the specified options.

```csharp
var options = new EpicRoomOptions
{
    MaxPlayers = 8,
    IsVisible = true,
    Password = "secret",
    CustomRoomProperties = new Dictionary<string, object>
    {
        { "Mode", "CTF" }
    }
};
EpicNetwork.CreateRoom("MyRoom", options);
```

#### JoinRoom

```csharp
public static void JoinRoom(string roomName, bool createFallback = false, string password = null)
```

Joins an existing room.

**Parameters:**
- `roomName` - The room name to join
- `createFallback` - If true, creates the room if it doesn't exist
- `password` - Password for protected rooms

#### JoinRandomRoom

```csharp
public static void JoinRandomRoom()
```

Joins a random available room.

#### LeaveRoom

```csharp
public static void LeaveRoom()
```

Leaves the current room.

#### GetRoomList

```csharp
public static void GetRoomList()
```

Queries available rooms. Results delivered via `OnRoomListUpdate` event.

#### SetRoomProperties

```csharp
public static void SetRoomProperties(Dictionary<string, object> properties)
```

Updates room properties (master client only).

---

### Object Instantiation

#### Instantiate

```csharp
public static GameObject Instantiate(string prefabName, Vector3 position, Quaternion rotation)
```

Spawns a networked object from a prefab in the Resources folder. The object is owned by the local player.

```csharp
GameObject player = EpicNetwork.Instantiate("PlayerPrefab", spawnPoint, Quaternion.identity);
```

#### Destroy

```csharp
public static void Destroy(GameObject obj)
```

Destroys a networked object across all clients. Only the owner or master client can destroy objects.

```csharp
EpicNetwork.Destroy(myNetworkObject);
```

---

### RPC (Remote Procedure Calls)

#### RPC (by ViewID)

```csharp
public static void RPC(EpicView view, string methodName, RpcTarget target, params object[] parameters)
```

Calls an RPC on a specific network object.

```csharp
view.RPC("TakeDamage", RpcTarget.All, 25f);
```

#### RPC (to specific player)

```csharp
public static void RPC(EpicView view, string methodName, EpicPlayer targetPlayer, bool reliable, params object[] parameters)
```

Sends an RPC to a specific player only.

---

### Player Management (Master Client Only)

#### KickPlayer

```csharp
public static void KickPlayer(EpicPlayer player, string reason = "")
```

Kicks a player from the room.

#### BanPlayer

```csharp
public static void BanPlayer(EpicPlayer player)
```

Bans a player (they cannot rejoin until the room is recreated).

#### UnbanPlayer

```csharp
public static void UnbanPlayer(ProductUserId userId)
```

Removes a ban.

#### ClearBans

```csharp
public static void ClearBans()
```

Clears all bans.

---

### Reconnection

#### Reconnect

```csharp
public static void Reconnect()
```

Manually triggers reconnection to the last room.

#### CancelReconnect

```csharp
public static void CancelReconnect()
```

Cancels ongoing reconnection attempts.

---

### Network Statistics

#### GetNetworkStats

```csharp
public static NetworkStats GetNetworkStats()
```

Returns current network statistics.

```csharp
var stats = EpicNetwork.GetNetworkStats();
Debug.Log($"Bytes sent: {stats.BytesSent}");
Debug.Log($"Packet loss: {stats.PacketLossPercent}%");
```

**NetworkStats struct:**

| Field | Type | Description |
|-------|------|-------------|
| `BytesSent` | `long` | Total bytes sent |
| `BytesReceived` | `long` | Total bytes received |
| `PacketsSent` | `int` | Total packets sent |
| `PacketsReceived` | `int` | Total packets received |
| `PacketLossPercent` | `float` | Estimated packet loss % |

---

## Usage Example

```csharp
public class GameManager : EpicMonoBehaviourCallbacks
{
    void Start()
    {
        EpicNetwork.NickName = "Player123";
        EpicNetwork.LoginWithDeviceId(EpicNetwork.NickName, OnLogin);
    }

    void Update()
    {
        EpicNetwork.Update();
    }

    void OnLogin(bool success, string msg)
    {
        if (success) EpicNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        EpicNetwork.JoinRoom("Lobby", createFallback: true);
    }

    public override void OnJoinedRoom()
    {
        var player = EpicNetwork.Instantiate("Player", Vector3.zero, Quaternion.identity);
    }

    public override void OnPlayerEnteredRoom(EpicPlayer player)
    {
        Debug.Log($"{player.NickName} joined!");
    }
}
```
