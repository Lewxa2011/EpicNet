# EpicStream

`public class EpicStream`

Bidirectional data stream for serializing and deserializing network data. Used in `IEpicObservable.OnEpicSerializeView()` for automatic state synchronization.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsWriting` | `bool` | True if the local player owns the object (sending data) |
| `IsReading` | `bool` | True if receiving data from another player |
| `Count` | `int` | Number of items in the stream |

---

## Methods

### SendNext

```csharp
public void SendNext(object value)
```

Writes a value to the stream (only call when `IsWriting` is true).

**Supported types:**
- `int`
- `float`
- `string`
- `bool`
- `Vector3`
- `Quaternion`
- `byte[]`

### ReceiveNext

```csharp
public object ReceiveNext()
```

Reads the next value from the stream (only call when `IsReading` is true).

### TryReceiveNext<T>

```csharp
public bool TryReceiveNext<T>(out T value)
```

Type-safe reading with null/failure handling.

---

## Basic Usage

```csharp
public class PlayerSync : MonoBehaviour, IEpicObservable
{
    public float health = 100f;
    public int ammo = 30;
    public string status = "alive";

    public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Owner sends data
            stream.SendNext(health);
            stream.SendNext(ammo);
            stream.SendNext(status);
        }
        else
        {
            // Others receive data
            health = (float)stream.ReceiveNext();
            ammo = (int)stream.ReceiveNext();
            status = (string)stream.ReceiveNext();
        }
    }
}
```

---

## Advanced Patterns

### Conditional Serialization

Only send data that changed:

```csharp
public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
{
    if (stream.IsWriting)
    {
        // Send flags indicating what changed
        byte flags = 0;
        if (healthChanged) flags |= 1;
        if (ammoChanged) flags |= 2;

        stream.SendNext(flags);
        if (healthChanged) stream.SendNext(health);
        if (ammoChanged) stream.SendNext(ammo);

        healthChanged = false;
        ammoChanged = false;
    }
    else
    {
        byte flags = (byte)(int)stream.ReceiveNext();
        if ((flags & 1) != 0) health = (float)stream.ReceiveNext();
        if ((flags & 2) != 0) ammo = (int)stream.ReceiveNext();
    }
}
```

### Vector3 with Compression

```csharp
public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
{
    if (stream.IsWriting)
    {
        // Send position (Vector3 is natively supported)
        stream.SendNext(transform.position);
        stream.SendNext(transform.rotation);
    }
    else
    {
        Vector3 pos = (Vector3)stream.ReceiveNext();
        Quaternion rot = (Quaternion)stream.ReceiveNext();

        // Apply with interpolation
        transform.position = Vector3.Lerp(transform.position, pos, 0.5f);
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, 0.5f);
    }
}
```

### Complex Data with byte[]

For custom data structures, serialize to bytes:

```csharp
[System.Serializable]
public class PlayerState
{
    public float health;
    public int[] inventory;
    public Vector3 lastCheckpoint;
}

public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
{
    if (stream.IsWriting)
    {
        string json = JsonUtility.ToJson(playerState);
        byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
        stream.SendNext(data);
    }
    else
    {
        byte[] data = (byte[])stream.ReceiveNext();
        string json = System.Text.Encoding.UTF8.GetString(data);
        playerState = JsonUtility.FromJson<PlayerState>(json);
    }
}
```

---

## Using EpicMessageInfo

The `info` parameter provides context about the message:

```csharp
public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
{
    if (stream.IsReading)
    {
        // Who sent this data?
        Debug.Log($"Data from: {info.Sender?.NickName}");

        // When was it sent? (local timestamp)
        double lag = Time.timeAsDouble - info.Timestamp;
        Debug.Log($"Lag: {lag * 1000:F0}ms");
    }
}
```

---

# IEpicObservable

`public interface IEpicObservable`

Interface for components that synchronize data automatically.

## Method

```csharp
void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info);
```

Called at the `EpicNetwork.SendRate` frequency (default 20Hz) to sync data.

## Implementation

```csharp
public class MyComponent : MonoBehaviour, IEpicObservable
{
    private float syncedValue;

    public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(syncedValue);
        }
        else
        {
            syncedValue = (float)stream.ReceiveNext();
        }
    }
}
```

---

# EpicMessageInfo

`public struct EpicMessageInfo`

Metadata about a received network message.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Sender` | `EpicPlayer` | Player who sent the message (may be null if disconnected) |
| `Timestamp` | `double` | Local time when message was received |

## Usage in RPC

```csharp
[EpicRPC]
void ReceiveChat(string message, EpicMessageInfo info)
{
    Debug.Log($"[{info.Sender?.NickName}]: {message}");
}
```

---

# EpicTransformView

`public class EpicTransformView : MonoBehaviour, IEpicObservable`

Built-in component for automatic transform synchronization with interpolation.

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SynchronizePosition` | `bool` | `true` | Sync position |
| `SynchronizeRotation` | `bool` | `true` | Sync rotation |
| `SynchronizeScale` | `bool` | `false` | Sync scale |
| `LerpSpeed` | `float` | `10f` | Interpolation speed |
| `TeleportDistance` | `float` | `5f` | Distance to snap instead of lerp |
| `UseExtrapolation` | `bool` | `true` | Predict movement based on velocity |

## Usage

1. Add `EpicView` to your prefab
2. Add `EpicTransformView` to the same GameObject
3. Configure sync options in the inspector
4. The component handles everything automatically

For the owner, position is controlled directly. For remote players, the transform smoothly interpolates to the network position.
