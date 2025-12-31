using UnityEngine;
using Epic.OnlineServices;
using System;

namespace EpicNet
{
    /// <summary>
    /// Transform synchronization component - equivalent to PhotonTransformView
    /// </summary>
    public class EpicTransformView : MonoBehaviour, IEpicObservable
    {
        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;
        [SerializeField] private bool syncScale = false;

        [SerializeField] private float lerpSpeed = 10f;

        private Vector3 _networkPosition;
        private Quaternion _networkRotation;
        private Vector3 _networkScale;

        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private Vector3 _startScale;

        private float _lerpTime;

        private EpicView _view;

        private void Awake()
        {
            _view = GetComponent<EpicView>();

            // CRITICAL: initialize network values so we never lerp from garbage
            _networkPosition = transform.localPosition;
            _networkRotation = transform.localRotation;
            _networkScale = transform.localScale;

            _startPosition = _networkPosition;
            _startRotation = _networkRotation;
            _startScale = _networkScale;
        }

        public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
        {
            if (stream.IsWriting)
            {
                if (syncPosition) stream.SendNext(transform.localPosition);
                if (syncRotation) stream.SendNext(transform.localRotation);
                if (syncScale) stream.SendNext(transform.localScale);
            }
            else
            {
                if (syncPosition)
                {
                    _startPosition = transform.localPosition;
                    _networkPosition = (Vector3)stream.ReceiveNext();
                }

                if (syncRotation)
                {
                    _startRotation = transform.localRotation;
                    _networkRotation = (Quaternion)stream.ReceiveNext();

                    // HARD GUARD — never allow invalid quaternions
                    if (!IsValidQuaternion(_networkRotation))
                    {
                        _networkRotation = transform.localRotation;
                    }
                }

                if (syncScale)
                {
                    _startScale = transform.localScale;
                    _networkScale = (Vector3)stream.ReceiveNext();
                }

                _lerpTime = 0f;
            }
        }

        private void Update()
        {
            if (_view == null || _view.IsMine)
                return;

            _lerpTime += Time.deltaTime * lerpSpeed;
            float t = Mathf.Clamp01(_lerpTime);

            if (syncPosition)
            {
                transform.localPosition = Vector3.Lerp(
                    _startPosition,
                    _networkPosition,
                    t
                );
            }

            if (syncRotation && IsValidQuaternion(_networkRotation))
            {
                transform.localRotation = Quaternion.Slerp(
                    _startRotation,
                    _networkRotation,
                    t
                );
            }

            if (syncScale)
            {
                transform.localScale = Vector3.Lerp(
                    _startScale,
                    _networkScale,
                    t
                );
            }
        }

        private bool IsValidQuaternion(Quaternion q)
        {
            if (float.IsNaN(q.x) || float.IsNaN(q.y) ||
                float.IsNaN(q.z) || float.IsNaN(q.w))
                return false;

            // blocks {0,0,0,0}
            return !(q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f);
        }
    }
}