using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Transform synchronization component with interpolation and extrapolation
    /// </summary>
    public class EpicTransformView : MonoBehaviour, IEpicObservable
    {
        [Header("Sync Settings")]
        [SerializeField] private bool syncPosition = true;
        [SerializeField] private bool syncRotation = true;
        [SerializeField] private bool syncScale = false;

        [Header("Interpolation")]
        [SerializeField] private float lerpSpeed = 10f;
        [SerializeField] private float teleportDistance = 10f;

        [Header("Extrapolation")]
        [SerializeField] private bool useExtrapolation = true;
        [SerializeField] private float maxExtrapolationTime = 0.25f;

        private Vector3 _networkPosition;
        private Quaternion _networkRotation;
        private Vector3 _networkScale;

        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private Vector3 _startScale;

        // Velocity for extrapolation
        private Vector3 _velocity;
        private Vector3 _angularVelocity;
        private Vector3 _previousNetworkPosition;
        private Quaternion _previousNetworkRotation;
        private float _lastReceiveTime;

        private float _lerpTime;
        private bool _initialized;

        private EpicView _view;

        private void Awake()
        {
            _view = GetComponent<EpicView>();

            // CRITICAL: initialize network values so we never lerp from garbage
            _networkPosition = transform.position;
            _networkRotation = transform.rotation;
            _networkScale = transform.localScale;

            _startPosition = _networkPosition;
            _startRotation = _networkRotation;
            _startScale = _networkScale;

            _previousNetworkPosition = _networkPosition;
            _previousNetworkRotation = _networkRotation;
        }

        public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
        {
            if (stream.IsWriting)
            {
                if (syncPosition) stream.SendNext(transform.position);
                if (syncRotation) stream.SendNext(transform.rotation);
                if (syncScale) stream.SendNext(transform.localScale);
            }
            else
            {
                float deltaTime = Time.time - _lastReceiveTime;

                if (syncPosition)
                {
                    _startPosition = transform.position;
                    _previousNetworkPosition = _networkPosition;
                    _networkPosition = (Vector3)stream.ReceiveNext();

                    // Calculate velocity for extrapolation
                    if (_initialized && deltaTime > 0.001f)
                    {
                        _velocity = (_networkPosition - _previousNetworkPosition) / deltaTime;
                    }
                }

                if (syncRotation)
                {
                    _startRotation = transform.rotation;
                    _previousNetworkRotation = _networkRotation;
                    _networkRotation = (Quaternion)stream.ReceiveNext();

                    // HARD GUARD — never allow invalid quaternions
                    if (!IsValidQuaternion(_networkRotation))
                    {
                        _networkRotation = transform.rotation;
                    }

                    // Calculate angular velocity for extrapolation
                    if (_initialized && deltaTime > 0.001f && IsValidQuaternion(_previousNetworkRotation))
                    {
                        Vector3 deltaEuler = _networkRotation.eulerAngles - _previousNetworkRotation.eulerAngles;
                        // Handle wrap-around
                        if (deltaEuler.x > 180) deltaEuler.x -= 360;
                        if (deltaEuler.y > 180) deltaEuler.y -= 360;
                        if (deltaEuler.z > 180) deltaEuler.z -= 360;
                        if (deltaEuler.x < -180) deltaEuler.x += 360;
                        if (deltaEuler.y < -180) deltaEuler.y += 360;
                        if (deltaEuler.z < -180) deltaEuler.z += 360;
                        _angularVelocity = deltaEuler / deltaTime;
                    }
                }

                if (syncScale)
                {
                    _startScale = transform.localScale;
                    _networkScale = (Vector3)stream.ReceiveNext();
                }

                _lastReceiveTime = Time.time;
                _lerpTime = 0f;
                _initialized = true;
            }
        }

        private void Update()
        {
            if (_view == null || _view.IsMine)
                return;

            _lerpTime += Time.deltaTime * lerpSpeed;
            float t = Mathf.Clamp01(_lerpTime);
            float timeSinceUpdate = Time.time - _lastReceiveTime;

            if (syncPosition)
            {
                Vector3 targetPos = _networkPosition;

                // Extrapolate if enabled and within time limit
                if (useExtrapolation && timeSinceUpdate < maxExtrapolationTime && _initialized)
                {
                    targetPos += _velocity * timeSinceUpdate;
                }

                // Teleport if too far away
                float distance = Vector3.Distance(transform.position, targetPos);
                if (distance > teleportDistance)
                {
                    transform.position = targetPos;
                    _startPosition = targetPos;
                }
                else
                {
                    transform.position = Vector3.Lerp(_startPosition, targetPos, t);
                }
            }

            if (syncRotation && IsValidQuaternion(_networkRotation))
            {
                Quaternion targetRot = _networkRotation;

                // Extrapolate rotation if enabled
                if (useExtrapolation && timeSinceUpdate < maxExtrapolationTime && _initialized && _angularVelocity.sqrMagnitude > 0.01f)
                {
                    targetRot = Quaternion.Euler(_networkRotation.eulerAngles + _angularVelocity * timeSinceUpdate);
                }

                transform.rotation = Quaternion.Slerp(_startRotation, targetRot, t);
            }

            if (syncScale)
            {
                transform.localScale = Vector3.Lerp(_startScale, _networkScale, t);
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