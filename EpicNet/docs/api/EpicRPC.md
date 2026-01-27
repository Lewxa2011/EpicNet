# EpicRPC Attribute

`[EpicRPC]`

Marks a method as callable over the network. RPC (Remote Procedure Call) methods can be invoked on remote clients using `EpicView.RPC()`.

## Basic Usage

```csharp
// Define an RPC method
[EpicRPC]
void PlaySound(string soundName)
{
    AudioManager.Play(soundName);
}

// Call it over the network
view.RPC("PlaySound", RpcTarget.All, "explosion");
```

---

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Security` | `RpcSecurityLevel` | `Anyone` | Who can call this RPC |

---

## Security Levels

```csharp
public enum RpcSecurityLevel
{
    Anyone,              // Default - any player can call
    OwnerOnly,           // Only the object owner
    MasterClientOnly,    // Only the host
    OwnerOrMasterClient  // Owner or host
}
```

### Anyone (Default)

Any player can call this RPC. Use for general gameplay events.

```csharp
[EpicRPC]
void PlayEffect(Vector3 position)
{
    Instantiate(effectPrefab, position, Quaternion.identity);
}
```

### OwnerOnly

Only the owner of the EpicView can call this RPC. Use for actions that should only come from the controlling player.

```csharp
[EpicRPC(Security = RpcSecurityLevel.OwnerOnly)]
void TakeDamage(float amount)
{
    // Only the player who owns this character can report damage
    health -= amount;
}
```

### MasterClientOnly

Only the master client (host) can call this RPC. Use for authoritative game state.

```csharp
[EpicRPC(Security = RpcSecurityLevel.MasterClientOnly)]
void StartRound(int roundNumber)
{
    // Only the host can start rounds
    currentRound = roundNumber;
    gameState = GameState.Playing;
}
```

### OwnerOrMasterClient

Either the owner or the master client can call this RPC.

```csharp
[EpicRPC(Security = RpcSecurityLevel.OwnerOrMasterClient)]
void ForceRespawn()
{
    // Owner can respawn themselves, host can respawn anyone
    transform.position = spawnPoint;
    health = maxHealth;
}
```

---

## RPC Targets

```csharp
public enum RpcTarget
{
    // Reliable (guaranteed delivery, ordered)
    All,                    // Everyone including sender
    Others,                 // Everyone except sender
    MasterClient,           // Only the host
    AllBuffered,            // Everyone + save for late joiners
    OthersBuffered,         // Others + save for late joiners
    AllViaServer,           // Everyone, relayed through host
    AllBufferedViaServer,   // Everyone via relay + buffered

    // Unreliable (may drop, no ordering)
    AllUnreliable,          // Everyone, fast but may drop
    OthersUnreliable,       // Others, fast but may drop
    MasterClientUnreliable  // Host only, fast but may drop
}
```

### When to Use Each Target

| Target | Use Case |
|--------|----------|
| `All` | Events all players should see (animations, sounds) |
| `Others` | Events sender doesn't need (their client handles it locally) |
| `MasterClient` | Requesting server-side validation |
| `AllBuffered` | State that late joiners need (team colors, names) |
| `OthersBuffered` | Same, but sender doesn't need the callback |
| `AllViaServer` | When ordering matters across all clients |
| `AllUnreliable` | Frequent updates (position hints) |
| `OthersUnreliable` | Sender handles locally, others get hints |

---

## Parameter Types

RPCs support these parameter types:

| Type | Notes |
|------|-------|
| `int` | 32-bit integer |
| `float` | Single precision |
| `string` | UTF-8 encoded |
| `bool` | True/false |
| `Vector3` | Position/direction |
| `Quaternion` | Rotation |
| `byte[]` | Raw data (for custom serialization) |

### Example with Multiple Parameters

```csharp
[EpicRPC]
void SpawnProjectile(Vector3 position, Quaternion rotation, float speed, string projectileType)
{
    var prefab = Resources.Load<GameObject>($"Projectiles/{projectileType}");
    var proj = Instantiate(prefab, position, rotation);
    proj.GetComponent<Rigidbody>().velocity = rotation * Vector3.forward * speed;
}

// Call it
view.RPC("SpawnProjectile", RpcTarget.All,
    muzzle.position,
    muzzle.rotation,
    bulletSpeed,
    "Rocket"
);
```

---

## EpicMessageInfo Parameter

Add `EpicMessageInfo` as the last parameter to get sender information:

```csharp
[EpicRPC]
void ReceiveChat(string message, EpicMessageInfo info)
{
    string sender = info.Sender?.NickName ?? "Unknown";
    chatLog.Add($"[{sender}]: {message}");
}

[EpicRPC]
void HitRegistration(float damage, Vector3 hitPoint, EpicMessageInfo info)
{
    // Verify the sender is the expected attacker
    if (info.Sender != expectedAttacker)
    {
        Debug.LogWarning("Suspicious hit registration!");
        return;
    }

    ApplyDamage(damage, hitPoint);
}
```

---

## Buffered RPCs

Buffered RPCs are saved and replayed to players who join later.

```csharp
// When a player selects their team
[EpicRPC]
void SetTeamColor(int colorIndex)
{
    renderer.material.color = teamColors[colorIndex];
}

void OnTeamSelected(int team)
{
    // AllBuffered ensures late joiners see the correct color
    view.RPC("SetTeamColor", RpcTarget.AllBuffered, team);
}
```

### Clearing Buffered RPCs

Buffered RPCs are cleared when:
- The object is destroyed
- The room is left
- Manually (not currently exposed in API)

---

## Best Practices

### 1. Validate Input on Authoritative Side

```csharp
[EpicRPC(Security = RpcSecurityLevel.Anyone)]
void RequestPurchase(string itemId, EpicMessageInfo info)
{
    if (!EpicNetwork.IsMasterClient) return;

    // Validate on host
    var player = info.Sender;
    if (!CanAfford(player, itemId))
    {
        // Send denial
        view.RPC("PurchaseDenied", player, true, itemId, "Not enough coins");
        return;
    }

    // Process purchase
    DeductCoins(player, GetPrice(itemId));
    view.RPC("PurchaseConfirmed", RpcTarget.All, player.ActorNumber, itemId);
}
```

### 2. Use Unreliable for Frequent Updates

```csharp
// Position hints - okay if some are dropped
[EpicRPC]
void UpdateAimDirection(Vector3 direction)
{
    aimIndicator.forward = direction;
}

void Update()
{
    if (view.IsMine && aimChanged)
    {
        // Unreliable is fine for frequently updated, non-critical data
        view.RPC("UpdateAimDirection", RpcTarget.OthersUnreliable, aimDirection);
    }
}
```

### 3. Combine Related Data

```csharp
// Bad: Multiple RPCs for related data
view.RPC("SetHealth", RpcTarget.All, health);
view.RPC("SetArmor", RpcTarget.All, armor);
view.RPC("SetShield", RpcTarget.All, shield);

// Good: Single RPC with all data
view.RPC("SetDefensiveStats", RpcTarget.All, health, armor, shield);
```

### 4. Use Security Levels Appropriately

```csharp
// Player actions - OwnerOnly prevents spoofing
[EpicRPC(Security = RpcSecurityLevel.OwnerOnly)]
void Jump() { }

// Game state - MasterClientOnly for authority
[EpicRPC(Security = RpcSecurityLevel.MasterClientOnly)]
void SetScore(int playerActor, int score) { }

// Visual effects - Anyone is fine
[EpicRPC]
void PlayVFX(string vfxName) { }
```
