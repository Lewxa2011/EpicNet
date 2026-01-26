using Epic.OnlineServices;
using Epic.OnlineServices.Ecom;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Provides access to EOS Ecommerce for managing entitlements and purchases.
    /// Use this for cosmetic ownership, DLC, battle passes, and premium currency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entitlements represent ownership of in-game items and must be configured in the EOS Developer Portal.
    /// This system handles:
    /// </para>
    /// <list type="bullet">
    /// <item>Querying owned entitlements (cosmetics, DLC)</item>
    /// <item>Checking ownership of specific items</item>
    /// <item>Initiating purchases through the Epic overlay</item>
    /// <item>Consuming/redeeming entitlements</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Check if player owns a cosmetic
    /// EpicEntitlements.HasEntitlement("SKIN_LEGENDARY_001", owns => {
    ///     if (owns) EquipSkin("SKIN_LEGENDARY_001");
    /// });
    ///
    /// // Query all owned entitlements
    /// EpicEntitlements.QueryEntitlements(entitlements => {
    ///     foreach (var ent in entitlements) {
    ///         UnlockCosmetic(ent.Id);
    ///     }
    /// });
    /// </code>
    /// </example>
    public static class EpicEntitlements
    {
        #region Events

        /// <summary>Fired when entitlements are queried.</summary>
        public static event Action<List<Entitlement>> OnEntitlementsQueried;

        /// <summary>Fired when a purchase completes.</summary>
        public static event Action<string, bool> OnPurchaseComplete;

        /// <summary>Fired when an entitlement is consumed/redeemed.</summary>
        public static event Action<string, bool> OnEntitlementConsumed;

        #endregion

        #region Private Fields

        private static EcomInterface _ecomInterface;
        private static readonly Dictionary<string, Entitlement> _cachedEntitlements = new Dictionary<string, Entitlement>();
        private static readonly Dictionary<string, CatalogItem> _cachedCatalog = new Dictionary<string, CatalogItem>();
        private static readonly object _cacheLock = new object();

        #endregion

        #region Public Properties

        /// <summary>Whether the Ecom interface is initialized.</summary>
        public static bool IsInitialized => _ecomInterface != null;

        /// <summary>Number of owned entitlements.</summary>
        public static int OwnedCount
        {
            get
            {
                lock (_cacheLock)
                {
                    return _cachedEntitlements.Count;
                }
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the Ecom interface. Called automatically by EpicNetwork.
        /// </summary>
        internal static void Initialize()
        {
            var platformInterface = EOSManager.Instance.GetEOSPlatformInterface();
            if (platformInterface == null)
            {
                Debug.LogError("[EpicNet Entitlements] Failed to get platform interface");
                return;
            }

            _ecomInterface = platformInterface.GetEcomInterface();
            if (_ecomInterface == null)
            {
                Debug.LogError("[EpicNet Entitlements] Failed to get Ecom interface");
                return;
            }

            Debug.Log("[EpicNet Entitlements] Initialized");
        }

        #endregion

        #region Query Entitlements

        /// <summary>
        /// Queries all entitlements owned by the local player.
        /// </summary>
        /// <param name="callback">Callback with list of owned entitlements.</param>
        /// <param name="includeRedeemed">Whether to include already redeemed entitlements.</param>
        public static void QueryEntitlements(Action<List<Entitlement>> callback, bool includeRedeemed = true)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[EpicNet Entitlements] Not initialized");
                callback?.Invoke(new List<Entitlement>());
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            var epicAccountId = EOSManager.Instance.GetLocalUserId();

            if (localUserId == null || epicAccountId == null)
            {
                Debug.LogError("[EpicNet Entitlements] Not logged in");
                callback?.Invoke(new List<Entitlement>());
                return;
            }

            var options = new QueryEntitlementsOptions
            {
                LocalUserId = epicAccountId,
                IncludeRedeemed = includeRedeemed
            };

            _ecomInterface.QueryEntitlements(ref options, null, (ref QueryEntitlementsCallbackInfo info) =>
            {
                var entitlements = new List<Entitlement>();

                if (info.ResultCode == Result.Success)
                {
                    var countOptions = new GetEntitlementsCountOptions
                    {
                        LocalUserId = epicAccountId
                    };

                    uint count = _ecomInterface.GetEntitlementsCount(ref countOptions);

                    lock (_cacheLock)
                    {
                        _cachedEntitlements.Clear();

                        for (uint i = 0; i < count; i++)
                        {
                            var copyOptions = new CopyEntitlementByIndexOptions
                            {
                                LocalUserId = epicAccountId,
                                EntitlementIndex = i
                            };

                            var result = _ecomInterface.CopyEntitlementByIndex(ref copyOptions, out Epic.OnlineServices.Ecom.Entitlement? ent);
                            if (result == Result.Success && ent.HasValue)
                            {
                                var entitlement = new Entitlement
                                {
                                    Id = ent.Value.EntitlementId,
                                    CatalogItemId = ent.Value.CatalogItemId,
                                    IsRedeemed = ent.Value.Redeemed,
                                    EndTimestamp = ent.Value.EndTimestamp,
                                    ServerIndex = ent.Value.ServerIndex
                                };

                                _cachedEntitlements[entitlement.Id] = entitlement;
                                entitlements.Add(entitlement);
                            }
                        }
                    }

                    Debug.Log($"[EpicNet Entitlements] Found {entitlements.Count} entitlements");
                    OnEntitlementsQueried?.Invoke(entitlements);
                }
                else
                {
                    Debug.LogError($"[EpicNet Entitlements] Failed to query: {info.ResultCode}");
                }

                callback?.Invoke(entitlements);
            });
        }

        /// <summary>
        /// Checks if the player owns a specific entitlement.
        /// </summary>
        /// <param name="entitlementId">The entitlement ID or catalog item ID to check.</param>
        /// <param name="callback">Callback with ownership status.</param>
        public static void HasEntitlement(string entitlementId, Action<bool> callback)
        {
            if (string.IsNullOrEmpty(entitlementId))
            {
                callback?.Invoke(false);
                return;
            }

            // Check cache first
            lock (_cacheLock)
            {
                if (_cachedEntitlements.ContainsKey(entitlementId))
                {
                    callback?.Invoke(true);
                    return;
                }

                // Also check by catalog item ID
                foreach (var ent in _cachedEntitlements.Values)
                {
                    if (ent.CatalogItemId == entitlementId)
                    {
                        callback?.Invoke(true);
                        return;
                    }
                }
            }

            // Query from server
            QueryEntitlements(entitlements =>
            {
                bool owns = false;
                foreach (var ent in entitlements)
                {
                    if (ent.Id == entitlementId || ent.CatalogItemId == entitlementId)
                    {
                        owns = true;
                        break;
                    }
                }
                callback?.Invoke(owns);
            });
        }

        /// <summary>
        /// Checks multiple entitlements at once (uses cache).
        /// </summary>
        /// <param name="entitlementIds">Array of entitlement IDs to check.</param>
        /// <returns>Dictionary of ID to ownership status.</returns>
        public static Dictionary<string, bool> HasEntitlements(string[] entitlementIds)
        {
            var results = new Dictionary<string, bool>();

            lock (_cacheLock)
            {
                foreach (var id in entitlementIds)
                {
                    bool owns = _cachedEntitlements.ContainsKey(id);

                    if (!owns)
                    {
                        foreach (var ent in _cachedEntitlements.Values)
                        {
                            if (ent.CatalogItemId == id)
                            {
                                owns = true;
                                break;
                            }
                        }
                    }

                    results[id] = owns;
                }
            }

            return results;
        }

        #endregion

        #region Catalog

        /// <summary>
        /// Queries the store catalog.
        /// </summary>
        /// <param name="callback">Callback with list of catalog items.</param>
        public static void QueryCatalog(Action<List<CatalogItem>> callback)
        {
            if (!IsInitialized)
            {
                callback?.Invoke(new List<CatalogItem>());
                return;
            }

            var epicAccountId = EOSManager.Instance.GetLocalUserId();
            if (epicAccountId == null)
            {
                callback?.Invoke(new List<CatalogItem>());
                return;
            }

            var options = new QueryOffersOptions
            {
                LocalUserId = epicAccountId
            };

            _ecomInterface.QueryOffers(ref options, null, (ref QueryOffersCallbackInfo info) =>
            {
                var items = new List<CatalogItem>();

                if (info.ResultCode == Result.Success)
                {
                    var countOptions = new GetOfferCountOptions
                    {
                        LocalUserId = epicAccountId
                    };

                    uint count = _ecomInterface.GetOfferCount(ref countOptions);

                    lock (_cacheLock)
                    {
                        _cachedCatalog.Clear();

                        for (uint i = 0; i < count; i++)
                        {
                            var copyOptions = new CopyOfferByIndexOptions
                            {
                                LocalUserId = epicAccountId,
                                OfferIndex = i
                            };

                            var result = _ecomInterface.CopyOfferByIndex(ref copyOptions, out CatalogOffer? offer);
                            if (result == Result.Success && offer.HasValue)
                            {
                                var item = new CatalogItem
                                {
                                    Id = offer.Value.Id,
                                    Title = offer.Value.TitleText,
                                    Description = offer.Value.DescriptionText,
                                    PriceText = offer.Value.CurrentPrice64.ToString(),
                                    OriginalPriceText = offer.Value.OriginalPrice64.ToString(),
                                    CurrentPrice = offer.Value.CurrentPrice64,
                                    OriginalPrice = offer.Value.OriginalPrice64,
                                    DiscountPercentage = offer.Value.DiscountPercentage,
                                    CurrencyCode = offer.Value.CurrencyCode,
                                    ExpirationTimestamp = offer.Value.ExpirationTimestamp,
                                    PurchaseLimit = (uint)offer.Value.PurchaseLimit
                                };

                                _cachedCatalog[item.Id] = item;
                                items.Add(item);
                            }
                        }
                    }

                    Debug.Log($"[EpicNet Entitlements] Found {items.Count} catalog items");
                }
                else
                {
                    Debug.LogError($"[EpicNet Entitlements] Failed to query catalog: {info.ResultCode}");
                }

                callback?.Invoke(items);
            });
        }

        /// <summary>
        /// Gets a cached catalog item by ID.
        /// </summary>
        public static CatalogItem? GetCatalogItem(string itemId)
        {
            lock (_cacheLock)
            {
                if (_cachedCatalog.TryGetValue(itemId, out var item))
                {
                    return item;
                }
            }
            return null;
        }

        #endregion

        #region Purchases

        /// <summary>
        /// Initiates a purchase through the Epic overlay.
        /// </summary>
        /// <param name="offerId">The offer/catalog item ID to purchase.</param>
        /// <param name="callback">Callback with success status.</param>
        public static void Purchase(string offerId, Action<bool> callback = null)
        {
            if (!IsInitialized || string.IsNullOrEmpty(offerId))
            {
                callback?.Invoke(false);
                return;
            }

            var epicAccountId = EOSManager.Instance.GetLocalUserId();
            if (epicAccountId == null)
            {
                callback?.Invoke(false);
                return;
            }

            var options = new CheckoutOptions
            {
                LocalUserId = epicAccountId,
                Entries = new CheckoutEntry[]
                {
                    new CheckoutEntry { OfferId = offerId }
                }
            };

            _ecomInterface.Checkout(ref options, null, (ref CheckoutCallbackInfo info) =>
            {
                bool success = info.ResultCode == Result.Success;

                if (success)
                {
                    Debug.Log($"[EpicNet Entitlements] Purchase completed: {offerId}");
                    // Refresh entitlements after purchase
                    QueryEntitlements(_ => { });
                }
                else if (info.ResultCode == Result.Canceled)
                {
                    Debug.Log($"[EpicNet Entitlements] Purchase cancelled: {offerId}");
                }
                else
                {
                    Debug.LogError($"[EpicNet Entitlements] Purchase failed: {info.ResultCode}");
                }

                callback?.Invoke(success);
                OnPurchaseComplete?.Invoke(offerId, success);
            });
        }

        #endregion

        #region Redeem/Consume

        /// <summary>
        /// Redeems/consumes an entitlement (for consumable items).
        /// </summary>
        /// <param name="entitlementId">The entitlement ID to redeem.</param>
        /// <param name="callback">Callback with success status.</param>
        public static void RedeemEntitlement(string entitlementId, Action<bool> callback = null)
        {
            if (!IsInitialized || string.IsNullOrEmpty(entitlementId))
            {
                callback?.Invoke(false);
                return;
            }

            var epicAccountId = EOSManager.Instance.GetLocalUserId();
            if (epicAccountId == null)
            {
                callback?.Invoke(false);
                return;
            }

            var options = new RedeemEntitlementsOptions
            {
                LocalUserId = epicAccountId,
                EntitlementIds = new Utf8String[] { entitlementId }
            };

            _ecomInterface.RedeemEntitlements(ref options, null, (ref RedeemEntitlementsCallbackInfo info) =>
            {
                bool success = info.ResultCode == Result.Success;

                if (success)
                {
                    Debug.Log($"[EpicNet Entitlements] Redeemed: {entitlementId}");

                    // Update cache
                    lock (_cacheLock)
                    {
                        if (_cachedEntitlements.TryGetValue(entitlementId, out var ent))
                        {
                            ent.IsRedeemed = true;
                            _cachedEntitlements[entitlementId] = ent;
                        }
                    }
                }
                else
                {
                    Debug.LogError($"[EpicNet Entitlements] Failed to redeem: {info.ResultCode}");
                }

                callback?.Invoke(success);
                OnEntitlementConsumed?.Invoke(entitlementId, success);
            });
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets all cached entitlements.
        /// </summary>
        public static List<Entitlement> GetAllEntitlements()
        {
            lock (_cacheLock)
            {
                return new List<Entitlement>(_cachedEntitlements.Values);
            }
        }

        /// <summary>
        /// Gets all cached catalog items.
        /// </summary>
        public static List<CatalogItem> GetAllCatalogItems()
        {
            lock (_cacheLock)
            {
                return new List<CatalogItem>(_cachedCatalog.Values);
            }
        }

        /// <summary>
        /// Clears the entitlements cache.
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedEntitlements.Clear();
                _cachedCatalog.Clear();
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents an owned entitlement.
    /// </summary>
    [Serializable]
    public struct Entitlement
    {
        /// <summary>Unique entitlement ID.</summary>
        public string Id;

        /// <summary>The catalog item this entitlement is for.</summary>
        public string CatalogItemId;

        /// <summary>Whether the entitlement has been redeemed/consumed.</summary>
        public bool IsRedeemed;

        /// <summary>When the entitlement expires (-1 for never).</summary>
        public long EndTimestamp;

        /// <summary>Server-side index for this entitlement.</summary>
        public int ServerIndex;

        /// <summary>Whether the entitlement is still valid (not expired).</summary>
        public bool IsValid => EndTimestamp < 0 || EndTimestamp > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Represents a purchasable catalog item.
    /// </summary>
    [Serializable]
    public struct CatalogItem
    {
        /// <summary>Unique offer/item ID.</summary>
        public string Id;

        /// <summary>Display title.</summary>
        public string Title;

        /// <summary>Description text.</summary>
        public string Description;

        /// <summary>Formatted current price string.</summary>
        public string PriceText;

        /// <summary>Formatted original price string (before discount).</summary>
        public string OriginalPriceText;

        /// <summary>Current price in smallest currency unit.</summary>
        public ulong CurrentPrice;

        /// <summary>Original price before discount.</summary>
        public ulong OriginalPrice;

        /// <summary>Discount percentage (0-100).</summary>
        public uint DiscountPercentage;

        /// <summary>Currency code (e.g., "USD").</summary>
        public string CurrencyCode;

        /// <summary>When the offer expires (-1 for never).</summary>
        public long ExpirationTimestamp;

        /// <summary>Purchase limit per user (-1 for unlimited).</summary>
        public uint PurchaseLimit;

        /// <summary>Whether the item is on sale.</summary>
        public bool IsOnSale => DiscountPercentage > 0;
    }
}
