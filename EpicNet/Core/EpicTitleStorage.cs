using Epic.OnlineServices;
using Epic.OnlineServices.TitleStorage;
using PlayEveryWare.EpicOnlineServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Provides access to EOS Title Storage for downloading game data files.
    /// Use this for cosmetics definitions, game configurations, localization, and other
    /// read-only data that should be centrally managed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Title Storage files are uploaded via the EOS Developer Portal and are read-only
    /// for clients. This is ideal for:
    /// </para>
    /// <list type="bullet">
    /// <item>Cosmetics catalog (skins, items, effects)</item>
    /// <item>Game balance data (weapon stats, character abilities)</item>
    /// <item>Localization files</item>
    /// <item>Event configurations</item>
    /// <item>News/announcements</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Download a cosmetics catalog
    /// EpicTitleStorage.DownloadFile("cosmetics.json", (success, data) =>
    /// {
    ///     if (success)
    ///     {
    ///         var catalog = JsonUtility.FromJson&lt;CosmeticsCatalog&gt;(data);
    ///         LoadCosmetics(catalog);
    ///     }
    /// });
    /// </code>
    /// </example>
    public static class EpicTitleStorage
    {
        #region Events

        /// <summary>
        /// Fired when the file list is retrieved.
        /// </summary>
        public static event Action<List<string>> OnFileListReceived;

        /// <summary>
        /// Fired when a file download completes.
        /// </summary>
        public static event Action<string, bool, string> OnFileDownloaded;

        /// <summary>
        /// Fired when download progress updates.
        /// </summary>
        public static event Action<string, float> OnDownloadProgress;

        #endregion

        #region Private Fields

        private static TitleStorageInterface _titleStorageInterface;
        private static readonly Dictionary<string, byte[]> _fileCache = new Dictionary<string, byte[]>();
        private static readonly Dictionary<string, TitleStorageFileTransferRequest> _activeTransfers = new Dictionary<string, TitleStorageFileTransferRequest>();
        private static readonly object _cacheLock = new object();

        #endregion

        #region Public Properties

        /// <summary>
        /// Whether the Title Storage interface is initialized.
        /// </summary>
        public static bool IsInitialized => _titleStorageInterface != null;

        /// <summary>
        /// Whether caching is enabled. Default: true.
        /// When enabled, downloaded files are cached in memory.
        /// </summary>
        public static bool CacheEnabled { get; set; } = true;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the Title Storage interface. Called automatically by EpicNetwork.
        /// </summary>
        internal static void Initialize()
        {
            var platformInterface = EOSManager.Instance.GetEOSPlatformInterface();
            if (platformInterface == null)
            {
                Debug.LogError("[EpicNet TitleStorage] Failed to get platform interface");
                return;
            }

            _titleStorageInterface = platformInterface.GetTitleStorageInterface();
            if (_titleStorageInterface == null)
            {
                Debug.LogError("[EpicNet TitleStorage] Failed to get Title Storage interface");
                return;
            }

            Debug.Log("[EpicNet TitleStorage] Initialized");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets a list of all files available in Title Storage.
        /// </summary>
        /// <param name="tags">Optional tags to filter files.</param>
        /// <param name="callback">Callback with the list of filenames.</param>
        public static void GetFileList(string[] tags = null, Action<List<string>> callback = null)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[EpicNet TitleStorage] Not initialized");
                callback?.Invoke(new List<string>());
                return;
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                Debug.LogError("[EpicNet TitleStorage] Not logged in");
                callback?.Invoke(new List<string>());
                return;
            }

            var options = new QueryFileListOptions
            {
                LocalUserId = localUserId,
                ListOfTags = tags
                    .Select(t => new Utf8String(t))
                    .ToArray()
            };

            _titleStorageInterface.QueryFileList(ref options, null, (ref QueryFileListCallbackInfo info) =>
            {
                var fileList = new List<string>();

                if (info.ResultCode == Result.Success)
                {
                    var countOptions = new GetFileMetadataCountOptions
                    {
                        LocalUserId = localUserId
                    };

                    uint fileCount = _titleStorageInterface.GetFileMetadataCount(ref countOptions);

                    for (uint i = 0; i < fileCount; i++)
                    {
                        var copyOptions = new CopyFileMetadataAtIndexOptions
                        {
                            LocalUserId = localUserId,
                            Index = i
                        };

                        var result = _titleStorageInterface.CopyFileMetadataAtIndex(ref copyOptions, out FileMetadata? metadata);
                        if (result == Result.Success && metadata.HasValue)
                        {
                            fileList.Add(metadata.Value.Filename);
                        }
                    }

                    Debug.Log($"[EpicNet TitleStorage] Found {fileList.Count} files");
                }
                else
                {
                    Debug.LogError($"[EpicNet TitleStorage] Failed to query file list: {info.ResultCode}");
                }

                callback?.Invoke(fileList);
                OnFileListReceived?.Invoke(fileList);
            });
        }

        /// <summary>
        /// Downloads a file from Title Storage.
        /// </summary>
        /// <param name="filename">The filename to download.</param>
        /// <param name="callback">Callback with success status and file contents as string.</param>
        /// <param name="useCache">Whether to use cached data if available.</param>
        public static void DownloadFile(string filename, Action<bool, string> callback, bool useCache = true)
        {
            DownloadFileBytes(filename, (success, data) =>
            {
                string content = success && data != null ? Encoding.UTF8.GetString(data) : null;
                callback?.Invoke(success, content);
            }, useCache);
        }

        /// <summary>
        /// Downloads a file from Title Storage as raw bytes.
        /// </summary>
        /// <param name="filename">The filename to download.</param>
        /// <param name="callback">Callback with success status and file contents as bytes.</param>
        /// <param name="useCache">Whether to use cached data if available.</param>
        public static void DownloadFileBytes(string filename, Action<bool, byte[]> callback, bool useCache = true)
        {
            if (string.IsNullOrEmpty(filename))
            {
                Debug.LogError("[EpicNet TitleStorage] Filename cannot be null or empty");
                callback?.Invoke(false, null);
                return;
            }

            if (!IsInitialized)
            {
                Debug.LogError("[EpicNet TitleStorage] Not initialized");
                callback?.Invoke(false, null);
                return;
            }

            // Check cache first
            if (useCache && CacheEnabled)
            {
                lock (_cacheLock)
                {
                    if (_fileCache.TryGetValue(filename, out byte[] cachedData))
                    {
                        Debug.Log($"[EpicNet TitleStorage] Returning cached file: {filename}");
                        callback?.Invoke(true, cachedData);
                        return;
                    }
                }
            }

            var localUserId = EpicNetwork.LocalPlayer?.UserId;
            if (localUserId == null)
            {
                Debug.LogError("[EpicNet TitleStorage] Not logged in");
                callback?.Invoke(false, null);
                return;
            }

            // Check if already downloading
            lock (_cacheLock)
            {
                if (_activeTransfers.ContainsKey(filename))
                {
                    Debug.LogWarning($"[EpicNet TitleStorage] Already downloading: {filename}");
                    return;
                }
            }

            var fileData = new List<byte>();

            var options = new ReadFileOptions
            {
                LocalUserId = localUserId,
                Filename = filename,
                ReadChunkLengthBytes = 4096,
                ReadFileDataCallback = (ref ReadFileDataCallbackInfo dataInfo) =>
                {
                    if (dataInfo.DataChunk != null)
                    {
                        fileData.AddRange(dataInfo.DataChunk.ToArray());
                    }
                    return ReadResult.RrContinueReading;
                },
                FileTransferProgressCallback = (ref FileTransferProgressCallbackInfo progressInfo) =>
                {
                    float progress = progressInfo.TotalFileSizeBytes > 0
                        ? (float)progressInfo.BytesTransferred / progressInfo.TotalFileSizeBytes
                        : 0f;
                    OnDownloadProgress?.Invoke(filename, progress);
                }
            };

            var transferRequest = _titleStorageInterface.ReadFile(ref options, null, (ref ReadFileCallbackInfo info) =>
            {
                lock (_cacheLock)
                {
                    _activeTransfers.Remove(filename);
                }

                if (info.ResultCode == Result.Success)
                {
                    byte[] data = fileData.ToArray();

                    // Cache the file
                    if (CacheEnabled)
                    {
                        lock (_cacheLock)
                        {
                            _fileCache[filename] = data;
                        }
                    }

                    Debug.Log($"[EpicNet TitleStorage] Downloaded: {filename} ({data.Length} bytes)");
                    callback?.Invoke(true, data);
                    OnFileDownloaded?.Invoke(filename, true, Encoding.UTF8.GetString(data));
                }
                else
                {
                    Debug.LogError($"[EpicNet TitleStorage] Failed to download {filename}: {info.ResultCode}");
                    callback?.Invoke(false, null);
                    OnFileDownloaded?.Invoke(filename, false, null);
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
        /// Downloads multiple files in parallel.
        /// </summary>
        /// <param name="filenames">List of filenames to download.</param>
        /// <param name="callback">Callback when all downloads complete.</param>
        public static void DownloadFiles(string[] filenames, Action<Dictionary<string, string>> callback)
        {
            if (filenames == null || filenames.Length == 0)
            {
                callback?.Invoke(new Dictionary<string, string>());
                return;
            }

            var results = new Dictionary<string, string>();
            int remaining = filenames.Length;
            object lockObj = new object();

            foreach (var filename in filenames)
            {
                DownloadFile(filename, (success, data) =>
                {
                    lock (lockObj)
                    {
                        if (success)
                        {
                            results[filename] = data;
                        }
                        remaining--;

                        if (remaining == 0)
                        {
                            callback?.Invoke(results);
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Gets file metadata without downloading the content.
        /// </summary>
        /// <param name="filename">The filename to query.</param>
        /// <param name="callback">Callback with file metadata.</param>
        public static void GetFileMetadata(string filename, Action<TitleFileMetadata?> callback)
        {
            if (!IsInitialized)
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

            var options = new CopyFileMetadataByFilenameOptions
            {
                LocalUserId = localUserId,
                Filename = filename
            };

            var result = _titleStorageInterface.CopyFileMetadataByFilename(ref options, out FileMetadata? metadata);

            if (result == Result.Success && metadata.HasValue)
            {
                callback?.Invoke(new TitleFileMetadata
                {
                    Filename = metadata.Value.Filename,
                    FileSizeBytes = metadata.Value.FileSizeBytes,
                    MD5Hash = metadata.Value.MD5Hash
                });
            }
            else
            {
                callback?.Invoke(null);
            }
        }

        /// <summary>
        /// Cancels an active download.
        /// </summary>
        /// <param name="filename">The filename to cancel.</param>
        public static void CancelDownload(string filename)
        {
            lock (_cacheLock)
            {
                if (_activeTransfers.TryGetValue(filename, out var transfer))
                {
                    transfer.CancelRequest();
                    _activeTransfers.Remove(filename);
                    Debug.Log($"[EpicNet TitleStorage] Cancelled download: {filename}");
                }
            }
        }

        /// <summary>
        /// Clears the file cache.
        /// </summary>
        /// <param name="filename">Optional specific filename to clear. If null, clears all.</param>
        public static void ClearCache(string filename = null)
        {
            lock (_cacheLock)
            {
                if (filename == null)
                {
                    _fileCache.Clear();
                    Debug.Log("[EpicNet TitleStorage] Cache cleared");
                }
                else
                {
                    _fileCache.Remove(filename);
                    Debug.Log($"[EpicNet TitleStorage] Cleared cache for: {filename}");
                }
            }
        }

        /// <summary>
        /// Checks if a file is cached.
        /// </summary>
        public static bool IsCached(string filename)
        {
            lock (_cacheLock)
            {
                return _fileCache.ContainsKey(filename);
            }
        }

        #endregion
    }

    /// <summary>
    /// Metadata for a Title Storage file.
    /// </summary>
    public struct TitleFileMetadata
    {
        /// <summary>The filename.</summary>
        public string Filename;

        /// <summary>File size in bytes.</summary>
        public uint FileSizeBytes;

        /// <summary>MD5 hash for integrity verification.</summary>
        public string MD5Hash;
    }
}
