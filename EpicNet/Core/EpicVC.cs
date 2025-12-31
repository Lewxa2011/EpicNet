using Concentus.Enums;
using Concentus.Structs;
using System;
using UnityEngine;
using static EpicNet.EpicVCMgr;

namespace EpicNet
{
    [RequireComponent(typeof(EpicView))]
    public class EpicVC : MonoBehaviour, IEpicObservable
    {
        [SerializeField] private AudioSource audioSource;

        private EpicView _view;

        // Microphone Recording
        private AudioClip _micClip;
        private int _lastSamplePos;
        private float[] _floatIn;

        // Remote Playback
        private int _playbackWritePos;

        const int CHANNELS = 1;
        const int FRAME_DURATION_MS = 20; // 20ms per frame
        const SampleRate SR = SampleRate.FortyEightKHz;
        const int FRAME_SIZE = ((int)SR / 1000) * FRAME_DURATION_MS; // 960 samples
        const int MAX_OPUS_PACKET_SIZE = 400; // Safe for voice

        private OpusEncoder _encoder;
        private OpusDecoder _decoder;

        // Buffers
        private short[] _pcmIn = new short[FRAME_SIZE];
        private short[] _pcmOut = new short[FRAME_SIZE];
        private float[] _floatOut = new float[FRAME_SIZE];
        private byte[] _opusPacket = new byte[MAX_OPUS_PACKET_SIZE];

        private void Awake()
        {
            _view = GetComponent<EpicView>();
            _floatIn = new float[FRAME_SIZE];

            audioSource.spatialBlend = 1f; // 3D audio
            audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            audioSource.minDistance = 1f;
            audioSource.maxDistance = 25f;
            audioSource.loop = true;
        }

        private void Start()
        {
            if (_view.IsMine)
            {
                _encoder = new OpusEncoder(
                    (int)SR,
                    CHANNELS,
                    OpusApplication.OPUS_APPLICATION_VOIP
                )
                {
                    Bitrate = 24000,
                    SignalType = OpusSignal.OPUS_SIGNAL_VOICE
                };

                _micClip = Microphone.Start(
                    EpicVCMgr.CurrentDevice,
                    true,
                    1,
                    (int)SR
                );
            }
            else
            {
                _decoder = new OpusDecoder((int)SR, CHANNELS);

                audioSource.clip = AudioClip.Create(
                    "RemoteVoice",
                    (int)SR * 2,
                    1,
                    (int)SR,
                    false
                );

                audioSource.Play();
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

        private void Cleanup()
        {
            if (_view.IsMine)
            {
                if (Microphone.IsRecording(EpicVCMgr.CurrentDevice))
                    Microphone.End(EpicVCMgr.CurrentDevice);

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

        public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
        {
            if (stream.IsWriting)
            {
                if (!EpicVCMgr.IsTransmitting()) return;

                int micPos = Microphone.GetPosition(null);
                int diff = (micPos - _lastSamplePos + _micClip.samples) % _micClip.samples;

                while (diff >= FRAME_SIZE)
                {
                    _micClip.GetData(_floatIn, _lastSamplePos);
                    for (int i = 0; i < FRAME_SIZE; i++)
                        _pcmIn[i] = FloatToPcm16(_floatIn[i]);

                    int encodedBytes = _encoder.Encode(
                        _pcmIn.AsSpan(0, FRAME_SIZE),
                        FRAME_SIZE,
                        _opusPacket.AsSpan(),
                        _opusPacket.Length
                    );

                    if (encodedBytes > 0)
                    {
                        byte[] send = new byte[encodedBytes];
                        Buffer.BlockCopy(_opusPacket, 0, send, 0, encodedBytes);
                        stream.SendNext(send);
                    }

                    _lastSamplePos = (_lastSamplePos + FRAME_SIZE) % _micClip.samples;
                    diff -= FRAME_SIZE;
                }
            }
            else
            {
                object data = stream.ReceiveNext();
                if (data is byte[] opusData)
                {
                    int decodedSamples = _decoder.Decode(
                        opusData.AsSpan(),
                        _pcmOut.AsSpan(),
                        FRAME_SIZE,
                        false
                    );

                    for (int i = 0; i < decodedSamples; i++)
                        _floatOut[i] = _pcmOut[i] / 32768f;

                    audioSource.clip.SetData(_floatOut, _playbackWritePos);
                    _playbackWritePos = (_playbackWritePos + decodedSamples) % audioSource.clip.samples;
                }
            }
        }

        private static short FloatToPcm16(float f) => (short)(Mathf.Clamp(f, -1f, 1f) * short.MaxValue);
    }
}