using UnityEngine;
using System.Collections.Generic;

namespace EpicNet
{
    /// <summary>
    /// High-performance object pool for networked GameObjects.
    /// Reduces garbage collection and instantiation overhead by reusing objects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When enabled, <see cref="EpicNetwork.Instantiate"/> and <see cref="EpicNetwork.Destroy"/>
    /// automatically use the pool instead of creating/destroying objects.
    /// </para>
    /// <para>
    /// For best performance, call <see cref="Prewarm"/> during loading to pre-create objects.
    /// </para>
    /// <para>
    /// Implement <see cref="IEpicPoolable"/> on components that need reset logic when
    /// spawned from or returned to the pool.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Pre-warm the pool during loading
    /// EpicPool.Prewarm("Projectile", 50);
    ///
    /// // Objects are automatically pooled when using EpicNetwork
    /// var obj = EpicNetwork.Instantiate("Projectile", pos, rot);
    /// EpicNetwork.Destroy(obj);
    /// </code>
    /// </example>
    public static class EpicPool
    {
        #region Constants

        private const string PoolParentName = "[EpicNet Pool]";
        private const int MaxDequeueRetries = 10;

        #endregion

        #region Private Fields

        private static readonly Dictionary<string, Queue<GameObject>> _pools = new Dictionary<string, Queue<GameObject>>();
        private static readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();
        private static readonly Dictionary<GameObject, string> _instanceToPrefab = new Dictionary<GameObject, string>();
        private static Transform _poolParent;

        #endregion

        #region Public Properties

        /// <summary>
        /// Whether object pooling is enabled. Default: true.
        /// When disabled, objects are created/destroyed normally.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Total number of objects currently in all pools.
        /// </summary>
        public static int TotalPooledCount
        {
            get
            {
                int count = 0;
                foreach (var pool in _pools.Values)
                    count += pool.Count;
                return count;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Pre-creates objects in the pool to avoid runtime instantiation hitches.
        /// Call this during loading screens for frequently used prefabs.
        /// </summary>
        /// <param name="prefabName">The name of the prefab in the Resources folder.</param>
        /// <param name="count">Number of instances to pre-create.</param>
        public static void Prewarm(string prefabName, int count)
        {
            if (!Enabled) return;
            if (string.IsNullOrEmpty(prefabName))
            {
                Debug.LogError("[EpicNet Pool] Prefab name cannot be null or empty");
                return;
            }
            if (count <= 0) return;

            EnsurePoolParent();
            var prefab = GetPrefab(prefabName);
            if (prefab == null) return;

            if (!_pools.ContainsKey(prefabName))
            {
                _pools[prefabName] = new Queue<GameObject>();
            }

            for (int i = 0; i < count; i++)
            {
                var obj = Object.Instantiate(prefab, _poolParent);
                obj.SetActive(false);
                obj.name = $"{prefabName} (Pooled)";
                _pools[prefabName].Enqueue(obj);
                _instanceToPrefab[obj] = prefabName;
            }

            Debug.Log($"[EpicNet Pool] Pre-warmed '{prefabName}' with {count} instances (total: {_pools[prefabName].Count})");
        }

        /// <summary>
        /// Gets an object from the pool or creates a new one if the pool is empty.
        /// The object is positioned and activated before being returned.
        /// </summary>
        /// <param name="prefabName">The name of the prefab in the Resources folder.</param>
        /// <param name="position">World position for the object.</param>
        /// <param name="rotation">World rotation for the object.</param>
        /// <returns>The spawned GameObject, or null if the prefab wasn't found.</returns>
        public static GameObject Get(string prefabName, Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                Debug.LogError("[EpicNet Pool] Prefab name cannot be null or empty");
                return null;
            }

            EnsurePoolParent();

            GameObject obj = null;

            // Try to get from pool with retry limit to handle destroyed objects
            int retries = MaxDequeueRetries;
            while (Enabled && _pools.TryGetValue(prefabName, out Queue<GameObject> pool) && pool.Count > 0 && retries-- > 0)
            {
                obj = pool.Dequeue();

                // Check if object was destroyed externally
                if (obj == null)
                {
                    continue;
                }

                obj.transform.SetPositionAndRotation(position, rotation);
                obj.SetActive(true);

                // Ensure object is tracked for return to pool
                if (!_instanceToPrefab.ContainsKey(obj))
                {
                    _instanceToPrefab[obj] = prefabName;
                }

                break;
            }

            // If we didn't get an object from pool, instantiate a new one
            if (obj == null)
            {
                var prefab = GetPrefab(prefabName);
                if (prefab == null) return null;

                obj = Object.Instantiate(prefab, position, rotation);
                obj.name = prefabName;
                _instanceToPrefab[obj] = prefabName;
            }

            // Reset physics state
            ResetPhysics(obj);

            // Notify poolable components
            NotifySpawnFromPool(obj);

            return obj;
        }

        /// <summary>
        /// Gets an object from the pool or creates a new one with a specified parent.
        /// </summary>
        /// <param name="prefabName">The name of the prefab in the Resources folder.</param>
        /// <param name="position">World position for the object.</param>
        /// <param name="rotation">World rotation for the object.</param>
        /// <param name="parent">Parent transform for the object.</param>
        /// <returns>The spawned GameObject, or null if the prefab wasn't found.</returns>
        public static GameObject Get(string prefabName, Vector3 position, Quaternion rotation, Transform parent)
        {
            var obj = Get(prefabName, position, rotation);
            if (obj != null && parent != null)
            {
                obj.transform.SetParent(parent, true);
            }
            return obj;
        }

        /// <summary>
        /// Returns an object to the pool for later reuse.
        /// If pooling is disabled or the object wasn't pooled, it will be destroyed.
        /// </summary>
        /// <param name="obj">The GameObject to return.</param>
        public static void Return(GameObject obj)
        {
            if (obj == null) return;

            // Notify poolable components before deactivation
            NotifyReturnToPool(obj);

            if (!Enabled || !_instanceToPrefab.TryGetValue(obj, out string prefabName))
            {
                Object.Destroy(obj);
                return;
            }

            EnsurePoolParent();

            if (!_pools.ContainsKey(prefabName))
            {
                _pools[prefabName] = new Queue<GameObject>();
            }

            obj.SetActive(false);
            obj.transform.SetParent(_poolParent);
            _pools[prefabName].Enqueue(obj);
        }

        /// <summary>
        /// Returns an object to the pool after a delay.
        /// </summary>
        /// <param name="obj">The GameObject to return.</param>
        /// <param name="delay">Delay in seconds before returning.</param>
        public static void Return(GameObject obj, float delay)
        {
            if (obj == null) return;
            if (delay <= 0)
            {
                Return(obj);
                return;
            }

            // Use coroutine via a helper MonoBehaviour
            var helper = obj.GetComponent<PoolReturnHelper>();
            if (helper == null)
            {
                helper = obj.AddComponent<PoolReturnHelper>();
            }
            helper.ReturnAfterDelay(delay);
        }

        /// <summary>
        /// Destroys all pooled objects and clears all pools.
        /// Call this when changing scenes or when pools are no longer needed.
        /// </summary>
        public static void ClearAll()
        {
            foreach (var pool in _pools.Values)
            {
                while (pool.Count > 0)
                {
                    var obj = pool.Dequeue();
                    if (obj != null)
                    {
                        Object.Destroy(obj);
                    }
                }
            }
            _pools.Clear();
            _instanceToPrefab.Clear();
            _prefabCache.Clear();

            if (_poolParent != null)
            {
                Object.Destroy(_poolParent.gameObject);
                _poolParent = null;
            }

            Debug.Log("[EpicNet Pool] All pools cleared");
        }

        /// <summary>
        /// Destroys all pooled objects for a specific prefab.
        /// </summary>
        /// <param name="prefabName">The prefab name to clear.</param>
        public static void Clear(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return;

            if (_pools.TryGetValue(prefabName, out Queue<GameObject> pool))
            {
                while (pool.Count > 0)
                {
                    var obj = pool.Dequeue();
                    if (obj != null)
                    {
                        _instanceToPrefab.Remove(obj);
                        Object.Destroy(obj);
                    }
                }
                _pools.Remove(prefabName);
            }

            _prefabCache.Remove(prefabName);
            Debug.Log($"[EpicNet Pool] Pool '{prefabName}' cleared");
        }

        /// <summary>
        /// Gets the number of available (inactive) objects in a pool.
        /// </summary>
        /// <param name="prefabName">The prefab name to check.</param>
        /// <returns>Number of pooled objects available.</returns>
        public static int GetPoolCount(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return 0;

            if (_pools.TryGetValue(prefabName, out Queue<GameObject> pool))
            {
                return pool.Count;
            }
            return 0;
        }

        /// <summary>
        /// Checks if a prefab has been cached (loaded from Resources).
        /// </summary>
        /// <param name="prefabName">The prefab name to check.</param>
        /// <returns>True if the prefab is cached.</returns>
        public static bool IsPrefabCached(string prefabName)
        {
            return !string.IsNullOrEmpty(prefabName) && _prefabCache.ContainsKey(prefabName);
        }

        #endregion

        #region Private Methods

        private static void EnsurePoolParent()
        {
            if (_poolParent == null)
            {
                var go = new GameObject(PoolParentName);
                Object.DontDestroyOnLoad(go);
                _poolParent = go.transform;
            }
        }

        private static GameObject GetPrefab(string prefabName)
        {
            if (_prefabCache.TryGetValue(prefabName, out GameObject cached))
            {
                return cached;
            }

            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Debug.LogError($"[EpicNet Pool] Prefab '{prefabName}' not found in Resources folder!");
                return null;
            }

            _prefabCache[prefabName] = prefab;
            return prefab;
        }

        private static void ResetPhysics(GameObject obj)
        {
            // Reset 3D rigidbody
            var rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Reset 2D rigidbody
            var rb2d = obj.GetComponent<Rigidbody2D>();
            if (rb2d != null)
            {
                rb2d.velocity = Vector2.zero;
                rb2d.angularVelocity = 0f;
            }
        }

        private static void NotifySpawnFromPool(GameObject obj)
        {
            var poolables = obj.GetComponents<IEpicPoolable>();
            foreach (var poolable in poolables)
            {
                try
                {
                    poolable.OnSpawnFromPool();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[EpicNet Pool] Error in OnSpawnFromPool: {e.Message}");
                }
            }
        }

        private static void NotifyReturnToPool(GameObject obj)
        {
            var poolables = obj.GetComponents<IEpicPoolable>();
            foreach (var poolable in poolables)
            {
                try
                {
                    poolable.OnReturnToPool();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[EpicNet Pool] Error in OnReturnToPool: {e.Message}");
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Interface for components that need to respond to pool spawn/return events.
    /// Implement this on MonoBehaviours attached to pooled objects.
    /// </summary>
    /// <example>
    /// <code>
    /// public class Projectile : MonoBehaviour, IEpicPoolable
    /// {
    ///     private TrailRenderer trail;
    ///
    ///     public void OnSpawnFromPool()
    ///     {
    ///         // Reset state when spawning
    ///         trail.Clear();
    ///     }
    ///
    ///     public void OnReturnToPool()
    ///     {
    ///         // Cleanup before returning
    ///         CancelInvoke();
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IEpicPoolable
    {
        /// <summary>
        /// Called when the object is retrieved from the pool.
        /// Use this to reset state and initialize the object.
        /// </summary>
        void OnSpawnFromPool();

        /// <summary>
        /// Called when the object is about to be returned to the pool.
        /// Use this for cleanup before the object is deactivated.
        /// </summary>
        void OnReturnToPool();
    }

    /// <summary>
    /// Internal helper component for delayed pool returns.
    /// </summary>
    internal class PoolReturnHelper : MonoBehaviour
    {
        public void ReturnAfterDelay(float delay)
        {
            StartCoroutine(ReturnCoroutine(delay));
        }

        private System.Collections.IEnumerator ReturnCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            EpicPool.Return(gameObject);
        }
    }
}
