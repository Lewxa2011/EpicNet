# EpicNet Documentation

**EpicNet** is a high-level multiplayer networking framework for Unity built on Epic Online Services (EOS). It provides a Photon PUN2-like API, making it easy to add multiplayer functionality to your game.

## Features

- **Simple API** - Familiar patterns if you've used Photon PUN2
- **P2P Networking** - Direct peer-to-peer with EOS relay fallback for NAT traversal
- **Room Management** - Create, join, and manage game lobbies with password protection
- **Object Synchronization** - Automatic transform and custom state synchronization
- **RPC System** - Remote procedure calls with security levels and buffering
- **Host Migration** - Automatic master client promotion when the host leaves
- **Voice Chat** - Built-in Opus-encoded voice with 3D spatial audio
- **Object Pooling** - High-performance object recycling to reduce GC
- **Game Services** - Stats, Leaderboards, Achievements, and Cloud Save

## Quick Start

```csharp
using EpicNet;
using UnityEngine;

public class GameManager : EpicMonoBehaviourCallbacks
{
    void Start()
    {
        // Login and connect
        EpicNetwork.LoginWithDeviceId("PlayerName", (success, msg) =>
        {
            if (success) EpicNetwork.ConnectUsingSettings();
        });
    }

    void Update()
    {
        // Process network messages
        EpicNetwork.Update();
    }

    public override void OnConnectedToMaster()
    {
        // Join or create a room
        EpicNetwork.JoinRoom("MyRoom", createFallback: true);
    }

    public override void OnJoinedRoom()
    {
        // Spawn your player
        EpicNetwork.Instantiate("PlayerPrefab", Vector3.zero, Quaternion.identity);
    }
}
```

## Documentation

### Getting Started
- [Installation & Setup](getting-started.md)
- [Creating Your First Multiplayer Game](tutorial-basics.md)

### Core Concepts
- [Rooms & Matchmaking](guides/rooms.md)
- [Network Objects & Views](guides/network-objects.md)
- [RPCs (Remote Procedure Calls)](guides/rpcs.md)
- [State Synchronization](guides/synchronization.md)
- [Ownership & Authority](guides/ownership.md)

### Advanced Features
- [Voice Chat](guides/voice-chat.md)
- [Object Pooling](guides/pooling.md)
- [Host Migration](guides/host-migration.md)
- [Reconnection](guides/reconnection.md)

### Game Services
- [Player Stats](guides/stats.md)
- [Leaderboards](guides/leaderboards.md)
- [Achievements](guides/achievements.md)
- [Cloud Save](guides/cloud-save.md)

### API Reference
- [EpicNetwork](api/EpicNetwork.md) - Central networking manager
- [EpicView](api/EpicView.md) - Network object identifier
- [EpicPlayer](api/EpicPlayer.md) - Player representation
- [EpicRoom](api/EpicRoom.md) - Room/lobby container
- [EpicStream](api/EpicStream.md) - Data serialization
- [EpicPool](api/EpicPool.md) - Object pooling
- [EpicVC](api/EpicVC.md) - Voice chat

## Requirements

- Unity 2021.3 or later
- [Epic Online Services Plugin for Unity](https://github.com/PlayEveryWare/eos_plugin_for_unity)
- [Concentus Unity](https://github.com/adrenak/concentus-unity) (for voice chat - Opus codec)
- EOS Developer Portal account with configured product

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                     Your Game Code                       │
├─────────────────────────────────────────────────────────┤
│  EpicNetwork  │  EpicView  │  EpicVC  │  EpicPool      │
├─────────────────────────────────────────────────────────┤
│  EpicStats │ EpicLeaderboards │ EpicPlayerData │ ...   │
├─────────────────────────────────────────────────────────┤
│              Epic Online Services (EOS)                  │
└─────────────────────────────────────────────────────────┘
```

## License

EpicNet is provided as-is for use in your Unity projects.
