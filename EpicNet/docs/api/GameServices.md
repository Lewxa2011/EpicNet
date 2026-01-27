# Game Services

EpicNet provides wrappers for EOS game services: Stats, Leaderboards, Achievements, and Cloud Save.

---

# EpicStats

`public static class EpicStats`

Player statistics tracking using EOS Stats. Track kills, deaths, wins, playtime, and other numerical stats.

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsInitialized` | `bool` | - | Whether Stats is ready |
| `BatchUpdates` | `bool` | `true` | Batch stat updates |
| `BatchFlushInterval` | `float` | `30f` | Seconds between flushes |

## Events

```csharp
public static event Action<Dictionary<string, int>> OnStatsQueried;
public static event Action<string, int> OnStatIngested;
```

## Setup

Stats must be configured in the [EOS Developer Portal](https://dev.epicgames.com):

1. Go to your product → Game Services → Stats
2. Create stats with appropriate aggregation types:
   - **SUM** - Accumulates values (kills, deaths, playtime)
   - **MAX** - Keeps highest value (high score)
   - **MIN** - Keeps lowest value (fastest time)
   - **LATEST** - Keeps most recent value

## Methods

### IngestStat

```csharp
public static void IngestStat(string statName, int amount, Action<bool> callback = null)
```

Records a stat value.

```csharp
// After getting a kill
EpicStats.IngestStat("KILLS", 1);

// After winning a match
EpicStats.IngestStat("WINS", 1);
EpicStats.IngestStat("MATCHES_PLAYED", 1);

// Track playtime (call periodically)
EpicStats.IngestStat("PLAYTIME_SECONDS", 60);
```

### IngestStats

```csharp
public static void IngestStats(Dictionary<string, int> stats, Action<bool> callback = null)
```

Records multiple stats at once.

```csharp
EpicStats.IngestStats(new Dictionary<string, int>
{
    { "KILLS", kills },
    { "DEATHS", deaths },
    { "SCORE", score }
});
```

### QueryStats

```csharp
public static void QueryStats(string[] statNames, Action<Dictionary<string, int>> callback)
public static void QueryStats(ProductUserId targetUserId, string[] statNames, Action<Dictionary<string, int>> callback)
```

Retrieves stat values.

```csharp
EpicStats.QueryStats(new[] { "KILLS", "DEATHS", "WINS" }, stats =>
{
    Debug.Log($"K/D: {stats["KILLS"]}/{stats["DEATHS"]}");
    Debug.Log($"Wins: {stats["WINS"]}");
});
```

### FlushPendingStats

```csharp
public static void FlushPendingStats(Action<bool> callback = null)
```

Immediately sends batched stats to server.

```csharp
// Force flush before leaving
void OnApplicationPause(bool paused)
{
    if (paused) EpicStats.FlushPendingStats();
}
```

### GetCachedStat

```csharp
public static int GetCachedStat(string statName)
public static Dictionary<string, int> GetAllCachedStats()
```

Gets locally cached stat values (no network request).

---

# EpicLeaderboards

`public static class EpicLeaderboards`

Competitive rankings using EOS Leaderboards.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsInitialized` | `bool` | Whether Leaderboards is ready |

## Events

```csharp
public static event Action<string, List<LeaderboardEntry>> OnLeaderboardReceived;
public static event Action<string, LeaderboardEntry> OnPlayerRankReceived;
```

## Setup

Leaderboards are configured in the EOS Developer Portal and linked to Stats.

## Methods

### GetLeaderboard

```csharp
public static void GetLeaderboard(string leaderboardId, int startRank, int count, Action<List<LeaderboardEntry>> callback)
```

Gets leaderboard entries by rank range.

```csharp
// Get top 10
EpicLeaderboards.GetLeaderboard("HighScores", 0, 10, entries =>
{
    foreach (var entry in entries)
    {
        Debug.Log($"#{entry.Rank} {entry.DisplayName}: {entry.Score}");
    }
});
```

### GetLeaderboardAroundPlayer

```csharp
public static void GetLeaderboardAroundPlayer(string leaderboardId, int range, Action<List<LeaderboardEntry>> callback)
```

Gets entries around the local player.

```csharp
// Get 5 above and 5 below the player
EpicLeaderboards.GetLeaderboardAroundPlayer("HighScores", 5, entries =>
{
    foreach (var entry in entries)
    {
        bool isMe = entry.UserId == EpicNetwork.LocalPlayer.UserId;
        Debug.Log($"{(isMe ? ">>> " : "")}#{entry.Rank} {entry.DisplayName}: {entry.Score}");
    }
});
```

### GetPlayerRank

```csharp
public static void GetPlayerRank(string leaderboardId, Action<LeaderboardEntry?> callback)
```

Gets the local player's rank.

```csharp
EpicLeaderboards.GetPlayerRank("HighScores", entry =>
{
    if (entry.HasValue)
        Debug.Log($"Your rank: #{entry.Value.Rank}");
    else
        Debug.Log("Not ranked yet");
});
```

## LeaderboardEntry Struct

```csharp
public struct LeaderboardEntry
{
    public ProductUserId UserId;
    public string DisplayName;
    public int Rank;
    public int Score;
}
```

---

# EpicAchievements

`public static class EpicAchievements`

Achievement tracking and unlocking using EOS Achievements.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsInitialized` | `bool` | Whether Achievements is ready |
| `TotalAchievements` | `int` | Total achievements defined |
| `UnlockedCount` | `int` | Number unlocked by player |

## Events

```csharp
public static event Action<string, AchievementData> OnAchievementUnlocked;
public static event Action<string, float> OnAchievementProgress;
public static event Action<List<AchievementData>> OnAchievementsQueried;
```

## Setup

Configure achievements in the EOS Developer Portal. They can be linked to Stats for automatic unlocking.

## Methods

### QueryDefinitions

```csharp
public static void QueryDefinitions(Action<bool> callback = null)
```

Loads achievement definitions. Call once at startup.

```csharp
void Start()
{
    EpicAchievements.QueryDefinitions(success =>
    {
        Debug.Log($"Loaded {EpicAchievements.TotalAchievements} achievements");
    });
}
```

### QueryAchievements

```csharp
public static void QueryAchievements(Action<List<AchievementData>> callback)
```

Gets player's achievement progress.

```csharp
EpicAchievements.QueryAchievements(achievements =>
{
    foreach (var ach in achievements)
    {
        if (ach.IsUnlocked)
            Debug.Log($"[UNLOCKED] {ach.DisplayName}");
        else
            Debug.Log($"[{ach.Progress:P0}] {ach.DisplayName}");
    }
});
```

### Unlock

```csharp
public static void Unlock(string achievementId, Action<bool> callback = null)
public static void UnlockMultiple(string[] achievementIds, Action<bool> callback = null)
```

Unlocks achievements.

```csharp
// Single achievement
EpicAchievements.Unlock("FIRST_WIN", success =>
{
    if (success) ShowAchievementPopup("First Win!");
});

// Multiple achievements
EpicAchievements.UnlockMultiple(new[] { "COMPLETE_TUTORIAL", "FIRST_KILL" });
```

### Helper Methods

```csharp
public static AchievementData? GetAchievement(string achievementId)
public static bool IsUnlocked(string achievementId)
public static List<AchievementData> GetAllAchievements()
public static AchievementDefinition? GetDefinition(string achievementId)
```

## AchievementData Struct

```csharp
public struct AchievementData
{
    public string Id;
    public string DisplayName;
    public string Description;
    public string IconUrl;
    public float Progress;      // 0 to 1
    public bool IsUnlocked;
    public DateTime? UnlockTime;
    public bool IsHidden;
}
```

---

# EpicPlayerData

`public static class EpicPlayerData`

Cloud save functionality using EOS Player Data Storage.

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsInitialized` | `bool` | - | Whether storage is ready |
| `CacheEnabled` | `bool` | `true` | Use local caching |
| `AutoSync` | `bool` | `true` | Auto-sync to cloud |

## Events

```csharp
public static event Action<string, bool> OnFileSaved;
public static event Action<string, bool, string> OnFileLoaded;
public static event Action<string, bool> OnFileDeleted;
public static event Action<List<string>> OnFileListReceived;
```

## Methods

### SaveFile / SaveFileBytes

```csharp
public static void SaveFile(string filename, string content, Action<bool> callback = null)
public static void SaveFileBytes(string filename, byte[] data, Action<bool> callback = null)
```

Saves data to cloud storage.

```csharp
// Save string
EpicPlayerData.SaveFile("settings.json", jsonSettings);

// Save bytes
EpicPlayerData.SaveFileBytes("save.dat", binaryData);
```

### SaveJson<T>

```csharp
public static void SaveJson<T>(string filename, T data, Action<bool> callback = null)
```

Serializes and saves an object.

```csharp
[System.Serializable]
public class PlayerInventory
{
    public int coins;
    public int gems;
    public List<string> items;
}

var inventory = new PlayerInventory { coins = 500, gems = 25 };
EpicPlayerData.SaveJson("inventory.json", inventory);
```

### LoadFile / LoadFileBytes

```csharp
public static void LoadFile(string filename, Action<bool, string> callback, bool useCache = true)
public static void LoadFileBytes(string filename, Action<bool, byte[]> callback, bool useCache = true)
```

Loads data from cloud storage.

```csharp
EpicPlayerData.LoadFile("settings.json", (success, content) =>
{
    if (success)
        ApplySettings(JsonUtility.FromJson<Settings>(content));
});
```

### LoadJson<T>

```csharp
public static void LoadJson<T>(string filename, Action<bool, T> callback, T defaultValue = default)
```

Loads and deserializes an object.

```csharp
EpicPlayerData.LoadJson<PlayerInventory>("inventory.json", (success, inventory) =>
{
    if (success)
        this.inventory = inventory;
    else
        this.inventory = new PlayerInventory(); // Default
});
```

### DeleteFile

```csharp
public static void DeleteFile(string filename, Action<bool> callback = null)
```

Deletes a file from cloud storage.

### GetFileList

```csharp
public static void GetFileList(Action<List<string>> callback)
```

Lists all saved files.

```csharp
EpicPlayerData.GetFileList(files =>
{
    foreach (var file in files)
        Debug.Log($"Saved file: {file}");
});
```

### FileExists

```csharp
public static void FileExists(string filename, Action<bool> callback)
```

Checks if a file exists.

### Cache Methods

```csharp
public static void ClearCache()
public static bool TryGetCached(string filename, out string content)
```

---

## Complete Example

```csharp
public class GameServices : MonoBehaviour
{
    void Start()
    {
        // Load achievements
        EpicAchievements.QueryDefinitions();
        EpicAchievements.QueryAchievements(OnAchievementsLoaded);

        // Load player data
        EpicPlayerData.LoadJson<SaveData>("save.json", OnSaveLoaded);

        // Query stats
        EpicStats.QueryStats(new[] { "KILLS", "DEATHS", "WINS" }, OnStatsLoaded);
    }

    void OnMatchEnd(int kills, int deaths, bool won)
    {
        // Update stats
        EpicStats.IngestStats(new Dictionary<string, int>
        {
            { "KILLS", kills },
            { "DEATHS", deaths },
            { "MATCHES_PLAYED", 1 },
            { "WINS", won ? 1 : 0 }
        });

        // Check achievements
        if (won && kills >= 10)
            EpicAchievements.Unlock("PERFECT_GAME");

        // Save progress
        EpicPlayerData.SaveJson("save.json", currentSaveData);
    }

    void ShowLeaderboard()
    {
        EpicLeaderboards.GetLeaderboard("HighScores", 0, 100, DisplayLeaderboard);
    }
}
```
