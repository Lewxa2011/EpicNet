# EpicNet - Complete Implementation Documentation

## Overview
EpicNet is now a fully-featured networking library for Unity using Epic Online Services (EOS). This document covers all implemented features.

---

## Implemented Features

### 1. **RPC System (Remote Procedure Calls)**

Call methods across the network on all clients or specific targets.

#### Usage:
```csharp
// Mark methods with [EpicRPC] attribute
[EpicRPC]
private void MyRPCMethod(string message, int value, EpicMessageInfo info)
{
    Debug.Log($"RPC received from {info.Sender.NickName}: {message}");
}

// Call RPC on network view
epicView.RPC("MyRPCMethod", RpcTarget.All, "Hello", 42);
```

#### RPC Targets:
- `RpcTarget.All` - Send to all clients including self
- `RpcTarget.Others` - Send to all except self
- `RpcTarget.MasterClient` - Send only to master
- `RpcTarget.AllBuffered` - Send to all and buffer for late joiners
- `RpcTarget.OthersBuffered` - Send to others and buffer
- `RpcTarget.AllViaServer` - Route through server
- `RpcTarget.AllBufferedViaServer` - Route through server and buffer

#### Supported Parameter Types:
- `int`, `float`, `bool`, `string`
- `Vector3`, `Quaternion`
- `byte[]`
- Any type with `.ToString()` (fallback)

---

### 2. **Observable Synchronization System**

Automatically sync component state across the network.

#### Usage:
```csharp
public class MyComponent : MonoBehaviour, IEpicObservable
{
    public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Send data
            stream.SendNext(transform.position);
            stream.SendNext(health);
        }
        else
        {
            // Receive data
            transform.position = (Vector3)stream.ReceiveNext();
            health = (int)stream.ReceiveNext();
        }
    }
}
```

**Built-in Components:**
- `EpicTransformView` - Syncs position, rotation, scale
- `EpicVC` - Voice chat (Opus codec)

#### Configuration:
```csharp
EpicNetwork.SendRate = 20f; // 20 Hz sync rate (default)
```

---

### 3. **Complete P2P Communication**

Full peer-to-peer networking with automatic connection management.

**Features:**
- Automatic P2P connection acceptance
- Reliable ordered packet delivery
- Ping/latency measurement
- Packet serialization/deserialization

#### Ping Measurement:
```csharp
EpicNetwork.SendPing(targetPlayer);
int latency = EpicNetwork.Ping; // milliseconds
```

---

### 4. **Room/Lobby System**

Complete room management with properties and listing.

#### Create Room:
```csharp
var options = new EpicRoomOptions
{
    MaxPlayers = 10,
    IsVisible = true,
    IsOpen = true,
    CustomRoomProperties = new Dictionary<string, object>
    {
        { "GameMode", "Deathmatch" },
        { "Map", "Desert" }
    }
};
EpicNetwork.CreateRoom("MyRoom", options);
```

#### Join Room:
```csharp
// By name
EpicNetwork.JoinRoom("MyRoom");

// Random room
EpicNetwork.JoinRandomRoom(createIfNoneAvailable: true);
```

#### Room Listing:
```csharp
// Request room list
EpicNetwork.GetRoomList();

// Subscribe to updates
EpicNetwork.OnRoomListUpdate += (rooms) =>
{
    foreach (var room in rooms)
    {
        Debug.Log($"{room.Name}: {room.PlayerCount}/{room.MaxPlayers}");
    }
};
```

#### Room Properties:
```csharp
// Set (Master Client only)
EpicNetwork.SetRoomProperties(new Dictionary<string, object>
{
    { "Round", 3 },
    { "TimeLimit", 300 }
});

// Get
object round = EpicNetwork.CurrentRoom.CustomProperties["Round"];
```

---

### 5. **Player Properties**

Set custom properties on players visible to all.

```csharp
EpicNetwork.SetLocalPlayerProperties(new Dictionary<string, object>
{
    { "Team", "Red" },
    { "Score", 100 },
    { "Class", "Warrior" }
});

// Access other player properties
object team = player.CustomProperties["Team"];
```

---

### 6. **Network Object Management**

Instantiate and destroy networked GameObjects.

#### Instantiate:
```csharp
// Must be in Resources folder
GameObject obj = EpicNetwork.Instantiate(
    "MyPrefab",
    Vector3.zero,
    Quaternion.identity
);
```

#### Destroy:
```csharp
EpicNetwork.Destroy(gameObject);
```

**Features:**
- Automatic ViewID assignment
- Owner tracking
- Automatic cleanup on player disconnect

---

### 7. **Ownership System**

Transfer ownership of network objects between players.

#### Ownership Options:
```csharp
[SerializeField] private OwnershipOption ownershipTransfer = OwnershipOption.Takeover;
```

- `Fixed` - Cannot transfer ownership
- `Takeover` - Anyone can take ownership instantly
- `Request` - Must request from current owner

#### Transfer Ownership:
```csharp
// Takeover mode
epicView.RequestOwnership(); // Instant

// Request mode
epicView.RequestOwnership(); // Sends request to owner

// Manual transfer
epicView.TransferOwnership(targetPlayer);
```

#### Custom Approval:
```csharp
protected override bool OnOwnershipRequestReceived(EpicPlayer requester)
{
    // Custom logic
    bool canTake = CheckIfPlayerIsAllowed(requester);
    return canTake;
}
```

---

### 8. **Late Joiner Support**

New players receive complete game state on join.

**Automatically Synced:**
- All network objects (position, rotation, owner)
- Buffered RPCs
- Room properties
- Player properties

**How it works:**
1. Player joins room
2. Master client detects new player
3. Master sends initial state packet
4. Late joiner spawns all objects and executes buffered RPCs

---

### 9. **Host Migration**

Automatic master client switching when host leaves.

**What happens:**
1. Master client disconnects
2. New master selected (lowest actor number)
3. New master promoted to lobby owner
4. Orphaned objects reassigned to new master
5. `OnMasterClientSwitched` event fired

```csharp
EpicNetwork.OnMasterClientSwitched += (newMaster) =>
{
    if (EpicNetwork.IsMasterClient)
    {
        Debug.Log("I am now the master!");
        // Take over master responsibilities
    }
};
```

---

### 10. **Network Statistics**

Monitor network performance.

```csharp
int ping = EpicNetwork.Ping; // Latency in ms
float sendRate = EpicNetwork.SendRate; // Updates per second
int objectCount = networkObjects.Count; // Network object count
```

---

## Setup Requirements

### 1. **Call EpicNetwork.Update() in MonoBehaviour**
```csharp
private void Update()
{
    EpicNetwork.Update(); // CRITICAL - processes messages and syncs observables
}
```

### 2. **Prefabs in Resources Folder**
All networked prefabs must be in `Resources/` folder:
```
Assets/
  Resources/
    MyPrefab.prefab
    Net Player.prefab
```

### 3. **EpicView Component**
Every networked GameObject needs `EpicView` component:
```csharp
[RequireComponent(typeof(EpicView))]
public class MyNetworkScript : MonoBehaviour
{
    private EpicView _view;
    
    void Awake()
    {
        _view = GetComponent<EpicView>();
    }
}
```

---

## Code Examples

### Complete Network Player
```csharp
public class NetworkPlayer : MonoBehaviour
{
    private EpicView _view;
    
    void Awake()
    {
        _view = GetComponent<EpicView>();
    }
    
    void Start()
    {
        if (_view.IsMine)
        {
            // Set player color and sync to all
            Color myColor = Random.ColorHSV();
            _view.RPC("SetColor", RpcTarget.AllBuffered, myColor);
        }
    }
    
    [EpicRPC]
    void SetColor(Color color, EpicMessageInfo info)
    {
        GetComponent<Renderer>().material.color = color;
    }
}
```

### Master-Only Logic
```csharp
void Update()
{
    if (EpicNetwork.IsMasterClient)
    {
        // Spawn enemies, manage game state, etc.
        if (Time.time > nextSpawnTime)
        {
            SpawnEnemy();
        }
    }
}

void SpawnEnemy()
{
    Vector3 spawnPos = GetRandomSpawnPoint();
    GameObject enemy = EpicNetwork.Instantiate("Enemy", spawnPos, Quaternion.identity);
}
```

### Custom Observable Component
```csharp
public class SyncedHealth : MonoBehaviour, IEpicObservable
{
    private int _health = 100;
    private EpicView _view;
    
    void Awake()
    {
        _view = GetComponent<EpicView>();
    }
    
    public void TakeDamage(int damage)
    {
        if (_view.IsMine)
        {
            _health -= damage;
            _view.RPC("ApplyDamage", RpcTarget.Others, damage);
        }
    }
    
    [EpicRPC]
    void ApplyDamage(int damage)
    {
        _health -= damage;
    }
    
    public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(_health);
        }
        else
        {
            _health = (int)stream.ReceiveNext();
        }
    }
}
```

---

## Best Practices

### 1. **Use RPCs for Events**
```csharp
// Good: Use RPC for one-time events
_view.RPC("PlayAnimation", RpcTarget.All, "Jump");

// Bad: Don't sync state that changes constantly via RPC
// Use IEpicObservable instead
```

### 2. **Buffer Important RPCs**
```csharp
// Late joiners will receive this
_view.RPC("SetPlayerName", RpcTarget.AllBuffered, playerName);
```

### 3. **Check Ownership Before Modifying**
```csharp
void Update()
{
    if (_view.IsMine)
    {
        // Only owner should modify
        transform.position += velocity * Time.deltaTime;
    }
}
```

### 4. **Use Master Client for Authority**
```csharp
public void RequestPickup(int itemId)
{
    // Send request to master
    _view.RPC("MasterHandlePickup", RpcTarget.MasterClient, itemId, EpicNetwork.LocalPlayer.ActorNumber);
}

[EpicRPC]
void MasterHandlePickup(int itemId, int requesterId)
{
    if (EpicNetwork.IsMasterClient)
    {
        // Validate and broadcast result
        bool success = CanPickup(itemId, requesterId);
        _view.RPC("PickupResult", RpcTarget.All, itemId, requesterId, success);
    }
}
```

---

## Limitations & Notes

1. **No localStorage/sessionStorage** - Use in-memory state only
2. **Prefabs must be in Resources/** - Required for network instantiation
3. **EpicNetwork.Update() must be called** - Critical for message processing
4. **Late joiner sync is master-only** - Master client sends initial state
5. **Buffered RPC limit** - Consider clearing old buffered RPCs periodically
6. **P2P connections** - All traffic is peer-to-peer, no dedicated server

---

## Troubleshooting

### Objects not syncing?
- Ensure `EpicNetwork.Update()` is called in a MonoBehaviour
- Check that `IEpicObservable` components are on the GameObject
- Verify `EpicView` component is present

### RPCs not received?
- Check method has `[EpicRPC]` attribute
- Verify method signature matches call
- Ensure you're in a room (`EpicNetwork.InRoom`)

### Late joiners missing objects?
- Make sure objects were instantiated via `EpicNetwork.Instantiate()`
- Verify master client is in the room
- Check P2P connections are established

### Host migration issues?
- Orphaned objects are reassigned to new master
- Use `OnMasterClientSwitched` event to handle transitions
- Master-only logic should check `IsMasterClient` continuously

---

## API Reference

### Core Methods
- `EpicNetwork.Update()` - Process messages (call every frame)
- `EpicNetwork.LoginWithDeviceId(name, callback)` - Login to EOS
- `EpicNetwork.ConnectUsingSettings()` - Connect to services
- `EpicNetwork.CreateRoom(name, options)` - Create new room
- `EpicNetwork.JoinRoom(name)` - Join specific room
- `EpicNetwork.JoinRandomRoom(create)` - Join any available room
- `EpicNetwork.LeaveRoom()` - Leave current room
- `EpicNetwork.GetRoomList()` - Request available rooms
- `EpicNetwork.Instantiate(prefab, pos, rot)` - Spawn networked object
- `EpicNetwork.Destroy(obj)` - Destroy networked object
- `EpicNetwork.SetRoomProperties(dict)` - Set room custom properties
- `EpicNetwork.SetLocalPlayerProperties(dict)` - Set player properties

### EpicView Methods
- `RPC(methodName, target, params)` - Call networked method
- `RequestOwnership()` - Request to become owner
- `TransferOwnership(player)` - Give ownership to player

### Events
- `OnConnectedToMaster` - Connected to services
- `OnJoinedRoom` - Successfully joined room
- `OnLeftRoom` - Left the room
- `OnPlayerEnteredRoom(player)` - New player joined
- `OnPlayerLeftRoom(player)` - Player disconnected
- `OnMasterClientSwitched(newMaster)` - Master changed
- `OnRoomListUpdate(rooms)` - Room list received

---

## Performance Tips

1. **Adjust send rate based on needs:**
   ```csharp
   EpicNetwork.SendRate = 10f; // For slow-paced games
   EpicNetwork.SendRate = 30f; // For fast-paced action
   ```

2. **Only sync what's necessary:**
   ```csharp
   if (stream.IsWriting && HasChanged())
   {
       stream.SendNext(value);
   }
   ```

3. **Use buffered RPCs sparingly:**
   - They're stored permanently until room closes
   - Clear buffers if needed for long-running rooms

4. **Batch RPC calls when possible:**
   ```csharp
   _view.RPC("UpdateMultiple", RpcTarget.All, value1, value2, value3);
   ```

---

## Current Features

✅ RPC System with multiple targets
✅ Observable sync system (IEpicObservable)  
✅ P2P connection management  
✅ Room creation and joining  
✅ Room listing and browsing  
✅ Custom room properties  
✅ Custom player properties  
✅ Network object instantiation/destruction  
✅ Ownership transfer system  
✅ Late joiner support  
✅ Host migration  
✅ Buffered RPCs  
✅ Network statistics  
✅ Ping measurement  
✅ Voice chat (EpicVC)  
✅ Transform synchronization (EpicTransformView)