using Concentus.Enums;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

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

        // Remote Playback
        private int _playbackWritePos;

        const int CHANNELS = 1;
        const int FRAME_SIZE = EpicVCMgr.SampleRate / 50; // 20ms
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

            // Setup Spatial Audio
            audioSource.spatialBlend = 1.0f; // Force 3D
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
                    EpicVCMgr.SampleRate,
                    CHANNELS,
                    OpusApplication.OPUS_APPLICATION_VOIP
                );

                _encoder.Bitrate = 24000;
                _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;

                _micClip = Microphone.Start(
                    EpicVCMgr.CurrentDevice,
                    true,
                    1,
                    EpicVCMgr.SampleRate
                );
            }
            else
            {
                _decoder = new OpusDecoder(
                    EpicVCMgr.SampleRate,
                    CHANNELS
                );

                audioSource.clip = AudioClip.Create(
                    "RemoteVoice",
                    EpicVCMgr.SampleRate * 2,
                    1,
                    EpicVCMgr.SampleRate,
                    false
                );

                audioSource.Play();
            }
        }

        /// <summary>
        /// Syncs audio samples using the EpicNet Stream system
        /// </summary>
        public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
        {
            if (stream.IsWriting)
            {
                if (!EpicVCMgr.IsTransmitting()) return;

                int micPos = Microphone.GetPosition(null);
                int diff = (micPos - _lastSamplePos + _micClip.samples) % _micClip.samples;

                while (diff >= FRAME_SIZE)
                {
                    float[] temp = new float[FRAME_SIZE];
                    _micClip.GetData(temp, _lastSamplePos);

                    for (int i = 0; i < FRAME_SIZE; i++)
                        _pcmIn[i] = FloatToPcm16(temp[i]);

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
                    _playbackWritePos =
                        (_playbackWritePos + decodedSamples) % audioSource.clip.samples;
                }
            }
        }

        private static float Pcm16ToFloat(short s)
        {
            return s / 32768f;
        }

        private static short FloatToPcm16(float f)
        {
            f = Mathf.Clamp(f, -1f, 1f);
            return (short)(f * short.MaxValue);
        }
    }
}