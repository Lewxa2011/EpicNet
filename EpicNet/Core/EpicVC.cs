using Concentus.Enums;
using Concentus.Structs;
using System;
using UnityEngine;
using static EpicNet.EpicVCMgr;

namespace EpicNet
{
    /// <summary>
    /// Voice chat component that enables real-time voice communication.
    /// Uses Opus codec for efficient compression and supports 3D spatial audio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attach this component to networked player objects alongside <see cref="EpicView"/>.
    /// The owner's microphone audio is encoded and sent to other players.
    /// </para>
    /// <para>
    /// Before using voice chat, call <see cref="EpicVCMgr.Initialize"/> to set up
    /// microphone permissions and select the audio device.
    /// </para>
    /// <para>
    /// Includes Voice Activity Detection (VAD) to only transmit when the user is speaking.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(EpicView))]
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("EpicNet/Epic Voice Chat")]
    public class EpicVC : MonoBehaviour, IEpicObservable
    {
        #region Serialized Fields

        [SerializeField]
        [Tooltip("AudioSource used for playing received voice audio. Required for remote players.")]
        private AudioSource audioSource;

        #endregion

        #region Constants

        private const int Channels = 1;
        private const int FrameDurationMs = 20; // 20ms per frame - standard for VoIP
        private const SampleRate SR = EpicVCMgr.sr;
        private static readonly int FrameSize = ((int)SR / 1000) * FrameDurationMs;
        private const int MaxOpusPacketSize = 400; // Safe size for voice packets
        private const float VadThreshold = 0.07f; // Voice Activity Detection threshold
        private const int VadSamples = 256;

        #endregion

        #region Private Fields

        private EpicView _view;

        // Microphone Recording (local player only)
        private AudioClip _micClip;
        private int _lastSamplePos;
        private float[] _floatIn;

        // Remote Playback (remote players only)
        private int _playbackWritePos;

        // Opus codec
        private OpusEncoder _encoder;
        private OpusDecoder _decoder;

        // Audio buffers
        private short[] _pcmIn = new short[FrameSize];
        private short[] _pcmOut = new short[FrameSize];
        private float[] _floatOut = new float[FrameSize];
        private byte[] _opusPacket = new byte[MaxOpusPacketSize];
        private float[] _vadBuffer = new float[VadSamples];

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _view = GetComponent<EpicView>();
            _floatIn = new float[FrameSize];

            // Configure AudioSource for 3D spatial voice chat
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource != null)
            {
                audioSource.spatialBlend = 1f; // Full 3D audio
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource.minDistance = 1f;
                audioSource.maxDistance = 25f;
                audioSource.loop = true;
            }
        }

        private void Start()
        {
            if (_view.IsMine)
            {
                InitializeEncoder();
            }
            else
            {
                InitializeDecoder();
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void OnApplicationQuit()
        {
            Cleanup();
        }

        #endregion

        #region Initialization

        private void InitializeEncoder()
        {
            _encoder = new OpusEncoder(
                (int)SR,
                Channels,
                OpusApplication.OPUS_APPLICATION_VOIP
            )
            {
                Bitrate = 24000, // 24 kbps - good quality for voice
                SignalType = OpusSignal.OPUS_SIGNAL_VOICE
            };

            // Wait for permission before starting microphone
            if (EpicVCMgr.IsInitialized && EpicVCMgr.HasMicrophonePermission)
            {
                StartMicrophone();
            }
            else
            {
                EpicVCMgr.OnInitialized += StartMicrophone;
            }
        }

        private void InitializeDecoder()
        {
            _decoder = new OpusDecoder((int)SR, Channels);

            // Create a circular buffer clip for incoming audio
            audioSource.clip = AudioClip.Create(
                "RemoteVoice",
                (int)SR * 2, // 2 seconds buffer
                1,
                (int)SR,
                false
            );

            audioSource.Play();
        }

        private void StartMicrophone()
        {
            EpicVCMgr.OnInitialized -= StartMicrophone;

            if (!EpicVCMgr.HasMicrophonePermission)
            {
                Debug.LogWarning("[EpicNet VC] Cannot start microphone - permission not granted");
                return;
            }

            _micClip = Microphone.Start(
                EpicVCMgr.CurrentDevice,
                true,
                1,
                (int)SR
            );

            Debug.Log("[EpicNet VC] Microphone started");
        }

        private void Cleanup()
        {
            EpicVCMgr.OnInitialized -= StartMicrophone;

            if (_view != null && _view.IsMine)
            {
                if (Microphone.IsRecording(EpicVCMgr.CurrentDevice))
                {
                    Microphone.End(EpicVCMgr.CurrentDevice);
                }

                _encoder?.Dispose();
                _encoder = null;
            }
            else
            {
                if (audioSource != null)
                {
                    audioSource.Stop();
                    audioSource.clip = null;
                }

                _decoder?.Dispose();
                _decoder = null;
            }

            _micClip = null;
            _floatIn = null;
            _pcmIn = null;
            _pcmOut = null;
            _floatOut = null;
            _opusPacket = null;
        }

        #endregion

        #region IEpicObservable Implementation

        /// <summary>
        /// Serializes voice data for transmission or deserializes received audio.
        /// Called automatically by the networking system.
        /// </summary>
        public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
        {
            if (stream.IsWriting)
            {
                SendVoiceData(stream);
            }
            else
            {
                ReceiveVoiceData(stream);
            }
        }

        #endregion

        #region Private Methods

        private void SendVoiceData(EpicStream stream)
        {
            if (!IsMicActive()) return;

            int micPos = Microphone.GetPosition(EpicVCMgr.CurrentDevice);
            int diff = (micPos - _lastSamplePos + _micClip.samples) % _micClip.samples;

            while (diff >= FrameSize)
            {
                _micClip.GetData(_floatIn, _lastSamplePos);

                // Convert float samples to PCM16
                for (int i = 0; i < FrameSize; i++)
                {
                    _pcmIn[i] = FloatToPcm16(_floatIn[i]);
                }

                int encodedBytes = _encoder.Encode(
                    _pcmIn.AsSpan(0, FrameSize),
                    FrameSize,
                    _opusPacket.AsSpan(),
                    _opusPacket.Length
                );

                if (encodedBytes > 0)
                {
                    byte[] send = new byte[encodedBytes];
                    Buffer.BlockCopy(_opusPacket, 0, send, 0, encodedBytes);
                    stream.SendNext(send);
                }

                _lastSamplePos = (_lastSamplePos + FrameSize) % _micClip.samples;
                diff -= FrameSize;
            }
        }

        private void ReceiveVoiceData(EpicStream stream)
        {
            object data = stream.ReceiveNext();
            if (!(data is byte[] opusData) || _decoder == null) return;

            int decodedSamples = _decoder.Decode(
                opusData.AsSpan(),
                _pcmOut.AsSpan(),
                FrameSize,
                false
            );

            if (decodedSamples <= 0) return;

            // Convert PCM16 to float
            for (int i = 0; i < decodedSamples; i++)
            {
                _floatOut[i] = _pcmOut[i] / 32768f;
            }

            WriteToAudioClip(decodedSamples);
        }

        private void WriteToAudioClip(int sampleCount)
        {
            if (audioSource == null || audioSource.clip == null)
            {
                Debug.LogWarning("[EpicNet VC] Audio clip not initialized");
                return;
            }

            int clipSamples = audioSource.clip.samples;
            if (clipSamples <= 0) return;

            int samplesLeft = clipSamples - _playbackWritePos;

            if (sampleCount <= samplesLeft)
            {
                // Single contiguous write
                float[] writeBuffer = new float[sampleCount];
                Array.Copy(_floatOut, 0, writeBuffer, 0, sampleCount);
                audioSource.clip.SetData(writeBuffer, _playbackWritePos);
                _playbackWritePos += sampleCount;
            }
            else
            {
                // Wrap-around write
                float[] firstPart = new float[samplesLeft];
                Array.Copy(_floatOut, 0, firstPart, 0, samplesLeft);
                audioSource.clip.SetData(firstPart, _playbackWritePos);

                int remaining = sampleCount - samplesLeft;
                float[] secondPart = new float[remaining];
                Array.Copy(_floatOut, samplesLeft, secondPart, 0, remaining);
                audioSource.clip.SetData(secondPart, 0);

                _playbackWritePos = remaining;
            }
        }

        private bool IsMicActive()
        {
            if (_micClip == null) return false;

            int micPos = Microphone.GetPosition(EpicVCMgr.CurrentDevice);
            if (micPos < VadSamples) return false;

            _micClip.GetData(_vadBuffer, micPos - VadSamples);

            // Calculate RMS for voice activity detection
            float sum = 0f;
            for (int i = 0; i < VadSamples; i++)
            {
                sum += _vadBuffer[i] * _vadBuffer[i];
            }

            float rms = Mathf.Sqrt(sum / VadSamples);
            return rms > VadThreshold;
        }

        private static short FloatToPcm16(float f)
        {
            return (short)(Mathf.Clamp(f, -1f, 1f) * short.MaxValue);
        }

        #endregion
    }
}