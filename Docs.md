# EpicNet

## Project Title and Description
**EpicNet** is a Unity-based networking solution designed for multiplayer games. It provides a set of features that facilitate networking functions such as player management, room/lobby handling, object pooling, voice communication, and object synchronization. The goal of EpicNet is to simplify the development of real-time multiplayer experiences by supplying a robust and flexible architecture.

## Features
- **Player Management**: The `EpicPlayer` class represents players in the network, managing properties like `UserId`, `NickName`, and `ActorNumber`.
- **Room/Lobby Management**: Supports the creation and management of game rooms (lobbies) with customizable properties.
- **Object Pooling**: Implemented via the `EpicPool` class to reduce overhead and improve performance when instantiating objects.
- **Voice Communication**: Supports real-time voice chat using Opus codec, allowing players to communicate effectively.
- **Synchronization**: `EpicTransformView` synchronizes transforms (position, rotation, scale) over the network with options for interpolation and extrapolation.
- **Remote Procedure Calls (RPC)**: Supports the invocation of methods across the network, making it easy to implement game logic between players.

## Installation and Setup
Check out the [README](README) for info on Setup.

## Basic Usage
Here is a brief example of how to use some core functionalities of EpicNet:

### Setting Up a Room
```csharp
var roomOptions = new EpicRoomOptions
{
    MaxPlayers = 4,
    IsVisible = true,
    IsOpen = true
};
EpicNetwork.CreateRoom("Game Room 1", roomOptions);
```

### Joining a Room
```csharp
EpicNetwork.JoinRoom("Game Room 1");
```

### Using Object Pooling
```csharp
// Prewarm a pool with instances of a prefab
EpicPool.Prewarm("PlayerPrefab", 10);

// Retrieve an object from the pool
GameObject playerObject = EpicPool.Get("PlayerPrefab", spawnPosition, spawnRotation);
```

### Voice Chat Setup
Make sure to initiate the `EpicVCMgr` to start using voice communication features:
```csharp
EpicVCMgr.Initialize();
```

## Configuration
### Room Options
When creating a room, you can configure the following properties using the `EpicRoomOptions` class:
- `MaxPlayers`: Maximum number of players allowed in the room.
- `IsVisible`: Determines if the room is visible in the lobby.
- `IsOpen`: Indicates if the room is open for joining.

### Voice Chat Permissions
On Android, you need to request microphone permission. Ensure that the permission management is implemented as shown in the `EpicVCMgr` class.

## Contributing Guidelines
Contributions are welcome! If you would like to contribute to **EpicNet**, please fork the repository, make your changes, and submit a pull request. To maintain code quality, adhere to the following guidelines:
- Write clear and concise comments for your code.
- Ensure code follows the existing style for consistency.
- Test any feature or bug fix thoroughly before submitting a request.

## License
This project is licensed under The Unlicense License. See the [LICENSE](LICENSE) file for more details.
