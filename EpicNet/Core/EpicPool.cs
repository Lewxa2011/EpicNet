using UnityEngine;
using System.Collections.Generic;

namespace EpicNet
{
    /// <summary>
    /// Object pool for networked objects to reduce instantiation overhead
    /// </summary>
    public static class EpicPool
    {
        private static Dictionary<string, Queue<GameObject>> _pools = new Dictionary<string, Queue<GameObject>>();
        private static Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();
        private static Transform _poolParent;
        private static Dictionary<GameObject, string> _instanceToPrefab = new Dictionary<GameObject, string>();

        /// <summary>
        /// Whether pooling is enabled (default true)
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Pre-warm a pool with a number of instances
        /// </summary>
        public static void Prewarm(string prefabName, int count)
        {
            if (!Enabled) return;

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
                _pools[prefabName].Enqueue(obj);
                _instanceToPrefab[obj] = prefabName;
            }

            Debug.Log($"EpicNet: Pre-warmed pool '{prefabName}' with {count} instances");
        }

        /// <summary>
        /// Get an object from the pool or instantiate a new one
        /// </summary>
        public static GameObject Get(string prefabName, Vector3 position, Quaternion rotation)
        {
            EnsurePoolParent();

            GameObject obj = null;

            if (Enabled && _pools.TryGetValue(prefabName, out Queue<GameObject> pool) && pool.Count > 0)
            {
                obj = pool.Dequeue();

                // Check if object was destroyed externally
                if (obj == null)
                {
                    return Get(prefabName, position, rotation);
                }

                obj.transform.position = position;
                obj.transform.rotation = rotation;
                obj.SetActive(true);
            }
            else
            {
                var prefab = GetPrefab(prefabName);
                if (prefab == null) return null;

                obj = Object.Instantiate(prefab, position, rotation);
                _instanceToPrefab[obj] = prefabName;
            }

            // Reset rigidbody if present
            var rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            var rb2d = obj.GetComponent<Rigidbody2D>();
            if (rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
                rb2d.angularVelocity = 0f;
            }

            // Notify poolable components
            var poolables = obj.GetComponents<IEpicPoolable>();
            foreach (var poolable in poolables)
            {
                poolable.OnSpawnFromPool();
            }

            return obj;
        }

        /// <summary>
        /// Return an object to the pool
        /// </summary>
        public static void Return(GameObject obj)
        {
            if (obj == null) return;

            // Notify poolable components
            var poolables = obj.GetComponents<IEpicPoolable>();
            foreach (var poolable in poolables)
            {
                poolable.OnReturnToPool();
            }

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
        /// Clear all pools
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

            Debug.Log("EpicNet: All pools cleared");
        }

        /// <summary>
        /// Clear a specific pool
        /// </summary>
        public static void Clear(string prefabName)
        {
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

            Debug.Log($"EpicNet: Pool '{prefabName}' cleared");
        }

        /// <summary>
        /// Get the number of available objects in a pool
        /// </summary>
        public static int GetPoolCount(string prefabName)
        {
            if (_pools.TryGetValue(prefabName, out Queue<GameObject> pool))
            {
                return pool.Count;
            }
            return 0;
        }

        private static void EnsurePoolParent()
        {
            if (_poolParent == null)
            {
                var go = new GameObject("[EpicNet Pool]");
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
                Debug.LogError($"EpicNet: Prefab '{prefabName}' not found in Resources!");
                return null;
            }

            _prefabCache[prefabName] = prefab;
            return prefab;
        }
    }

    /// <summary>
    /// Interface for objects that need to respond to pool events
    /// </summary>
    public interface IEpicPoolable
    {
        void OnSpawnFromPool();
        void OnReturnToPool();
    }
}
