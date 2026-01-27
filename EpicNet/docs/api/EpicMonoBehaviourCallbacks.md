# EpicMonoBehaviourCallbacks

`public abstract class EpicMonoBehaviourCallbacks : MonoBehaviour`

Base class for MonoBehaviours that need to respond to network events. Similar to Photon's `MonoBehaviourPunCallbacks`.

## Usage

Inherit from this class and override the callback methods you need:

```csharp
public class GameManager : EpicMonoBehaviourCallbacks
{
    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected! Ready to join rooms.");
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined room: {EpicNetwork.CurrentRoom.Name}");
        SpawnPlayer();
    }

    public override void OnPlayerEnteredRoom(EpicPlayer newPlayer)
    {
        Debug.Log($"{newPlayer.NickName} joined");
    }
}
```

---

## Automatic Subscription

Callbacks are automatically subscribed in `OnEnable()` and unsubscribed in `OnDisable()`. This prevents memory leaks and ensures events only fire when the component is active.

If you override `OnEnable()` or `OnDisable()`, call the base method:

```csharp
protected override void OnEnable()
{
    base.OnEnable(); // Subscribe to events
    // Your code here
}

protected override void OnDisable()
{
    base.OnDisable(); // Unsubscribe from events
    // Your code here
}
```

---

## Callback Methods

### Connection Callbacks

#### OnConnectedToMaster

```csharp
public virtual void OnConnectedToMaster()
```

Called after successfully connecting to EOS services. At this point you can create or join rooms.

```csharp
public override void OnConnectedToMaster()
{
    // Show lobby UI or auto-join
    EpicNetwork.GetRoomList();
}
```

#### OnJoinedRoom

```csharp
public virtual void OnJoinedRoom()
```

Called after successfully joining or creating a room.

```csharp
public override void OnJoinedRoom()
{
    // Spawn player, start game
    var player = EpicNetwork.Instantiate("Player", spawnPoint, Quaternion.identity);
}
```

#### OnLeftRoom

```csharp
public virtual void OnLeftRoom()
```

Called after leaving the current room.

```csharp
public override void OnLeftRoom()
{
    // Return to menu
    SceneManager.LoadScene("MainMenu");
}
```

#### OnJoinRoomFailed

```csharp
public virtual void OnJoinRoomFailed(string reason)
```

Called when joining a room fails.

```csharp
public override void OnJoinRoomFailed(string reason)
{
    Debug.LogError($"Failed to join: {reason}");
    ShowErrorPopup(reason);
}
```

---

### Player Callbacks

#### OnPlayerEnteredRoom

```csharp
public virtual void OnPlayerEnteredRoom(EpicPlayer newPlayer)
```

Called when another player joins the room.

```csharp
public override void OnPlayerEnteredRoom(EpicPlayer newPlayer)
{
    chatLog.Add($"{newPlayer.NickName} joined the game");
    UpdatePlayerList();
}
```

#### OnPlayerLeftRoom

```csharp
public virtual void OnPlayerLeftRoom(EpicPlayer otherPlayer)
```

Called when another player leaves the room.

```csharp
public override void OnPlayerLeftRoom(EpicPlayer otherPlayer)
{
    chatLog.Add($"{otherPlayer.NickName} left the game");
    UpdatePlayerList();
}
```

#### OnMasterClientSwitched

```csharp
public virtual void OnMasterClientSwitched(EpicPlayer newMaster)
```

Called when the master client (host) changes due to host migration.

```csharp
public override void OnMasterClientSwitched(EpicPlayer newMaster)
{
    if (newMaster.IsLocal)
    {
        Debug.Log("I am now the host!");
        // Take over host duties
    }
}
```

---

### Room List Callback

#### OnRoomListUpdate

```csharp
public virtual void OnRoomListUpdate(List<EpicRoomInfo> roomList)
```

Called when the room list is updated after calling `EpicNetwork.GetRoomList()`.

```csharp
public override void OnRoomListUpdate(List<EpicRoomInfo> roomList)
{
    ClearRoomListUI();
    foreach (var room in roomList)
    {
        AddRoomButton(room.Name, room.PlayerCount, room.MaxPlayers);
    }
}
```

---

### Reconnection Callbacks

#### OnReconnecting

```csharp
public virtual void OnReconnecting()
```

Called when the client is attempting to reconnect.

```csharp
public override void OnReconnecting()
{
    reconnectingUI.SetActive(true);
}
```

#### OnReconnected

```csharp
public virtual void OnReconnected()
```

Called when reconnection succeeds.

```csharp
public override void OnReconnected()
{
    reconnectingUI.SetActive(false);
    Debug.Log("Reconnected successfully!");
}
```

#### OnReconnectFailed

```csharp
public virtual void OnReconnectFailed()
```

Called when all reconnection attempts have failed.

```csharp
public override void OnReconnectFailed()
{
    reconnectingUI.SetActive(false);
    ShowDisconnectedPopup();
}
```

---

### Moderation Callback

#### OnKicked

```csharp
public virtual void OnKicked(string reason)
```

Called when the local player is kicked from the room by the master client.

```csharp
public override void OnKicked(string reason)
{
    ShowPopup($"You were kicked: {reason}");
    SceneManager.LoadScene("MainMenu");
}
```

---

## Complete Example

```csharp
public class LobbyManager : EpicMonoBehaviourCallbacks
{
    [SerializeField] private Transform roomListParent;
    [SerializeField] private GameObject roomButtonPrefab;
    [SerializeField] private GameObject reconnectingPanel;
    [SerializeField] private Text statusText;

    void Start()
    {
        statusText.text = "Connecting...";
    }

    public override void OnConnectedToMaster()
    {
        statusText.text = "Connected! Fetching rooms...";
        EpicNetwork.GetRoomList();
    }

    public override void OnRoomListUpdate(List<EpicRoomInfo> roomList)
    {
        // Clear old list
        foreach (Transform child in roomListParent)
            Destroy(child.gameObject);

        // Populate new list
        foreach (var room in roomList)
        {
            var button = Instantiate(roomButtonPrefab, roomListParent);
            button.GetComponentInChildren<Text>().text =
                $"{room.Name} ({room.PlayerCount}/{room.MaxPlayers})";
            button.GetComponent<Button>().onClick.AddListener(() =>
                EpicNetwork.JoinRoom(room.Name));
        }

        statusText.text = $"Found {roomList.Count} rooms";
    }

    public override void OnJoinedRoom()
    {
        SceneManager.LoadScene("GameScene");
    }

    public override void OnJoinRoomFailed(string reason)
    {
        statusText.text = $"Failed: {reason}";
    }

    public override void OnReconnecting()
    {
        reconnectingPanel.SetActive(true);
    }

    public override void OnReconnected()
    {
        reconnectingPanel.SetActive(false);
    }

    public override void OnReconnectFailed()
    {
        reconnectingPanel.SetActive(false);
        statusText.text = "Connection lost";
    }

    public void CreateRoom(string roomName)
    {
        var options = new EpicRoomOptions { MaxPlayers = 4 };
        EpicNetwork.CreateRoom(roomName, options);
    }

    public void RefreshRoomList()
    {
        EpicNetwork.GetRoomList();
    }
}
```

---

## Alternative: Direct Event Subscription

For non-MonoBehaviour classes, subscribe directly to events:

```csharp
public class NetworkEvents
{
    public void Subscribe()
    {
        EpicNetwork.OnConnectedToMaster += HandleConnected;
        EpicNetwork.OnJoinedRoom += HandleJoinedRoom;
        EpicNetwork.OnPlayerEnteredRoom += HandlePlayerEntered;
    }

    public void Unsubscribe()
    {
        EpicNetwork.OnConnectedToMaster -= HandleConnected;
        EpicNetwork.OnJoinedRoom -= HandleJoinedRoom;
        EpicNetwork.OnPlayerEnteredRoom -= HandlePlayerEntered;
    }

    void HandleConnected() { }
    void HandleJoinedRoom() { }
    void HandlePlayerEntered(EpicPlayer player) { }
}
```
