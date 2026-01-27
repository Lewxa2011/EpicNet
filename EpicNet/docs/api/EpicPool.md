# EpicPool

`public static class EpicPool`

High-performance object pooling system for networked GameObjects. Reduces garbage collection overhead by reusing objects instead of creating and destroying them.

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `true` | Whether pooling is active |
| `TotalPooledCount` | `int` | - | Total objects in all pools |

---

## How It Works

When `EpicPool.Enabled` is true:
- `EpicNetwork.Instantiate()` retrieves objects from the pool (or creates new ones)
- `EpicNetwork.Destroy()` returns objects to the pool instead of destroying them
- Objects are deactivated and parented to a hidden container when pooled

---

## Methods

### Prewarm

```csharp
public static void Prewarm(string prefabName, int count)
```

Pre-creates objects during loading to avoid runtime hitches.

```csharp
void Start()
{
    // Pre-create 50 bullets and 20 explosions
    EpicPool.Prewarm("Bullet", 50);
    EpicPool.Prewarm("Explosion", 20);
}
```

### Get

```csharp
public static GameObject Get(string prefabName, Vector3 position, Quaternion rotation)
```

Retrieves an object from the pool or creates a new one.

```csharp
// Usually called via EpicNetwork.Instantiate, but can be used directly for local-only objects
GameObject bullet = EpicPool.Get("Bullet", muzzle.position, muzzle.rotation);
```

### Return

```csharp
public static void Return(GameObject obj)
public static void Return(GameObject obj, float delay)
```

Returns an object to the pool.

```csharp
// Immediate return
EpicPool.Return(bullet);

// Return after 2 seconds (useful for effects)
EpicPool.Return(explosion, 2f);
```

### Clear

```csharp
public static void Clear(string prefabName)
public static void ClearAll()
```

Destroys pooled objects.

```csharp
// Clear specific pool
EpicPool.Clear("Bullet");

// Clear all pools (call when changing scenes)
EpicPool.ClearAll();
```

### GetPoolCount

```csharp
public static int GetPoolCount(string prefabName)
```

Gets the number of available objects in a pool.

```csharp
int available = EpicPool.GetPoolCount("Bullet");
Debug.Log($"Bullets in pool: {available}");
```

---

## IEpicPoolable Interface

Implement this interface on components that need reset logic when pooled.

```csharp
public interface IEpicPoolable
{
    void OnSpawnFromPool();  // Called when retrieved from pool
    void OnReturnToPool();   // Called before returning to pool
}
```

### Example: Projectile with Trail

```csharp
public class Projectile : MonoBehaviour, IEpicPoolable
{
    public float speed = 20f;
    public float lifetime = 3f;

    private TrailRenderer trail;
    private Rigidbody rb;

    void Awake()
    {
        trail = GetComponent<TrailRenderer>();
        rb = GetComponent<Rigidbody>();
    }

    public void OnSpawnFromPool()
    {
        // Reset state when spawning
        trail.Clear();
        rb.velocity = transform.forward * speed;

        // Return to pool after lifetime
        EpicPool.Return(gameObject, lifetime);
    }

    public void OnReturnToPool()
    {
        // Cleanup before pooling
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        CancelInvoke();
    }

    void OnTriggerEnter(Collider other)
    {
        // Spawn explosion effect (also pooled)
        EpicPool.Get("Explosion", transform.position, Quaternion.identity);

        // Return this projectile to pool
        EpicPool.Return(gameObject);
    }
}
```

### Example: Particle Effect

```csharp
public class PooledParticle : MonoBehaviour, IEpicPoolable
{
    private ParticleSystem particles;

    void Awake()
    {
        particles = GetComponent<ParticleSystem>();
    }

    public void OnSpawnFromPool()
    {
        particles.Clear();
        particles.Play();

        // Return when particles finish
        EpicPool.Return(gameObject, particles.main.duration);
    }

    public void OnReturnToPool()
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
```

---

## Physics Reset

EpicPool automatically resets `Rigidbody` and `Rigidbody2D` components:

- `velocity` set to zero
- `angularVelocity` set to zero

This prevents objects from retaining momentum from previous use.

---

## Best Practices

### 1. Prewarm During Loading

```csharp
IEnumerator LoadGame()
{
    // Show loading screen
    loadingScreen.SetActive(true);

    // Prewarm pools
    EpicPool.Prewarm("PlayerBullet", 100);
    EpicPool.Prewarm("EnemyBullet", 200);
    EpicPool.Prewarm("Explosion", 30);
    EpicPool.Prewarm("HitEffect", 50);

    yield return new WaitForSeconds(0.5f);
    loadingScreen.SetActive(false);
}
```

### 2. Clear Pools on Scene Change

```csharp
void OnSceneUnloaded(Scene scene)
{
    EpicPool.ClearAll();
}
```

### 3. Implement IEpicPoolable for Complex Objects

Always reset:
- Particle systems
- Trail renderers
- Animation states
- Script variables
- Audio sources

```csharp
public class ComplexEnemy : MonoBehaviour, IEpicPoolable
{
    private Animator animator;
    private AudioSource audio;
    private NavMeshAgent agent;

    public float health;
    public bool isAlerted;

    public void OnSpawnFromPool()
    {
        health = 100f;
        isAlerted = false;
        animator.Play("Idle", 0, 0f);
        audio.Stop();
        agent.enabled = true;
    }

    public void OnReturnToPool()
    {
        agent.enabled = false;
        StopAllCoroutines();
    }
}
```

### 4. Use for Frequently Spawned Objects

Good candidates for pooling:
- Projectiles (bullets, rockets, arrows)
- Particle effects (explosions, impacts, muzzle flashes)
- Temporary objects (pickups, debris)
- UI elements (damage numbers, nameplates)

Not worth pooling:
- Player characters (spawned once)
- Static level geometry
- Singleton managers

---

## Integration with EpicNetwork

When using `EpicNetwork.Instantiate()` and `EpicNetwork.Destroy()`, pooling is automatic:

```csharp
// This uses the pool automatically
GameObject obj = EpicNetwork.Instantiate("Bullet", pos, rot);

// This returns to pool automatically
EpicNetwork.Destroy(obj);
```

You can disable pooling globally:

```csharp
EpicPool.Enabled = false; // All spawns/destroys are now real
```
