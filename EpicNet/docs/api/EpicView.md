# EpicView

`public class EpicView : MonoBehaviour`

Identifies a GameObject as a networked object. Every networked object must have an EpicView component. It manages the object's ViewID, ownership, and routes RPCs and observable data.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `ViewID` | `int` | Unique network identifier for this object |
| `IsMine` | `bool` | True if the local player owns this object |
| `Owner` | `EpicPlayer` | The player who owns this object |
| `OwnershipTransfer` | `OwnershipOption` | How ownership can be transferred |
| `Synchronization` | `ViewSynchronization` | Data synchronization mode |
| `IsSceneView` | `bool` | True if this is a scene object (not spawned) |

---

## Ownership Options

```csharp
public enum OwnershipOption
{
    Fixed,      // Cannot be transferred
    Takeover,   // Any player can take ownership immediately
    Request     // Must request and receive approval from owner
}
```

### Fixed
Ownership is permanent. Use for player characters or objects that should always be controlled by one player.

### Takeover
Any player can take ownership at any time. Use for shared interactable objects like vehicles or weapons on the ground.

### Request
Ownership transfer must be approved by the current owner. Use for objects that need controlled handoff.

---

## Synchronization Modes

```csharp
public enum ViewSynchronization
{
    Off,                      // Manual RPCs only
    ReliableDeltaCompressed,  // Reliable, only sends changes
    Unreliable,               // Fast, may drop packets
    UnreliableOnChange        // Unreliable, only when values change
}
```

| Mode | Use Case |
|------|----------|
| `Off` | Objects that only use RPCs, no automatic state sync |
| `ReliableDeltaCompressed` | Important state that must arrive (health, inventory) |
| `Unreliable` | Frequently updated data (position, rotation) |
| `UnreliableOnChange` | Infrequently changing data that's not critical |

---

## Methods

### RPC

```csharp
public void RPC(string methodName, RpcTarget target, params object[] parameters)
```

Calls a method marked with `[EpicRPC]` on this object across the network.

**Parameters:**
- `methodName` - Name of the method to call
- `target` - Who should receive the RPC
- `parameters` - Arguments to pass (int, float, string, bool, Vector3, Quaternion, byte[])

```csharp
// On the sending side
view.RPC("TakeDamage", RpcTarget.All, 50f, "explosion");

// On the receiving side
[EpicRPC]
void TakeDamage(float amount, string source)
{
    health -= amount;
    Debug.Log($"Took {amount} damage from {source}");
}
```

### RPC (to specific player)

```csharp
public void RPC(string methodName, EpicPlayer targetPlayer, bool reliable, params object[] parameters)
```

Sends an RPC to a specific player only.

```csharp
view.RPC("ReceivePrivateMessage", somePlayer, true, "Hello!");
```

### RequestOwnership

```csharp
public void RequestOwnership()
```

Requests ownership transfer from the current owner (only works if `OwnershipTransfer` is `Request`).

### TransferOwnership

```csharp
public void TransferOwnership(EpicPlayer newOwner)
```

Transfers ownership to another player (only the owner can do this).

---

## RPC Targets

```csharp
public enum RpcTarget
{
    All,                    // Everyone including sender (reliable)
    Others,                 // Everyone except sender (reliable)
    MasterClient,           // Only the host (reliable)
    AllBuffered,            // Everyone + buffer for late joiners
    OthersBuffered,         // Others + buffer for late joiners
    AllViaServer,           // Everyone via master client relay
    AllBufferedViaServer,   // Everyone via relay + buffered
    AllUnreliable,          // Everyone (unreliable)
    OthersUnreliable,       // Others (unreliable)
    MasterClientUnreliable  // Host only (unreliable)
}
```

### Buffered RPCs

Buffered RPCs are saved and automatically sent to players who join later. Use for:
- Initial game state setup
- Spawning objects that should exist for all players
- One-time events that late joiners need to know about

```csharp
// This RPC will be sent to current and future players
view.RPC("SetTeamColor", RpcTarget.AllBuffered, Color.red);
```

---

## Observable Components

EpicView automatically finds all `IEpicObservable` components on the GameObject and synchronizes them at the configured `SendRate`.

```csharp
public class HealthSync : MonoBehaviour, IEpicObservable
{
    public float health = 100f;
    private EpicView view;

    void Awake()
    {
        view = GetComponent<EpicView>();
    }

    public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Owner sends data
            stream.SendNext(health);
        }
        else
        {
            // Others receive data
            health = (float)stream.ReceiveNext();
        }
    }
}
```

---

## Inspector Setup

1. Add `EpicView` to your prefab's root GameObject
2. Set `Ownership Transfer` based on your needs:
   - `Fixed` for player characters
   - `Takeover` for pickups/interactables
   - `Request` for controlled handoffs
3. Set `Synchronization` mode:
   - `Off` if using only RPCs
   - `Unreliable` for fast-moving objects
   - `ReliableDeltaCompressed` for important state
4. Add `EpicTransformView` if you need automatic position/rotation sync
5. Place prefab in a `Resources` folder

---

## Example: Complete Networked Object

```csharp
public class NetworkedPlayer : MonoBehaviour, IEpicObservable
{
    private EpicView view;
    public float health = 100f;
    public string playerName;

    void Awake()
    {
        view = GetComponent<EpicView>();
    }

    void Update()
    {
        if (!view.IsMine) return;

        // Only the owner controls movement
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        transform.Translate(new Vector3(h, 0, v) * Time.deltaTime * 5f);

        // Fire weapon
        if (Input.GetMouseButtonDown(0))
        {
            view.RPC("FireWeapon", RpcTarget.All);
        }
    }

    public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(health);
            stream.SendNext(playerName);
        }
        else
        {
            health = (float)stream.ReceiveNext();
            playerName = (string)stream.ReceiveNext();
        }
    }

    [EpicRPC]
    void FireWeapon()
    {
        // Play fire animation/sound for all players
        Debug.Log($"{playerName} fired!");
    }

    [EpicRPC(Security = RpcSecurityLevel.OwnerOnly)]
    void TakeDamage(float amount)
    {
        health -= amount;
        if (health <= 0)
        {
            view.RPC("OnDeath", RpcTarget.All);
        }
    }

    [EpicRPC]
    void OnDeath()
    {
        Debug.Log($"{playerName} died!");
    }
}
```
