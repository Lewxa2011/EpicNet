using Epic.OnlineServices;
using Epic.OnlineServices.Leaderboards;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Provides leaderboard functionality using EOS Leaderboards.
    /// Use this for competitive rankings, high scores, and seasonal standings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Leaderboards are automatically populated from Stats. Configure leaderboards
    /// in the EOS Developer Portal and link them to stats.
    /// </para>
    /// <para>
    /// Leaderboards support:
    /// </para>
    /// <list type="bullet">
    /// <item>Global rankings</item>
    /// <item>Friend rankings</item>
    /// <item>Rankings around a specific player</item>
    /// <item>Time-based leaderboards (daily, weekly, seasonal)</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get top 10 players
    /// EpicLeaderboards.GetLeaderboard("HighScores", 0, 10, entries => {
    ///     foreach (var entry in entries) {
    ///         Debug.Log($"#{entry.Rank} {entry.DisplayName}: {entry.Score}");
    ///     }
    /// });
    ///
    /// // Get ranks around the local player
    /// EpicLeaderboards.GetLeaderboardAroundPlayer("HighScores", 5, entries => {
    ///     // Shows 5 players above and 5 below the local player
    /// });
    /// </code>
    /// </example>
    public static class EpicLeaderboards
    {
        #region Events

        /// <summary>Fired when leaderboard data is retrieved.</summary>
        public static event Action<string, List<LeaderboardEntry>> OnLeaderboardReceived;

        /// <summary>Fired when the player's rank is retrieved.</summary>
        public static event Action<string, LeaderboardEntry> OnPlayerRankReceived;

        #endregion

        #region Private Fields

        private static LeaderboardsInterface _leaderboardsInterface;
        private static readonly Dictionary<string, List<LeaderboardEntry>> _cachedLeaderboards = new Dictionary<string, List<LeaderboardEntry>>();
        private static readonly object _cacheLock = new object();

        #endregion

        #region Public Properties

        /// <summary>Whether the Leaderboards interface is initialized.</summary>
        public static bool IsInitialized => _leaderboardsInterface != null;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the Leaderboards interface. Called automatically by EpicNetwork.
        /// </summary>
        internal static void Initialize()
        {
            var platformInterface = EOSManager.Instance.GetEOSPlatformInterface();
            if (platformInterface == null)
            {
                Debug.LogError("[EpicNet Leaderboards] Failed to get platform interface");
                return;
            }

            _leaderboardsInterface = platformInterface.GetLeaderboardsInterface();
            if (_leaderboardsInterface == null)
            {
                Debug.LogError("[EpicNet Leaderboards] Failed to get Leaderboards interface");
                return;
            }

            Debug.Log("[EpicNet Leaderboards] Initialized");
        }

        #endregion

        #region Query Leaderboards

        /// <summary>
        /// Gets leaderboard entries by rank range.
        /// </summary>
        /// <param name="leaderboardId">The leaderboard ID from EOS Developer Portal.</param>
        /// <param name="startRank">Starting rank (0-based).</param>
        /// <param name="count">Number of entries to retrieve (max 1000).</param>
        /// <param name="callback">Callback with list of entries.</param>
        public static void GetLeaderboard(string leaderboardId, int startRank, int count, Action<List<LeaderboardEntry>> callback)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[EpicNet Leaderboards] Not initialized");
                callback?.Invoke(new List<LeaderboardEntry>());
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                callback?.Invoke(new List<LeaderboardEntry>());
                return;
            }

            // First query the leaderboard definition
            var queryDefOptions = new QueryLeaderboardDefinitionsOptions
            {
                LocalUserId = localUserId
            };

            _leaderboardsInterface.QueryLeaderboardDefinitions(ref queryDefOptions, null, (ref OnQueryLeaderboardDefinitionsCompleteCallbackInfo defInfo) =>
            {
                if (defInfo.ResultCode != Result.Success)
                {
                    Debug.LogError($"[EpicNet Leaderboards] Failed to query definitions: {defInfo.ResultCode}");
                    callback?.Invoke(new List<LeaderboardEntry>());
                    return;
                }

                // Now query the ranks
                var queryOptions = new QueryLeaderboardRanksOptions
                {
                    LocalUserId = localUserId,
                    LeaderboardId = leaderboardId
                };

                _leaderboardsInterface.QueryLeaderboardRanks(ref queryOptions, null, (ref OnQueryLeaderboardRanksCompleteCallbackInfo info) =>
                {
                    var entries = new List<LeaderboardEntry>();

                    if (info.ResultCode == Result.Success)
                    {
                        var countOptions = new GetLeaderboardRecordCountOptions();
                        uint recordCount = _leaderboardsInterface.GetLeaderboardRecordCount(ref countOptions);

                        int endIndex = Math.Min(startRank + count, (int)recordCount);

                        for (int i = startRank; i < endIndex; i++)
                        {
                            var copyOptions = new CopyLeaderboardRecordByIndexOptions
                            {
                                LeaderboardRecordIndex = (uint)i
                            };

                            var result = _leaderboardsInterface.CopyLeaderboardRecordByIndex(ref copyOptions, out LeaderboardRecord? record);
                            if (result == Result.Success && record.HasValue)
                            {
                                entries.Add(new LeaderboardEntry
                                {
                                    UserId = record.Value.UserId,
                                    DisplayName = record.Value.UserDisplayName ?? $"Player_{i + 1}",
                                    Rank = (int)record.Value.Rank,
                                    Score = record.Value.Score
                                });
                            }
                        }

                        // Cache results
                        lock (_cacheLock)
                        {
                            _cachedLeaderboards[leaderboardId] = entries;
                        }

                        Debug.Log($"[EpicNet Leaderboards] Retrieved {entries.Count} entries for {leaderboardId}");
                        OnLeaderboardReceived?.Invoke(leaderboardId, entries);
                    }
                    else
                    {
                        Debug.LogError($"[EpicNet Leaderboards] Failed to query ranks: {info.ResultCode}");
                    }

                    callback?.Invoke(entries);
                });
            });
        }

        /// <summary>
        /// Gets leaderboard entries around a specific player.
        /// </summary>
        /// <param name="leaderboardId">The leaderboard ID.</param>
        /// <param name="range">Number of entries above and below the player.</param>
        /// <param name="callback">Callback with list of entries.</param>
        public static void GetLeaderboardAroundPlayer(string leaderboardId, int range, Action<List<LeaderboardEntry>> callback)
        {
            GetLeaderboardAroundPlayer(leaderboardId, EpicNetwork.LocalPlayer?.UserId, range, callback);
        }

        /// <summary>
        /// Gets leaderboard entries around a specific user.
        /// </summary>
        /// <param name="leaderboardId">The leaderboard ID.</param>
        /// <param name="targetUserId">The user to center the results on.</param>
        /// <param name="range">Number of entries above and below the user.</param>
        /// <param name="callback">Callback with list of entries.</param>
        public static void GetLeaderboardAroundPlayer(string leaderboardId, ProductUserId targetUserId, int range, Action<List<LeaderboardEntry>> callback)
        {
            if (!IsInitialized || targetUserId == null)
            {
                callback?.Invoke(new List<LeaderboardEntry>());
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                callback?.Invoke(new List<LeaderboardEntry>());
                return;
            }

            var queryOptions = new QueryLeaderboardUserScoresOptions
            {
                LocalUserId = localUserId,
                UserIds = new ProductUserId[] { targetUserId },
                StatInfo = null // Will query all stats for the leaderboard
            };

            _leaderboardsInterface.QueryLeaderboardUserScores(ref queryOptions, null, (ref OnQueryLeaderboardUserScoresCompleteCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    // Get the user's record to find their rank
                    var copyOptions = new CopyLeaderboardUserScoreByIndexOptions
                    {
                        LeaderboardUserScoreIndex = 0,
                        StatName = null
                    };

                    var result = _leaderboardsInterface.CopyLeaderboardUserScoreByIndex(ref copyOptions, out LeaderboardUserScore? userScore);

                    if (result == Result.Success && userScore.HasValue)
                    {
                        GetLeaderboard(leaderboardId, 0, 1000, entries =>
                        {
                            int index = entries.FindIndex(e => e.UserId == targetUserId);
                            if (index < 0)
                            {
                                callback?.Invoke(new List<LeaderboardEntry>());
                                return;
                            }

                            int start = Math.Max(0, index - range);
                            int count = Math.Min(entries.Count - start, range * 2 + 1);

                            callback?.Invoke(entries.GetRange(start, count));
                        });
                        
                        return;
                    }
                }

                Debug.LogWarning($"[EpicNet Leaderboards] Could not find user rank for {leaderboardId}");
                callback?.Invoke(new List<LeaderboardEntry>());
            });
        }

        /// <summary>
        /// Gets the local player's rank on a leaderboard.
        /// </summary>
        /// <param name="leaderboardId">The leaderboard ID.</param>
        /// <param name="callback">Callback with the player's entry, or null if not ranked.</param>
        public static void GetPlayerRank(string leaderboardId, Action<LeaderboardEntry?> callback)
        {
            GetPlayerRank(leaderboardId, EpicNetwork.LocalPlayer?.UserId, callback);
        }

        /// <summary>
        /// Gets a specific player's rank on a leaderboard.
        /// </summary>
        /// <param name="leaderboardId">The leaderboard ID.</param>
        /// <param name="targetUserId">The user to get the rank for.</param>
        /// <param name="callback">Callback with the player's entry, or null if not ranked.</param>
        public static void GetPlayerRank(string leaderboardId, ProductUserId targetUserId, Action<LeaderboardEntry?> callback)
        {
            if (!IsInitialized || targetUserId == null)
            {
                callback?.Invoke(null);
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                callback?.Invoke(null);
                return;
            }

            var queryOptions = new QueryLeaderboardUserScoresOptions
            {
                LocalUserId = localUserId,
                UserIds = new ProductUserId[] { targetUserId },
                StatInfo = null
            };

            _leaderboardsInterface.QueryLeaderboardUserScores(ref queryOptions, null, (ref OnQueryLeaderboardUserScoresCompleteCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    var copyOptions = new CopyLeaderboardUserScoreByUserIdOptions
                    {
                        UserId = targetUserId,
                        StatName = null
                    };

                    var result = _leaderboardsInterface.CopyLeaderboardUserScoreByUserId(ref copyOptions, out LeaderboardUserScore? userScore);

                    if (result == Result.Success && userScore.HasValue)
                    {
                        var entry = new LeaderboardEntry
                        {
                            UserId = targetUserId,
                            DisplayName = EpicNetwork.LocalPlayer?.NickName ?? "Unknown",
                            Rank = userScore.Value.Score,
                            Score = userScore.Value.Score
                        };

                        OnPlayerRankReceived?.Invoke(leaderboardId, entry);
                        callback?.Invoke(entry);
                        return;
                    }
                }

                callback?.Invoke(null);
            });
        }

        /// <summary>
        /// Gets cached leaderboard data if available.
        /// </summary>
        public static List<LeaderboardEntry> GetCachedLeaderboard(string leaderboardId)
        {
            lock (_cacheLock)
            {
                if (_cachedLeaderboards.TryGetValue(leaderboardId, out var entries))
                {
                    return new List<LeaderboardEntry>(entries);
                }
            }
            return new List<LeaderboardEntry>();
        }

        #endregion
    }

    /// <summary>
    /// Represents an entry in a leaderboard.
    /// </summary>
    [Serializable]
    public struct LeaderboardEntry
    {
        /// <summary>The user's EOS Product User ID.</summary>
        public ProductUserId UserId;

        /// <summary>The display name of the user.</summary>
        public string DisplayName;

        /// <summary>The user's rank (1-based).</summary>
        public int Rank;

        /// <summary>The user's score.</summary>
        public int Score;

        /// <summary>
        /// Returns a formatted string of the entry.
        /// </summary>
        public override string ToString()
        {
            return $"#{Rank} {DisplayName}: {Score}";
        }
    }
}
