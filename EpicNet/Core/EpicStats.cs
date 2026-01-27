using Epic.OnlineServices;
using Epic.OnlineServices.Stats;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Provides player statistics tracking using EOS Stats.
    /// Use this for tracking kills, deaths, wins, playtime, and other numerical stats.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stats must be defined in the EOS Developer Portal before use.
    /// Stats can be configured as SUM, MAX, MIN, or LATEST aggregation types.
    /// </para>
    /// <para>
    /// Common stat types:
    /// </para>
    /// <list type="bullet">
    /// <item>KILLS - Total kills (SUM)</item>
    /// <item>DEATHS - Total deaths (SUM)</item>
    /// <item>WINS - Total wins (SUM)</item>
    /// <item>HIGHEST_SCORE - Best score achieved (MAX)</item>
    /// <item>FASTEST_TIME - Best completion time (MIN)</item>
    /// <item>PLAYTIME - Total playtime in seconds (SUM)</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Increment a stat after a kill
    /// EpicStats.IngestStat("KILLS", 1);
    ///
    /// // Query player stats
    /// EpicStats.QueryStats(new[] { "KILLS", "DEATHS", "WINS" }, stats => {
    ///     foreach (var stat in stats) {
    ///         Debug.Log($"{stat.Key}: {stat.Value}");
    ///     }
    /// });
    /// </code>
    /// </example>
    public static class EpicStats
    {
        #region Events

        /// <summary>Fired when stats are queried successfully.</summary>
        public static event Action<Dictionary<string, int>> OnStatsQueried;

        /// <summary>Fired when a stat is ingested successfully.</summary>
        public static event Action<string, int> OnStatIngested;

        #endregion

        #region Private Fields

        private static StatsInterface _statsInterface;
        private static readonly Dictionary<string, int> _cachedStats = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _pendingStats = new Dictionary<string, int>();
        private static readonly object _statsLock = new object();

        #endregion

        #region Public Properties

        /// <summary>Whether the Stats interface is initialized.</summary>
        public static bool IsInitialized => _statsInterface != null;

        /// <summary>Whether to batch stat updates. Default: true.</summary>
        public static bool BatchUpdates { get; set; } = true;

        /// <summary>How often to flush batched stats in seconds. Default: 30.</summary>
        public static float BatchFlushInterval { get; set; } = 30f;

        #endregion

        #region Initialization

        private static float _lastFlushTime;

        /// <summary>
        /// Initializes the Stats interface. Called automatically by EpicNetwork.
        /// </summary>
        internal static void Initialize()
        {
            var platformInterface = EOSManager.Instance.GetEOSPlatformInterface();
            if (platformInterface == null)
            {
                Debug.LogError("[EpicNet Stats] Failed to get platform interface");
                return;
            }

            _statsInterface = platformInterface.GetStatsInterface();
            if (_statsInterface == null)
            {
                Debug.LogError("[EpicNet Stats] Failed to get Stats interface");
                return;
            }

            _lastFlushTime = Time.time;
            Debug.Log("[EpicNet Stats] Initialized");
        }

        /// <summary>
        /// Call this from Update to handle batched stat flushing.
        /// </summary>
        internal static void Update()
        {
            if (!BatchUpdates || !IsInitialized) return;

            if (Time.time - _lastFlushTime >= BatchFlushInterval)
            {
                FlushPendingStats();
                _lastFlushTime = Time.time;
            }
        }

        #endregion

        #region Ingest Stats

        /// <summary>
        /// Ingests a stat value. The value is added to the existing stat based on its aggregation type.
        /// </summary>
        /// <param name="statName">The stat name (must be defined in EOS Developer Portal).</param>
        /// <param name="amount">The value to ingest.</param>
        /// <param name="callback">Optional callback with success status.</param>
        public static void IngestStat(string statName, int amount, Action<bool> callback = null)
        {
            IngestStats(new Dictionary<string, int> { { statName, amount } }, callback);
        }

        /// <summary>
        /// Ingests multiple stat values at once.
        /// </summary>
        /// <param name="stats">Dictionary of stat names to values.</param>
        /// <param name="callback">Optional callback with success status.</param>
        public static void IngestStats(Dictionary<string, int> stats, Action<bool> callback = null)
        {
            if (stats == null || stats.Count == 0)
            {
                callback?.Invoke(true);
                return;
            }

            if (!IsInitialized)
            {
                Debug.LogError("[EpicNet Stats] Not initialized");
                callback?.Invoke(false);
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                Debug.LogError("[EpicNet Stats] Not logged in");
                callback?.Invoke(false);
                return;
            }

            if (BatchUpdates)
            {
                // Add to pending batch
                lock (_statsLock)
                {
                    foreach (var kvp in stats)
                    {
                        if (_pendingStats.ContainsKey(kvp.Key))
                            _pendingStats[kvp.Key] += kvp.Value;
                        else
                            _pendingStats[kvp.Key] = kvp.Value;

                        // Update cache
                        if (_cachedStats.ContainsKey(kvp.Key))
                            _cachedStats[kvp.Key] += kvp.Value;
                        else
                            _cachedStats[kvp.Key] = kvp.Value;
                    }
                }

                callback?.Invoke(true);
                return;
            }

            // Direct ingest
            IngestStatsInternal(localUserId, stats, callback);
        }

        private static void IngestStatsInternal(ProductUserId userId, Dictionary<string, int> stats, Action<bool> callback)
        {
            var ingestStats = new IngestData[stats.Count];
            int index = 0;

            foreach (var kvp in stats)
            {
                ingestStats[index++] = new IngestData
                {
                    StatName = kvp.Key,
                    IngestAmount = kvp.Value
                };
            }

            var options = new IngestStatOptions
            {
                LocalUserId = userId,
                TargetUserId = userId,
                Stats = ingestStats
            };

            _statsInterface.IngestStat(ref options, null, (ref IngestStatCompleteCallbackInfo info) =>
            {
                bool success = info.ResultCode == Result.Success;

                if (success)
                {
                    foreach (var kvp in stats)
                    {
                        Debug.Log($"[EpicNet Stats] Ingested: {kvp.Key} += {kvp.Value}");
                        OnStatIngested?.Invoke(kvp.Key, kvp.Value);
                    }
                }
                else
                {
                    Debug.LogError($"[EpicNet Stats] Failed to ingest stats: {info.ResultCode}");
                }

                callback?.Invoke(success);
            });
        }

        /// <summary>
        /// Flushes any pending batched stats to the server.
        /// </summary>
        public static void FlushPendingStats(Action<bool> callback = null)
        {
            Dictionary<string, int> statsToFlush;

            lock (_statsLock)
            {
                if (_pendingStats.Count == 0)
                {
                    callback?.Invoke(true);
                    return;
                }

                statsToFlush = new Dictionary<string, int>(_pendingStats);
                _pendingStats.Clear();
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                callback?.Invoke(false);
                return;
            }

            IngestStatsInternal(localUserId, statsToFlush, callback);
        }

        #endregion

        #region Query Stats

        /// <summary>
        /// Queries the current player's stats.
        /// </summary>
        /// <param name="statNames">Array of stat names to query.</param>
        /// <param name="callback">Callback with dictionary of stat values.</param>
        public static void QueryStats(string[] statNames, Action<Dictionary<string, int>> callback)
        {
            QueryStats(EpicNetwork.LocalPlayer?.UserId, statNames, callback);
        }

        /// <summary>
        /// Queries another player's stats.
        /// </summary>
        /// <param name="targetUserId">The user to query stats for.</param>
        /// <param name="statNames">Array of stat names to query.</param>
        /// <param name="callback">Callback with dictionary of stat values.</param>
        public static void QueryStats(ProductUserId targetUserId, string[] statNames, Action<Dictionary<string, int>> callback)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[EpicNet Stats] Not initialized");
                callback?.Invoke(new Dictionary<string, int>());
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null || targetUserId == null)
            {
                callback?.Invoke(new Dictionary<string, int>());
                return;
            }

            var options = new QueryStatsOptions
            {
                LocalUserId = localUserId,
                TargetUserId = targetUserId,
                StatNames = statNames
                    .Select(s => new Utf8String(s))
                    .ToArray()
            };

            _statsInterface.QueryStats(ref options, null, (ref OnQueryStatsCompleteCallbackInfo info) =>
            {
                var results = new Dictionary<string, int>();

                if (info.ResultCode == Result.Success)
                {
                    var countOptions = new GetStatCountOptions
                    {
                        TargetUserId = targetUserId
                    };

                    uint statCount = _statsInterface.GetStatsCount(ref countOptions);

                    for (uint i = 0; i < statCount; i++)
                    {
                        var copyOptions = new CopyStatByIndexOptions
                        {
                            TargetUserId = targetUserId,
                            StatIndex = i
                        };

                        var result = _statsInterface.CopyStatByIndex(ref copyOptions, out Stat? stat);
                        if (result == Result.Success && stat.HasValue)
                        {
                            results[stat.Value.Name] = stat.Value.Value;
                        }
                    }

                    // Update cache if querying self
                    if (targetUserId.Equals(localUserId))
                    {
                        lock (_statsLock)
                        {
                            foreach (var kvp in results)
                            {
                                _cachedStats[kvp.Key] = kvp.Value;
                            }
                        }
                    }

                    Debug.Log($"[EpicNet Stats] Queried {results.Count} stats");
                    OnStatsQueried?.Invoke(results);
                }
                else
                {
                    Debug.LogError($"[EpicNet Stats] Failed to query stats: {info.ResultCode}");
                }

                callback?.Invoke(results);
            });
        }

        /// <summary>
        /// Gets a cached stat value. Returns 0 if not cached.
        /// </summary>
        public static int GetCachedStat(string statName)
        {
            lock (_statsLock)
            {
                return _cachedStats.TryGetValue(statName, out int value) ? value : 0;
            }
        }

        /// <summary>
        /// Gets all cached stats.
        /// </summary>
        public static Dictionary<string, int> GetAllCachedStats()
        {
            lock (_statsLock)
            {
                return new Dictionary<string, int>(_cachedStats);
            }
        }

        #endregion
    }
}
