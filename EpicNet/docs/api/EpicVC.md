# Voice Chat

EpicNet includes built-in voice chat with Opus codec compression and 3D spatial audio.

## Components

| Class | Description |
|-------|-------------|
| `EpicVCMgr` | Static manager for initialization and permissions |
| `EpicVC` | Component for voice transmission/reception |

---

# EpicVCMgr

`public static class EpicVCMgr`

Manages voice chat initialization and microphone permissions.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsInitialized` | `bool` | Whether voice chat is ready |
| `HasMicrophonePermission` | `bool` | Whether mic access is granted |
| `CurrentDevice` | `string` | Current microphone device name |

## Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `sr` | `24000` Hz | Sample rate (24 kHz) |
| `VoiceActivationLevel` | `0.07f` | RMS threshold for VAD |

## Events

```csharp
public static event Action OnInitialized;
public static event Action OnPermissionDenied;
```

## Methods

### Initialize

```csharp
public static void Initialize()
```

Initializes voice chat and requests microphone permission on Android.

```csharp
void Start()
{
    EpicVCMgr.OnInitialized += OnVoiceChatReady;
    EpicVCMgr.OnPermissionDenied += OnMicPermissionDenied;
    EpicVCMgr.Initialize();
}

void OnVoiceChatReady()
{
    Debug.Log("Voice chat ready!");
}

void OnMicPermissionDenied()
{
    Debug.Log("Microphone access denied - voice chat disabled");
}
```

### SetDevice

```csharp
public static bool SetDevice(string deviceName)
```

Changes the active microphone.

```csharp
// Get available microphones
string[] devices = EpicVCMgr.GetAvailableDevices();
foreach (var device in devices)
{
    Debug.Log($"Microphone: {device}");
}

// Switch to a specific microphone
EpicVCMgr.SetDevice("USB Headset Microphone");
```

### GetAvailableDevices

```csharp
public static string[] GetAvailableDevices()
```

Returns all available microphone device names.

---

# EpicVC

`public class EpicVC : MonoBehaviour`

Voice chat component. Add to player prefabs with an `EpicView`.

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsMuted` | `bool` | `false` | Whether output is muted |
| `IsTransmitting` | `bool` | - | Whether currently sending voice |
| `Volume` | `float` | `1.0f` | Playback volume (0-1) |
| `Enable3DAudio` | `bool` | `true` | Use spatial audio |
| `MaxDistance` | `float` | `50f` | 3D audio max distance |
| `MinDistance` | `float` | `1f` | 3D audio min distance |

## Requirements

1. **Concentus Unity** package - Install [com.adrenak.concentus-unity](https://github.com/adrenak/concentus-unity) via UPM (C# port of Opus codec)
2. `EpicView` component on the same GameObject
3. `AudioSource` component (auto-added if missing)
4. `EpicVCMgr.Initialize()` called before use

### Installing Concentus

Add to your `Packages/manifest.json`:
```json
{
  "dependencies": {
    "com.adrenak.concentus-unity": "https://github.com/adrenak/concentus-unity.git"
  }
}
```

Or via Package Manager: `Add package from git URL` → `https://github.com/adrenak/concentus-unity.git`

---

## Setup

### 1. Initialize Voice Chat

```csharp
public class GameManager : MonoBehaviour
{
    void Start()
    {
        EpicVCMgr.Initialize();
    }
}
```

### 2. Add to Player Prefab

1. Add `EpicView` component
2. Add `EpicVC` component
3. Configure 3D audio settings

### 3. Control Voice Chat

```csharp
public class VoiceChatUI : MonoBehaviour
{
    private EpicVC voiceChat;

    void Start()
    {
        // Find the local player's voice component
        voiceChat = FindLocalPlayerVC();
    }

    public void ToggleMute()
    {
        voiceChat.IsMuted = !voiceChat.IsMuted;
    }

    public void SetVolume(float volume)
    {
        voiceChat.Volume = volume;
    }
}
```

---

## 3D Spatial Audio

When `Enable3DAudio` is true, voice volume and panning are based on distance and direction:

```csharp
public class PlayerVoice : MonoBehaviour
{
    private EpicVC voiceChat;

    void Awake()
    {
        voiceChat = GetComponent<EpicVC>();

        // Configure 3D audio
        voiceChat.Enable3DAudio = true;
        voiceChat.MinDistance = 2f;   // Full volume within 2m
        voiceChat.MaxDistance = 30f;  // Silent beyond 30m
    }
}
```

---

## Voice Activity Detection (VAD)

EpicVC uses VAD to only transmit when the player is speaking:

- Audio below `VoiceActivationLevel` (0.07 RMS) is not sent
- Reduces bandwidth when players are silent
- Configurable via `EpicVCMgr.VoiceActivationLevel`

---

## Push-to-Talk

Implement push-to-talk by controlling the `AudioSource`:

```csharp
public class PushToTalk : MonoBehaviour
{
    private EpicVC voiceChat;
    private AudioSource audioSource;

    void Awake()
    {
        voiceChat = GetComponent<EpicVC>();
        audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        // Only transmit while holding V
        audioSource.mute = !Input.GetKey(KeyCode.V);
    }
}
```

---

## Example: Complete Voice Chat Setup

```csharp
public class VoiceChatController : EpicMonoBehaviourCallbacks
{
    public Slider volumeSlider;
    public Toggle muteToggle;
    public Text statusText;

    private EpicVC localVC;

    void Start()
    {
        EpicVCMgr.OnInitialized += OnVoiceReady;
        EpicVCMgr.OnPermissionDenied += OnPermissionDenied;
        EpicVCMgr.Initialize();
    }

    void OnVoiceReady()
    {
        statusText.text = $"Mic: {EpicVCMgr.CurrentDevice}";
    }

    void OnPermissionDenied()
    {
        statusText.text = "Microphone access denied";
    }

    public override void OnJoinedRoom()
    {
        // Spawn player with voice chat
        var player = EpicNetwork.Instantiate("Player", Vector3.zero, Quaternion.identity);
        localVC = player.GetComponent<EpicVC>();

        // Connect UI
        volumeSlider.onValueChanged.AddListener(v => localVC.Volume = v);
        muteToggle.onValueChanged.AddListener(m => localVC.IsMuted = m);
    }

    public void ChangeMicrophone(int index)
    {
        var devices = EpicVCMgr.GetAvailableDevices();
        if (index < devices.Length)
        {
            EpicVCMgr.SetDevice(devices[index]);
            statusText.text = $"Mic: {devices[index]}";
        }
    }
}
```

---

## Technical Details

### Codec

- **Opus** codec via Concentus library
- 20ms frames
- 24 kHz sample rate
- ~24 kbps bitrate

### Performance

- VAD prevents transmission during silence
- Opus compression reduces bandwidth significantly
- 3D audio uses Unity's built-in spatial audio

### Platform Support

| Platform | Notes |
|----------|-------|
| Windows | Full support |
| macOS | Full support |
| Android | Runtime permission required |
| iOS | Full support |
| WebGL | Not supported (no microphone access) |
