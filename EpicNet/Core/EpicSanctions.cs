using Epic.OnlineServices;
using Epic.OnlineServices.Sanctions;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Provides access to EOS Sanctions for managing player bans and restrictions.
    /// Use this to check if players are banned and enforce game-specific penalties.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sanctions are managed through the EOS Developer Portal or backend APIs.
    /// This client-side system can only query sanctions, not create them.
    /// </para>
    /// <para>
    /// Common sanction types:
    /// </para>
    /// <list type="bullet">
    /// <item>PERMANENT_BAN - Player is permanently banned</item>
    /// <item>TEMPORARY_BAN - Player is banned for a duration</item>
    /// <item>CHAT_MUTE - Player cannot use chat</item>
    /// <item>COMPETITIVE_BAN - Player cannot play ranked modes</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Check if local player has any active sanctions
    /// EpicSanctions.QuerySanctions(sanctions => {
    ///     foreach (var sanction in sanctions) {
    ///         if (sanction.Type == "COMPETITIVE_BAN") {
    ///             DisableRankedMode();
    ///         }
    ///     }
    /// });
    ///
    /// // Check if player is banned
    /// if (EpicSanctions.IsBanned()) {
    ///     ShowBanScreen();
    /// }
    /// </code>
    /// </example>
    public static class EpicSanctions
    {
        #region Events

        /// <summary>Fired when sanctions are queried.</summary>
        public static event Action<List<Sanction>> OnSanctionsQueried;

        /// <summary>Fired when a ban is detected.</summary>
        public static event Action<Sanction> OnBanDetected;

        #endregion

        #region Private Fields

        private static SanctionsInterface _sanctionsInterface;
        private static readonly List<Sanction> _activeSanctions = new List<Sanction>();
        private static readonly object _sanctionsLock = new object();
        private static bool _hasQueried;

        #endregion

        #region Public Properties

        /// <summary>Whether the Sanctions interface is initialized.</summary>
        public static bool IsInitialized => _sanctionsInterface != null;

        /// <summary>Whether the local player has any active sanctions.</summary>
        public static bool HasActiveSanctions
        {
            get
            {
                lock (_sanctionsLock)
                {
                    return _activeSanctions.Count > 0;
                }
            }
        }

        /// <summary>Whether sanctions have been queried at least once.</summary>
        public static bool HasQueried => _hasQueried;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the Sanctions interface. Called automatically by EpicNetwork.
        /// </summary>
        internal static void Initialize()
        {
            var platformInterface = EOSManager.Instance.GetEOSPlatformInterface();
            if (platformInterface == null)
            {
                Debug.LogError("[EpicNet Sanctions] Failed to get platform interface");
                return;
            }

            _sanctionsInterface = platformInterface.GetSanctionsInterface();
            if (_sanctionsInterface == null)
            {
                Debug.LogError("[EpicNet Sanctions] Failed to get Sanctions interface");
                return;
            }

            Debug.Log("[EpicNet Sanctions] Initialized");
        }

        #endregion

        #region Query Sanctions

        /// <summary>
        /// Queries active sanctions for the local player.
        /// </summary>
        /// <param name="callback">Callback with list of active sanctions.</param>
        public static void QuerySanctions(Action<List<Sanction>> callback = null)
        {
            QuerySanctions(EpicNetwork.LocalPlayer?.UserId, callback);
        }

        /// <summary>
        /// Queries active sanctions for a specific player.
        /// </summary>
        /// <param name="targetUserId">The user to check sanctions for.</param>
        /// <param name="callback">Callback with list of active sanctions.</param>
        public static void QuerySanctions(ProductUserId targetUserId, Action<List<Sanction>> callback = null)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[EpicNet Sanctions] Not initialized");
                callback?.Invoke(new List<Sanction>());
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null || targetUserId == null)
            {
                callback?.Invoke(new List<Sanction>());
                return;
            }

            var options = new QueryActivePlayerSanctionsOptions
            {
                LocalUserId = localUserId,
                TargetUserId = targetUserId
            };

            _sanctionsInterface.QueryActivePlayerSanctions(ref options, null, (ref QueryActivePlayerSanctionsCallbackInfo info) =>
            {
                var sanctions = new List<Sanction>();

                if (info.ResultCode == Result.Success)
                {
                    var countOptions = new GetPlayerSanctionCountOptions
                    {
                        TargetUserId = targetUserId
                    };

                    uint count = _sanctionsInterface.GetPlayerSanctionCount(ref countOptions);

                    for (uint i = 0; i < count; i++)
                    {
                        var copyOptions = new CopyPlayerSanctionByIndexOptions
                        {
                            TargetUserId = targetUserId,
                            SanctionIndex = i
                        };

                        var result = _sanctionsInterface.CopyPlayerSanctionByIndex(ref copyOptions, out PlayerSanction? sanction);
                        if (result == Result.Success && sanction.HasValue)
                        {
                            var s = new Sanction
                            {
                                Id = sanction.Value.ReferenceId,
                                Type = sanction.Value.Action,
                                Reason = "", // Not provided by EOS
                                StartTime = DateTimeOffset.FromUnixTimeSeconds(sanction.Value.TimePlaced).DateTime,
                                ExpirationTime = sanction.Value.TimeExpires > 0
                                    ? DateTimeOffset.FromUnixTimeSeconds(sanction.Value.TimeExpires).DateTime
                                    : (DateTime?)null,
                                IsPermanent = sanction.Value.TimeExpires <= 0
                            };

                            sanctions.Add(s);
                        }
                    }

                    // Update cache for local player
                    if (targetUserId.Equals(localUserId))
                    {
                        lock (_sanctionsLock)
                        {
                            _activeSanctions.Clear();
                            _activeSanctions.AddRange(sanctions);
                            _hasQueried = true;
                        }

                        // Check for bans
                        foreach (var sanction in sanctions)
                        {
                            if (sanction.Type.Contains("BAN"))
                            {
                                OnBanDetected?.Invoke(sanction);
                            }
                        }
                    }

                    Debug.Log($"[EpicNet Sanctions] Found {sanctions.Count} active sanctions");
                    OnSanctionsQueried?.Invoke(sanctions);
                }
                else
                {
                    Debug.LogError($"[EpicNet Sanctions] Failed to query: {info.ResultCode}");
                }

                callback?.Invoke(sanctions);
            });
        }

        private static string GetSanctionType(int action)
        {
            // EOS uses numeric action codes - map to readable types
            // These should be configured in your EOS Developer Portal
            switch (action)
            {
                case 0: return "WARNING";
                case 1: return "TEMPORARY_BAN";
                case 2: return "PERMANENT_BAN";
                case 3: return "CHAT_MUTE";
                case 4: return "COMPETITIVE_BAN";
                default: return $"UNKNOWN_{action}";
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Checks if the local player is banned (uses cached data).
        /// </summary>
        /// <returns>True if the player has an active ban.</returns>
        public static bool IsBanned()
        {
            lock (_sanctionsLock)
            {
                foreach (var sanction in _activeSanctions)
                {
                    if (sanction.Type.Contains("BAN") && sanction.IsActive)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if the local player has a specific sanction type (uses cached data).
        /// </summary>
        /// <param name="sanctionType">The sanction type to check for.</param>
        /// <returns>True if the player has the specified sanction.</returns>
        public static bool HasSanction(string sanctionType)
        {
            if (string.IsNullOrEmpty(sanctionType)) return false;

            lock (_sanctionsLock)
            {
                foreach (var sanction in _activeSanctions)
                {
                    if (sanction.Type == sanctionType && sanction.IsActive)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Gets all cached active sanctions.
        /// </summary>
        public static List<Sanction> GetActiveSanctions()
        {
            lock (_sanctionsLock)
            {
                return new List<Sanction>(_activeSanctions);
            }
        }

        /// <summary>
        /// Gets the most severe active ban, if any.
        /// </summary>
        public static Sanction? GetMostSevereBan()
        {
            lock (_sanctionsLock)
            {
                Sanction? mostSevere = null;

                foreach (var sanction in _activeSanctions)
                {
                    if (!sanction.Type.Contains("BAN") || !sanction.IsActive)
                        continue;

                    if (mostSevere == null)
                    {
                        mostSevere = sanction;
                        continue;
                    }

                    // Permanent bans are most severe
                    if (sanction.IsPermanent && !mostSevere.Value.IsPermanent)
                    {
                        mostSevere = sanction;
                    }
                    // Otherwise, longer ban is more severe
                    else if (!mostSevere.Value.IsPermanent && !sanction.IsPermanent)
                    {
                        if (sanction.ExpirationTime > mostSevere.Value.ExpirationTime)
                        {
                            mostSevere = sanction;
                        }
                    }
                }

                return mostSevere;
            }
        }

        /// <summary>
        /// Gets remaining ban time in a human-readable format.
        /// </summary>
        public static string GetBanTimeRemaining()
        {
            var ban = GetMostSevereBan();
            if (ban == null) return null;

            if (ban.Value.IsPermanent)
            {
                return "Permanent";
            }

            if (ban.Value.ExpirationTime == null)
            {
                return "Unknown";
            }

            var remaining = ban.Value.ExpirationTime.Value - DateTime.UtcNow;

            if (remaining.TotalDays >= 1)
            {
                return $"{(int)remaining.TotalDays} days";
            }
            if (remaining.TotalHours >= 1)
            {
                return $"{(int)remaining.TotalHours} hours";
            }
            if (remaining.TotalMinutes >= 1)
            {
                return $"{(int)remaining.TotalMinutes} minutes";
            }

            return "Less than a minute";
        }

        #endregion
    }

    /// <summary>
    /// Represents an active player sanction.
    /// </summary>
    [Serializable]
    public struct Sanction
    {
        /// <summary>Unique sanction reference ID.</summary>
        public string Id;

        /// <summary>Type of sanction (e.g., "TEMPORARY_BAN", "CHAT_MUTE").</summary>
        public string Type;

        /// <summary>Reason for the sanction.</summary>
        public string Reason;

        /// <summary>When the sanction was applied.</summary>
        public DateTime StartTime;

        /// <summary>When the sanction expires (null for permanent).</summary>
        public DateTime? ExpirationTime;

        /// <summary>Whether this is a permanent sanction.</summary>
        public bool IsPermanent;

        /// <summary>Whether the sanction is currently active.</summary>
        public bool IsActive => IsPermanent || (ExpirationTime.HasValue && ExpirationTime.Value > DateTime.UtcNow);

        /// <summary>
        /// Returns a formatted string of the sanction.
        /// </summary>
        public override string ToString()
        {
            if (IsPermanent)
            {
                return $"{Type} (Permanent)";
            }
            return $"{Type} (Expires: {ExpirationTime:g})";
        }
    }
}
