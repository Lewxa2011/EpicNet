using Epic.OnlineServices;
using Epic.OnlineServices.Achievements;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Provides achievement tracking and unlocking using EOS Achievements.
    /// Use this for tracking player accomplishments and milestones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Achievements must be configured in the EOS Developer Portal.
    /// They can be linked to Stats for automatic unlocking based on progress.
    /// </para>
    /// <para>
    /// Achievement types:
    /// </para>
    /// <list type="bullet">
    /// <item>Simple achievements - Unlocked manually or via a single stat threshold</item>
    /// <item>Progressive achievements - Show progress towards completion</item>
    /// <item>Hidden achievements - Description hidden until unlocked</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Unlock an achievement
    /// EpicAchievements.Unlock("FIRST_WIN", success => {
    ///     if (success) ShowAchievementPopup("First Win!");
    /// });
    ///
    /// // Query all achievements
    /// EpicAchievements.QueryAchievements(achievements => {
    ///     foreach (var ach in achievements) {
    ///         Debug.Log($"{ach.Id}: {(ach.IsUnlocked ? "Unlocked" : $"{ach.Progress:P0}")}");
    ///     }
    /// });
    /// </code>
    /// </example>
    public static class EpicAchievements
    {
        #region Events

        /// <summary>Fired when an achievement is unlocked.</summary>
        public static event Action<string, AchievementData> OnAchievementUnlocked;

        /// <summary>Fired when achievement progress is updated.</summary>
        public static event Action<string, float> OnAchievementProgress;

        /// <summary>Fired when achievements are queried.</summary>
        public static event Action<List<AchievementData>> OnAchievementsQueried;

        #endregion

        #region Private Fields

        private static AchievementsInterface _achievementsInterface;
        private static readonly Dictionary<string, AchievementData> _cachedAchievements = new Dictionary<string, AchievementData>();
        private static readonly Dictionary<string, AchievementDefinition> _definitions = new Dictionary<string, AchievementDefinition>();
        private static readonly object _cacheLock = new object();
        private static ulong _notificationId;

        #endregion

        #region Public Properties

        /// <summary>Whether the Achievements interface is initialized.</summary>
        public static bool IsInitialized => _achievementsInterface != null;

        /// <summary>Total number of achievements defined.</summary>
        public static int TotalAchievements
        {
            get
            {
                lock (_cacheLock)
                {
                    return _definitions.Count;
                }
            }
        }

        /// <summary>Number of unlocked achievements.</summary>
        public static int UnlockedCount
        {
            get
            {
                lock (_cacheLock)
                {
                    int count = 0;
                    foreach (var ach in _cachedAchievements.Values)
                    {
                        if (ach.IsUnlocked) count++;
                    }
                    return count;
                }
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the Achievements interface. Called automatically by EpicNetwork.
        /// </summary>
        internal static void Initialize()
        {
            var platformInterface = EOSManager.Instance.GetEOSPlatformInterface();
            if (platformInterface == null)
            {
                Debug.LogError("[EpicNet Achievements] Failed to get platform interface");
                return;
            }

            _achievementsInterface = platformInterface.GetAchievementsInterface();
            if (_achievementsInterface == null)
            {
                Debug.LogError("[EpicNet Achievements] Failed to get Achievements interface");
                return;
            }

            // Subscribe to unlock notifications
            var notifyOptions = new AddNotifyAchievementsUnlockedV2Options();
            _notificationId = _achievementsInterface.AddNotifyAchievementsUnlockedV2(ref notifyOptions, null, OnAchievementUnlockedNotification);

            Debug.Log("[EpicNet Achievements] Initialized");
        }

        /// <summary>
        /// Cleans up resources. Called by EpicNetwork.
        /// </summary>
        internal static void Shutdown()
        {
            if (_achievementsInterface != null && _notificationId != 0)
            {
                _achievementsInterface.RemoveNotifyAchievementsUnlocked(_notificationId);
                _notificationId = 0;
            }
        }

        private static void OnAchievementUnlockedNotification(ref OnAchievementsUnlockedCallbackV2Info info)
        {
            if (info.AchievementId != null)
            {
                var achievementId = info.AchievementId;
                Debug.Log($"[EpicNet Achievements] Achievement unlocked: {achievementId}");

                lock (_cacheLock)
                {
                    if (_cachedAchievements.TryGetValue(achievementId, out var data))
                    {
                        data.IsUnlocked = true;
                        data.Progress = 1f;
                        data.UnlockTime = DateTime.UtcNow;
                        _cachedAchievements[achievementId] = data;
                        OnAchievementUnlocked?.Invoke(achievementId, data);
                    }
                }
            }
        }

        #endregion

        #region Query Achievements

        /// <summary>
        /// Queries all achievement definitions from the server.
        /// Call this once at startup to cache achievement definitions.
        /// </summary>
        /// <param name="callback">Callback when definitions are loaded.</param>
        public static void QueryDefinitions(Action<bool> callback = null)
        {
            if (!IsInitialized)
            {
                callback?.Invoke(false);
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                callback?.Invoke(false);
                return;
            }

            var options = new QueryDefinitionsOptions
            {
                LocalUserId = localUserId
            };

            _achievementsInterface.QueryDefinitions(ref options, null, (ref OnQueryDefinitionsCompleteCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    var countOptions = new GetAchievementDefinitionCountOptions();
                    uint count = _achievementsInterface.GetAchievementDefinitionCount(ref countOptions);

                    lock (_cacheLock)
                    {
                        _definitions.Clear();

                        for (uint i = 0; i < count; i++)
                        {
                            var copyOptions = new CopyAchievementDefinitionV2ByIndexOptions
                            {
                                AchievementIndex = i
                            };

                            var result = _achievementsInterface.CopyAchievementDefinitionV2ByIndex(ref copyOptions, out DefinitionV2? def);
                            if (result == Result.Success && def.HasValue)
                            {
                                _definitions[def.Value.AchievementId] = new AchievementDefinition
                                {
                                    Id = def.Value.AchievementId,
                                    DisplayName = def.Value.UnlockedDisplayName,
                                    Description = def.Value.UnlockedDescription,
                                    LockedDisplayName = def.Value.LockedDisplayName,
                                    LockedDescription = def.Value.LockedDescription,
                                    IconUrl = def.Value.UnlockedIconURL,
                                    LockedIconUrl = def.Value.LockedIconURL,
                                    IsHidden = def.Value.IsHidden
                                };
                            }
                        }
                    }

                    Debug.Log($"[EpicNet Achievements] Loaded {count} achievement definitions");
                    callback?.Invoke(true);
                }
                else
                {
                    Debug.LogError($"[EpicNet Achievements] Failed to query definitions: {info.ResultCode}");
                    callback?.Invoke(false);
                }
            });
        }

        /// <summary>
        /// Queries the player's achievement progress.
        /// </summary>
        /// <param name="callback">Callback with list of achievement data.</param>
        public static void QueryAchievements(Action<List<AchievementData>> callback)
        {
            if (!IsInitialized)
            {
                callback?.Invoke(new List<AchievementData>());
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                callback?.Invoke(new List<AchievementData>());
                return;
            }

            var options = new QueryPlayerAchievementsOptions
            {
                LocalUserId = localUserId,
                TargetUserId = localUserId
            };

            _achievementsInterface.QueryPlayerAchievements(ref options, null, (ref OnQueryPlayerAchievementsCompleteCallbackInfo info) =>
            {
                var achievements = new List<AchievementData>();

                if (info.ResultCode == Result.Success)
                {
                    var countOptions = new GetPlayerAchievementCountOptions
                    {
                        UserId = localUserId
                    };

                    uint count = _achievementsInterface.GetPlayerAchievementCount(ref countOptions);

                    lock (_cacheLock)
                    {
                        for (uint i = 0; i < count; i++)
                        {
                            var copyOptions = new CopyPlayerAchievementByIndexOptions
                            {
                                LocalUserId = localUserId,
                                TargetUserId = localUserId,
                                AchievementIndex = i
                            };

                            var result = _achievementsInterface.CopyPlayerAchievementByIndex(ref copyOptions, out PlayerAchievement? playerAch);
                            if (result == Result.Success && playerAch.HasValue)
                            {
                                var data = new AchievementData
                                {
                                    Id = playerAch.Value.AchievementId,
                                    Progress = (float)playerAch.Value.Progress,
                                    IsUnlocked = playerAch.Value.UnlockTime.HasValue,
                                    UnlockTime = playerAch.Value.UnlockTime.HasValue
                                        ? playerAch.Value.UnlockTime.Value.DateTime
                                        : (DateTime?)null
                                };

                                // Add definition info if available
                                if (_definitions.TryGetValue(data.Id, out var def))
                                {
                                    data.DisplayName = data.IsUnlocked ? def.DisplayName : def.LockedDisplayName;
                                    data.Description = data.IsUnlocked ? def.Description : def.LockedDescription;
                                    data.IconUrl = data.IsUnlocked ? def.IconUrl : def.LockedIconUrl;
                                    data.IsHidden = def.IsHidden;
                                }

                                _cachedAchievements[data.Id] = data;
                                achievements.Add(data);
                            }
                        }
                    }

                    Debug.Log($"[EpicNet Achievements] Queried {achievements.Count} achievements");
                    OnAchievementsQueried?.Invoke(achievements);
                }
                else
                {
                    Debug.LogError($"[EpicNet Achievements] Failed to query achievements: {info.ResultCode}");
                }

                callback?.Invoke(achievements);
            });
        }

        #endregion

        #region Unlock Achievements

        /// <summary>
        /// Unlocks an achievement.
        /// </summary>
        /// <param name="achievementId">The achievement ID to unlock.</param>
        /// <param name="callback">Optional callback with success status.</param>
        public static void Unlock(string achievementId, Action<bool> callback = null)
        {
            if (string.IsNullOrEmpty(achievementId))
            {
                callback?.Invoke(false);
                return;
            }

            if (!IsInitialized)
            {
                Debug.LogError("[EpicNet Achievements] Not initialized");
                callback?.Invoke(false);
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                callback?.Invoke(false);
                return;
            }

            // Check if already unlocked
            lock (_cacheLock)
            {
                if (_cachedAchievements.TryGetValue(achievementId, out var cached) && cached.IsUnlocked)
                {
                    Debug.Log($"[EpicNet Achievements] Already unlocked: {achievementId}");
                    callback?.Invoke(true);
                    return;
                }
            }

            var options = new UnlockAchievementsOptions
            {
                UserId = localUserId,
                AchievementIds = new Utf8String[] { achievementId }
            };

            _achievementsInterface.UnlockAchievements(ref options, null, (ref OnUnlockAchievementsCompleteCallbackInfo info) =>
            {
                bool success = info.ResultCode == Result.Success;

                if (success)
                {
                    Debug.Log($"[EpicNet Achievements] Unlocked: {achievementId}");

                    lock (_cacheLock)
                    {
                        if (_cachedAchievements.TryGetValue(achievementId, out var data))
                        {
                            data.IsUnlocked = true;
                            data.Progress = 1f;
                            data.UnlockTime = DateTime.UtcNow;
                            _cachedAchievements[achievementId] = data;
                        }
                    }
                }
                else
                {
                    Debug.LogError($"[EpicNet Achievements] Failed to unlock {achievementId}: {info.ResultCode}");
                }

                callback?.Invoke(success);
            });
        }

        /// <summary>
        /// Unlocks multiple achievements at once.
        /// </summary>
        /// <param name="achievementIds">Array of achievement IDs to unlock.</param>
        /// <param name="callback">Optional callback with success status.</param>
        public static void UnlockMultiple(string[] achievementIds, Action<bool> callback = null)
        {
            if (achievementIds == null || achievementIds.Length == 0)
            {
                callback?.Invoke(true);
                return;
            }

            if (!IsInitialized)
            {
                callback?.Invoke(false);
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                callback?.Invoke(false);
                return;
            }

            var utf8Ids = new Utf8String[achievementIds.Length];
            for (int i = 0; i < achievementIds.Length; i++)
            {
                utf8Ids[i] = achievementIds[i];
            }

            var options = new UnlockAchievementsOptions
            {
                UserId = localUserId,
                AchievementIds = utf8Ids
            };

            _achievementsInterface.UnlockAchievements(ref options, null, (ref OnUnlockAchievementsCompleteCallbackInfo info) =>
            {
                bool success = info.ResultCode == Result.Success;

                if (success)
                {
                    Debug.Log($"[EpicNet Achievements] Unlocked {achievementIds.Length} achievements");
                }
                else
                {
                    Debug.LogError($"[EpicNet Achievements] Failed to unlock achievements: {info.ResultCode}");
                }

                callback?.Invoke(success);
            });
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets a cached achievement by ID.
        /// </summary>
        public static AchievementData? GetAchievement(string achievementId)
        {
            lock (_cacheLock)
            {
                if (_cachedAchievements.TryGetValue(achievementId, out var data))
                {
                    return data;
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if an achievement is unlocked (from cache).
        /// </summary>
        public static bool IsUnlocked(string achievementId)
        {
            lock (_cacheLock)
            {
                if (_cachedAchievements.TryGetValue(achievementId, out var data))
                {
                    return data.IsUnlocked;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets all cached achievements.
        /// </summary>
        public static List<AchievementData> GetAllAchievements()
        {
            lock (_cacheLock)
            {
                return new List<AchievementData>(_cachedAchievements.Values);
            }
        }

        /// <summary>
        /// Gets the achievement definition by ID.
        /// </summary>
        public static AchievementDefinition? GetDefinition(string achievementId)
        {
            lock (_cacheLock)
            {
                if (_definitions.TryGetValue(achievementId, out var def))
                {
                    return def;
                }
            }
            return null;
        }

        #endregion
    }

    /// <summary>
    /// Achievement progress data for a player.
    /// </summary>
    [Serializable]
    public struct AchievementData
    {
        /// <summary>The achievement ID.</summary>
        public string Id;

        /// <summary>Display name.</summary>
        public string DisplayName;

        /// <summary>Description text.</summary>
        public string Description;

        /// <summary>URL to the icon image.</summary>
        public string IconUrl;

        /// <summary>Progress from 0 to 1.</summary>
        public float Progress;

        /// <summary>Whether the achievement is unlocked.</summary>
        public bool IsUnlocked;

        /// <summary>When the achievement was unlocked.</summary>
        public DateTime? UnlockTime;

        /// <summary>Whether the achievement is hidden until unlocked.</summary>
        public bool IsHidden;
    }

    /// <summary>
    /// Achievement definition from the server.
    /// </summary>
    public struct AchievementDefinition
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string LockedDisplayName;
        public string LockedDescription;
        public string IconUrl;
        public string LockedIconUrl;
        public bool IsHidden;
    }
}
