using Epic.OnlineServices;
using Epic.OnlineServices.PlayerDataStorage;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Provides cloud save functionality for player data using EOS Player Data Storage.
    /// Use this for saving player inventory, settings, progression, and unlocked cosmetics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Player Data Storage is per-user cloud storage that syncs across devices.
    /// Each player has their own isolated storage space.
    /// </para>
    /// <para>
    /// Common use cases:
    /// </para>
    /// <list type="bullet">
    /// <item>Player inventory and equipped items</item>
    /// <item>Unlocked cosmetics and customization</item>
    /// <item>Game settings and preferences</item>
    /// <item>Campaign/level progression</item>
    /// <item>Currency balances</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Save player inventory
    /// var inventory = new PlayerInventory { coins = 500, gems = 25 };
    /// string json = JsonUtility.ToJson(inventory);
    /// EpicPlayerData.SaveFile("inventory.json", json, success => {
    ///     Debug.Log(success ? "Saved!" : "Save failed");
    /// });
    ///
    /// // Load player inventory
    /// EpicPlayerData.LoadFile("inventory.json", (success, data) => {
    ///     if (success) {
    ///         inventory = JsonUtility.FromJson&lt;PlayerInventory&gt;(data);
    ///     }
    /// });
    /// </code>
    /// </example>
    public static class EpicPlayerData
    {
        #region Events

        /// <summary>Fired when a file is saved.</summary>
        public static event Action<string, bool> OnFileSaved;

        /// <summary>Fired when a file is loaded.</summary>
        public static event Action<string, bool, string> OnFileLoaded;

        /// <summary>Fired when a file is deleted.</summary>
        public static event Action<string, bool> OnFileDeleted;

        /// <summary>Fired when the file list is retrieved.</summary>
        public static event Action<List<string>> OnFileListReceived;

        #endregion

        #region Private Fields

        private static PlayerDataStorageInterface _playerDataInterface;
        private static readonly Dictionary<string, byte[]> _localCache = new Dictionary<string, byte[]>();
        private static readonly Dictionary<string, PlayerDataStorageFileTransferRequest> _activeTransfers = new Dictionary<string, PlayerDataStorageFileTransferRequest>();
        private static readonly object _cacheLock = new object();

        #endregion

        #region Public Properties

        /// <summary>Whether the Player Data Storage interface is initialized.</summary>
        public static bool IsInitialized => _playerDataInterface != null;

        /// <summary>Whether local caching is enabled. Default: true.</summary>
        public static bool CacheEnabled { get; set; } = true;

        /// <summary>Whether to auto-save to cloud. If false, saves are cached locally until Sync() is called.</summary>
        public static bool AutoSync { get; set; } = true;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the Player Data Storage interface. Called automatically by EpicNetwork.
        /// </summary>
        internal static void Initialize()
        {
            var platformInterface = EOSManager.Instance.GetEOSPlatformInterface();
            if (platformInterface == null)
            {
                Debug.LogError("[EpicNet PlayerData] Failed to get platform interface");
                return;
            }

            _playerDataInterface = platformInterface.GetPlayerDataStorageInterface();
            if (_playerDataInterface == null)
            {
                Debug.LogError("[EpicNet PlayerData] Failed to get Player Data Storage interface");
                return;
            }

            Debug.Log("[EpicNet PlayerData] Initialized");
        }

        #endregion

        #region Save Methods

        /// <summary>
        /// Saves a string to cloud storage.
        /// </summary>
        /// <param name="filename">The filename to save as.</param>
        /// <param name="content">The string content to save.</param>
        /// <param name="callback">Optional callback with success status.</param>
        public static void SaveFile(string filename, string content, Action<bool> callback = null)
        {
            if (string.IsNullOrEmpty(content))
            {
                SaveFileBytes(filename, new byte[0], callback);
            }
            else
            {
                SaveFileBytes(filename, Encoding.UTF8.GetBytes(content), callback);
            }
        }

        /// <summary>
        /// Saves raw bytes to cloud storage.
        /// </summary>
        /// <param name="filename">The filename to save as.</param>
        /// <param name="data">The byte data to save.</param>
        /// <param name="callback">Optional callback with success status.</param>
        public static void SaveFileBytes(string filename, byte[] data, Action<bool> callback = null)
        {
            if (string.IsNullOrEmpty(filename))
            {
                Debug.LogError("[EpicNet PlayerData] Filename cannot be null or empty");
                callback?.Invoke(false);
                return;
            }

            if (!IsInitialized)
            {
                Debug.LogError("[EpicNet PlayerData] Not initialized");
                callback?.Invoke(false);
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                Debug.LogError("[EpicNet PlayerData] Not logged in");
                callback?.Invoke(false);
                return;
            }

            // Cache locally
            if (CacheEnabled)
            {
                lock (_cacheLock)
                {
                    _localCache[filename] = data;
                }
            }

            int dataOffset = 0;
            byte[] dataToWrite = data ?? new byte[0];

            var options = new WriteFileOptions
            {
                LocalUserId = localUserId,
                Filename = filename,
                ChunkLengthBytes = 4096,
                WriteFileDataCallback = (ref WriteFileDataCallbackInfo info, out ArraySegment<byte> outData) =>
                {
                    int remaining = dataToWrite.Length - dataOffset;
                    int chunkSize = Math.Min((int)info.DataBufferLengthBytes, remaining);

                    if (chunkSize > 0)
                    {
                        outData = new ArraySegment<byte>(dataToWrite, dataOffset, chunkSize);
                        dataOffset += chunkSize;
                        return remaining > chunkSize ? WriteResult.ContinueWriting : WriteResult.CompleteRequest;
                    }

                    outData = new ArraySegment<byte>();
                    return WriteResult.CompleteRequest;
                },
                FileTransferProgressCallback = null
            };

            var transferRequest = _playerDataInterface.WriteFile(ref options, null, (ref WriteFileCallbackInfo info) =>
            {
                lock (_cacheLock)
                {
                    _activeTransfers.Remove(filename);
                }

                bool success = info.ResultCode == Result.Success;

                if (success)
                {
                    Debug.Log($"[EpicNet PlayerData] Saved: {filename} ({dataToWrite.Length} bytes)");
                }
                else
                {
                    Debug.LogError($"[EpicNet PlayerData] Failed to save {filename}: {info.ResultCode}");
                }

                callback?.Invoke(success);
                OnFileSaved?.Invoke(filename, success);
            });

            if (transferRequest != null)
            {
                lock (_cacheLock)
                {
                    _activeTransfers[filename] = transferRequest;
                }
            }
        }

        /// <summary>
        /// Saves an object as JSON to cloud storage.
        /// </summary>
        /// <typeparam name="T">The object type.</typeparam>
        /// <param name="filename">The filename to save as.</param>
        /// <param name="data">The object to serialize and save.</param>
        /// <param name="callback">Optional callback with success status.</param>
        public static void SaveJson<T>(string filename, T data, Action<bool> callback = null)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                SaveFile(filename, json, callback);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EpicNet PlayerData] Failed to serialize {typeof(T).Name}: {e.Message}");
                callback?.Invoke(false);
            }
        }

        #endregion

        #region Load Methods

        /// <summary>
        /// Loads a file from cloud storage as a string.
        /// </summary>
        /// <param name="filename">The filename to load.</param>
        /// <param name="callback">Callback with success status and file contents.</param>
        /// <param name="useCache">Whether to use cached data if available.</param>
        public static void LoadFile(string filename, Action<bool, string> callback, bool useCache = true)
        {
            LoadFileBytes(filename, (success, data) =>
            {
                string content = success && data != null && data.Length > 0
                    ? Encoding.UTF8.GetString(data)
                    : null;
                callback?.Invoke(success, content);
            }, useCache);
        }

        /// <summary>
        /// Loads a file from cloud storage as raw bytes.
        /// </summary>
        /// <param name="filename">The filename to load.</param>
        /// <param name="callback">Callback with success status and file contents.</param>
        /// <param name="useCache">Whether to use cached data if available.</param>
        public static void LoadFileBytes(string filename, Action<bool, byte[]> callback, bool useCache = true)
        {
            if (string.IsNullOrEmpty(filename))
            {
                Debug.LogError("[EpicNet PlayerData] Filename cannot be null or empty");
                callback?.Invoke(false, null);
                return;
            }

            if (!IsInitialized)
            {
                Debug.LogError("[EpicNet PlayerData] Not initialized");
                callback?.Invoke(false, null);
                return;
            }

            // Check cache first
            if (useCache && CacheEnabled)
            {
                lock (_cacheLock)
                {
                    if (_localCache.TryGetValue(filename, out byte[] cachedData))
                    {
                        Debug.Log($"[EpicNet PlayerData] Returning cached: {filename}");
                        callback?.Invoke(true, cachedData);
                        return;
                    }
                }
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                Debug.LogError("[EpicNet PlayerData] Not logged in");
                callback?.Invoke(false, null);
                return;
            }

            var fileData = new List<byte>();

            var options = new ReadFileOptions
            {
                LocalUserId = localUserId,
                Filename = filename,
                ReadChunkLengthBytes = 4096,
                ReadFileDataCallback = (ref Epic.OnlineServices.PlayerDataStorage.ReadFileDataCallbackInfo dataInfo) =>
                {
                    if (dataInfo.DataChunk != null)
                    {
                        fileData.AddRange(dataInfo.DataChunk.ToArray());
                    }
                    return Epic.OnlineServices.PlayerDataStorage.ReadResult.ContinueReading;
                },
                FileTransferProgressCallback = null
            };

            var transferRequest = _playerDataInterface.ReadFile(ref options, null, (ref Epic.OnlineServices.PlayerDataStorage.ReadFileCallbackInfo info) =>
            {
                lock (_cacheLock)
                {
                    _activeTransfers.Remove(filename);
                }

                if (info.ResultCode == Result.Success)
                {
                    byte[] data = fileData.ToArray();

                    // Update cache
                    if (CacheEnabled)
                    {
                        lock (_cacheLock)
                        {
                            _localCache[filename] = data;
                        }
                    }

                    Debug.Log($"[EpicNet PlayerData] Loaded: {filename} ({data.Length} bytes)");
                    callback?.Invoke(true, data);
                    OnFileLoaded?.Invoke(filename, true, data.Length > 0 ? Encoding.UTF8.GetString(data) : "");
                }
                else if (info.ResultCode == Result.NotFound)
                {
                    Debug.Log($"[EpicNet PlayerData] File not found: {filename}");
                    callback?.Invoke(false, null);
                    OnFileLoaded?.Invoke(filename, false, null);
                }
                else
                {
                    Debug.LogError($"[EpicNet PlayerData] Failed to load {filename}: {info.ResultCode}");
                    callback?.Invoke(false, null);
                    OnFileLoaded?.Invoke(filename, false, null);
                }
            });

            if (transferRequest != null)
            {
                lock (_cacheLock)
                {
                    _activeTransfers[filename] = transferRequest;
                }
            }
        }

        /// <summary>
        /// Loads a JSON file and deserializes it.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="filename">The filename to load.</param>
        /// <param name="callback">Callback with success status and deserialized object.</param>
        /// <param name="defaultValue">Default value if file not found or deserialization fails.</param>
        public static void LoadJson<T>(string filename, Action<bool, T> callback, T defaultValue = default)
        {
            LoadFile(filename, (success, json) =>
            {
                if (success && !string.IsNullOrEmpty(json))
                {
                    try
                    {
                        T data = JsonUtility.FromJson<T>(json);
                        callback?.Invoke(true, data);
                        return;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[EpicNet PlayerData] Failed to deserialize {typeof(T).Name}: {e.Message}");
                    }
                }

                callback?.Invoke(false, defaultValue);
            });
        }

        #endregion

        #region Delete & Query Methods

        /// <summary>
        /// Deletes a file from cloud storage.
        /// </summary>
        /// <param name="filename">The filename to delete.</param>
        /// <param name="callback">Optional callback with success status.</param>
        public static void DeleteFile(string filename, Action<bool> callback = null)
        {
            if (string.IsNullOrEmpty(filename))
            {
                callback?.Invoke(false);
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

            // Remove from cache
            lock (_cacheLock)
            {
                _localCache.Remove(filename);
            }

            var options = new DeleteFileOptions
            {
                LocalUserId = localUserId,
                Filename = filename
            };

            _playerDataInterface.DeleteFile(ref options, null, (ref DeleteFileCallbackInfo info) =>
            {
                bool success = info.ResultCode == Result.Success || info.ResultCode == Result.NotFound;

                if (info.ResultCode == Result.Success)
                {
                    Debug.Log($"[EpicNet PlayerData] Deleted: {filename}");
                }
                else if (info.ResultCode != Result.NotFound)
                {
                    Debug.LogError($"[EpicNet PlayerData] Failed to delete {filename}: {info.ResultCode}");
                }

                callback?.Invoke(success);
                OnFileDeleted?.Invoke(filename, success);
            });
        }

        /// <summary>
        /// Gets a list of all player data files.
        /// </summary>
        /// <param name="callback">Callback with list of filenames.</param>
        public static void GetFileList(Action<List<string>> callback)
        {
            if (!IsInitialized)
            {
                callback?.Invoke(new List<string>());
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                callback?.Invoke(new List<string>());
                return;
            }

            var options = new QueryFileListOptions
            {
                LocalUserId = localUserId
            };

            _playerDataInterface.QueryFileList(ref options, null,
                (ref QueryFileListCallbackInfo info) =>
                {
                    var fileList = new List<string>();

                    if (info.ResultCode == Result.Success)
                    {
                        var countOptions = new GetFileMetadataCountOptions
                        {
                            LocalUserId = localUserId
                        };

                        int fileCount;
                        var countResult = _playerDataInterface.GetFileMetadataCount(
                            ref countOptions,
                            out fileCount
                        );

                        if (countResult == Result.Success)
                        {
                            for (int i = 0; i < fileCount; i++)
                            {
                                var copyOptions = new CopyFileMetadataAtIndexOptions
                                {
                                    LocalUserId = localUserId,
                                    Index = (uint)i
                                };

                                var copyResult = _playerDataInterface.CopyFileMetadataAtIndex(
                                    ref copyOptions,
                                    out Epic.OnlineServices.PlayerDataStorage.FileMetadata? metadata
                                );

                                if (copyResult == Result.Success && metadata.HasValue)
                                {
                                    fileList.Add(metadata.Value.Filename.ToString());
                                }
                            }
                        }

                        Debug.Log($"[EpicNet PlayerData] Found {fileList.Count} files");
                    }
                    else
                    {
                        Debug.LogError($"[EpicNet PlayerData] Failed to query file list: {info.ResultCode}");
                    }

                    callback?.Invoke(fileList);
                    OnFileListReceived?.Invoke(fileList);
                });
        }

        /// <summary>
        /// Checks if a file exists in cloud storage.
        /// </summary>
        public static void FileExists(string filename, Action<bool> callback)
        {
            if (!IsInitialized || string.IsNullOrEmpty(filename))
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

            var options = new CopyFileMetadataByFilenameOptions
            {
                LocalUserId = localUserId,
                Filename = filename
            };

            var result = _playerDataInterface.CopyFileMetadataByFilename(ref options, out _);
            callback?.Invoke(result == Result.Success);
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Clears the local cache.
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _localCache.Clear();
            }
            Debug.Log("[EpicNet PlayerData] Cache cleared");
        }

        /// <summary>
        /// Gets a cached file without network request.
        /// </summary>
        public static bool TryGetCached(string filename, out string content)
        {
            content = null;
            lock (_cacheLock)
            {
                if (_localCache.TryGetValue(filename, out byte[] data))
                {
                    content = Encoding.UTF8.GetString(data);
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}
