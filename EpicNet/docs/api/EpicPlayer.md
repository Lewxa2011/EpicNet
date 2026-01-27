# EpicPlayer

`public class EpicPlayer`

Represents a player connected to the network. Access the local player via `EpicNetwork.LocalPlayer` and all players via `EpicNetwork.PlayerList`.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `UserId` | `ProductUserId` | Unique EOS Product User ID |
| `NickName` | `string` | Display name |
| `ActorNumber` | `int` | Unique session number (for ordering) |
| `IsMasterClient` | `bool` | Whether this player is the room host |
| `IsLocal` | `bool` | Whether this is the local player |
| `CustomProperties` | `Dictionary<string, object>` | Synced player properties |

---

## Usage

### Accessing Players

```csharp
// Local player
EpicPlayer me = EpicNetwork.LocalPlayer;
Debug.Log($"My name: {me.NickName}");

// Master client (host)
EpicPlayer host = EpicNetwork.MasterClient;
Debug.Log($"Host: {host.NickName}");

// All players in room
foreach (var player in EpicNetwork.PlayerList)
{
    Debug.Log($"Player: {player.NickName} (Actor #{player.ActorNumber})");
}

// From current room
foreach (var player in EpicNetwork.CurrentRoom.Players)
{
    Debug.Log(player.NickName);
}
```

### Checking Player Identity

```csharp
void OnPlayerEnteredRoom(EpicPlayer player)
{
    if (player.IsLocal)
    {
        // This is us joining
        return;
    }

    if (player.IsMasterClient)
    {
        Debug.Log("The host joined");
    }
}
```

### Custom Properties

Custom properties are synchronized across all players.

```csharp
// Set local player properties
EpicNetwork.SetLocalPlayerProperties(new Dictionary<string, object>
{
    { "Team", "Red" },
    { "Ready", true },
    { "Score", 0 }
});

// Read any player's properties
void ShowPlayerInfo(EpicPlayer player)
{
    if (player.CustomProperties.TryGetValue("Team", out object team))
    {
        Debug.Log($"{player.NickName} is on team {team}");
    }
}
```

---

## Comparing Players

Players are compared by their `UserId`, so two `EpicPlayer` instances for the same EOS user are equal:

```csharp
if (somePlayer == EpicNetwork.LocalPlayer)
{
    Debug.Log("That's me!");
}

// Or by reference
if (somePlayer.UserId.Equals(EpicNetwork.MasterClient.UserId))
{
    Debug.Log("That's the host");
}
```

---

## Example: Team Selection UI

```csharp
public class TeamSelector : EpicMonoBehaviourCallbacks
{
    public void SelectTeam(string teamName)
    {
        EpicNetwork.SetLocalPlayerProperties(new Dictionary<string, object>
        {
            { "Team", teamName }
        });
    }

    public void CheckAllPlayersReady()
    {
        foreach (var player in EpicNetwork.PlayerList)
        {
            if (!player.CustomProperties.TryGetValue("Ready", out object ready)
                || !(bool)ready)
            {
                Debug.Log($"{player.NickName} is not ready");
                return;
            }
        }
        Debug.Log("All players ready!");
    }

    public override void OnPlayerEnteredRoom(EpicPlayer newPlayer)
    {
        // Show in UI
        AddPlayerToList(newPlayer.NickName);
    }

    public override void OnPlayerLeftRoom(EpicPlayer player)
    {
        // Remove from UI
        RemovePlayerFromList(player.NickName);
    }
}
```

---

# EpicRoom

`public class EpicRoom`

Represents the current room/lobby. Access via `EpicNetwork.CurrentRoom`.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Room name/identifier |
| `PlayerCount` | `int` | Current number of players |
| `MaxPlayers` | `int` | Maximum allowed players |
| `IsOpen` | `bool` | Whether accepting new players |
| `IsVisible` | `bool` | Whether visible in room listings |
| `CustomProperties` | `Dictionary<string, object>` | Room properties |
| `Players` | `List<EpicPlayer>` | All players in the room |

## Usage

```csharp
public override void OnJoinedRoom()
{
    EpicRoom room = EpicNetwork.CurrentRoom;

    Debug.Log($"Room: {room.Name}");
    Debug.Log($"Players: {room.PlayerCount}/{room.MaxPlayers}");
    Debug.Log($"Open: {room.IsOpen}, Visible: {room.IsVisible}");

    // Read custom properties
    if (room.CustomProperties.TryGetValue("GameMode", out object mode))
    {
        Debug.Log($"Mode: {mode}");
    }
}
```

---

# EpicRoomInfo

`public class EpicRoomInfo`

Read-only information about a room from the lobby list. Used when browsing available rooms.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Room name |
| `PlayerCount` | `int` | Current players |
| `MaxPlayers` | `int` | Max players |
| `IsOpen` | `bool` | Accepting joins |
| `IsVisible` | `bool` | In public listings |
| `HasPassword` | `bool` | Password protected |
| `CustomProperties` | `Dictionary<string, object>` | Lobby-visible properties |

## Usage

```csharp
public override void OnRoomListUpdate(List<EpicRoomInfo> rooms)
{
    foreach (var room in rooms)
    {
        if (!room.IsOpen) continue;
        if (room.PlayerCount >= room.MaxPlayers) continue;

        Debug.Log($"{room.Name} ({room.PlayerCount}/{room.MaxPlayers})");

        if (room.HasPassword)
            Debug.Log("  [Password Protected]");

        if (room.CustomProperties.TryGetValue("Map", out object map))
            Debug.Log($"  Map: {map}");
    }
}
```

---

# EpicRoomOptions

`public class EpicRoomOptions`

Configuration for creating a new room.

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxPlayers` | `int` | `20` | Max players (up to 64) |
| `IsVisible` | `bool` | `true` | Show in room listings |
| `IsOpen` | `bool` | `true` | Accept new players |
| `Password` | `string` | `null` | Room password |
| `CustomRoomProperties` | `Dictionary<string, object>` | - | Custom properties |
| `CustomRoomPropertiesForLobby` | `string[]` | - | Properties visible in listings |

## Usage

```csharp
var options = new EpicRoomOptions
{
    MaxPlayers = 8,
    IsVisible = true,
    IsOpen = true,
    Password = "secret123",
    CustomRoomProperties = new Dictionary<string, object>
    {
        { "GameMode", "TeamDeathmatch" },
        { "Map", "Castle" },
        { "RoundTime", 300 }
    },
    // Only these will be visible in GetRoomList results
    CustomRoomPropertiesForLobby = new[] { "GameMode", "Map" }
};

EpicNetwork.CreateRoom("MyGame", options);
```
