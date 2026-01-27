using UnityEngine;

namespace EpicNet
{
    /// <summary>
    /// Automatically synchronizes a GameObject's transform (position, rotation, scale) across the network.
    /// Includes smooth interpolation for remote objects and optional extrapolation for prediction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attach this component to any networked object that needs transform synchronization.
    /// The object must also have an <see cref="EpicView"/> component.
    /// </para>
    /// <para>
    /// For the local owner, the transform is sent to other players.
    /// For remote objects, incoming data is smoothly interpolated to prevent jitter.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(EpicView))]
    [AddComponentMenu("EpicNet/Epic Transform View")]
    public class EpicTransformView : MonoBehaviour, IEpicObservable
    {
        #region Serialized Fields

        [Header("Sync Settings")]
        [SerializeField]
        [Tooltip("Synchronize the object's position.")]
        private bool syncPosition = true;

        [SerializeField]
        [Tooltip("Synchronize the object's rotation.")]
        private bool syncRotation = true;

        [SerializeField]
        [Tooltip("Synchronize the object's local scale (usually not needed).")]
        private bool syncScale = false;

        [Header("Interpolation")]
        [SerializeField]
        [Tooltip("How fast the transform interpolates to the target. Higher = snappier.")]
        [Range(1f, 50f)]
        private float lerpSpeed = 10f;

        [SerializeField]
        [Tooltip("If the object is farther than this distance, teleport instead of interpolating.")]
        private float teleportDistance = 10f;

        [Header("Extrapolation")]
        [SerializeField]
        [Tooltip("Enable prediction based on velocity to reduce perceived latency.")]
        private bool useExtrapolation = true;

        [SerializeField]
        [Tooltip("Maximum time to extrapolate before stopping prediction.")]
        [Range(0.1f, 1f)]
        private float maxExtrapolationTime = 0.25f;

        #endregion

        #region Private Fields

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

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _view = GetComponent<EpicView>();

            // Initialize network values to current transform to prevent lerping from garbage values
            _networkPosition = transform.position;
            _networkRotation = transform.rotation;
            _networkScale = transform.localScale;

            _startPosition = _networkPosition;
            _startRotation = _networkRotation;
            _startScale = _networkScale;

            _previousNetworkPosition = _networkPosition;
            _previousNetworkRotation = _networkRotation;
        }

        private void Update()
        {
            if (_view == null || _view.IsMine)
                return;

            InterpolateTransform();
        }

        #endregion

        #region IEpicObservable Implementation

        /// <summary>
        /// Serializes or deserializes transform data.
        /// Called automatically by the networking system.
        /// </summary>
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
                    var posData = stream.ReceiveNext();
                    if (posData is Vector3 pos)
                    {
                        _startPosition = transform.position;
                        _previousNetworkPosition = _networkPosition;
                        _networkPosition = pos;

                        // Calculate velocity for extrapolation
                        if (_initialized && deltaTime > 0.001f)
                        {
                            _velocity = (_networkPosition - _previousNetworkPosition) / deltaTime;
                        }
                    }
                }

                if (syncRotation)
                {
                    var rotData = stream.ReceiveNext();
                    if (rotData is Quaternion rot)
                    {
                        _startRotation = transform.rotation;
                        _previousNetworkRotation = _networkRotation;
                        _networkRotation = rot;

                        // HARD GUARD — never allow invalid quaternions, and normalize
                        if (!IsValidQuaternion(_networkRotation))
                        {
                            _networkRotation = transform.rotation;
                        }
                        else
                        {
                            _networkRotation = NormalizeQuaternion(_networkRotation);
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
                }

                if (syncScale)
                {
                    var scaleData = stream.ReceiveNext();
                    if (scaleData is Vector3 scale)
                    {
                        _startScale = transform.localScale;
                        _networkScale = scale;
                    }
                }

                _lastReceiveTime = Time.time;
                _lerpTime = 0f;
                _initialized = true;
            }
        }

        #endregion

        #region Private Methods

        private void InterpolateTransform()
        {
            _lerpTime += Time.deltaTime * lerpSpeed;
            float t = Mathf.Clamp01(_lerpTime);
            float timeSinceUpdate = Time.time - _lastReceiveTime;

            if (syncPosition)
            {
                InterpolatePosition(t, timeSinceUpdate);
            }

            if (syncRotation && IsValidQuaternion(_networkRotation))
            {
                InterpolateRotation(t, timeSinceUpdate);
            }

            if (syncScale)
            {
                transform.localScale = Vector3.Lerp(_startScale, _networkScale, t);
            }
        }

        private void InterpolatePosition(float t, float timeSinceUpdate)
        {
            Vector3 targetPos = _networkPosition;

            // Extrapolate if enabled and within time limit
            if (useExtrapolation && timeSinceUpdate < maxExtrapolationTime && _initialized)
            {
                targetPos += _velocity * timeSinceUpdate;
            }

            // Teleport if too far away to prevent rubber-banding
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

        private void InterpolateRotation(float t, float timeSinceUpdate)
        {
            Quaternion targetRot = _networkRotation;

            // Extrapolate rotation if enabled
            if (useExtrapolation && timeSinceUpdate < maxExtrapolationTime && _initialized && _angularVelocity.sqrMagnitude > 0.01f)
            {
                targetRot = Quaternion.Euler(_networkRotation.eulerAngles + _angularVelocity * timeSinceUpdate);
            }

            transform.rotation = Quaternion.Slerp(_startRotation, targetRot, t);
        }

        private static bool IsValidQuaternion(Quaternion q)
        {
            // Check for NaN values
            if (float.IsNaN(q.x) || float.IsNaN(q.y) ||
                float.IsNaN(q.z) || float.IsNaN(q.w))
                return false;

            // Block zero quaternion
            if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f)
                return false;

            // Check if roughly normalized (allow tolerance for floating point errors)
            float sqrMag = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return sqrMag > 0.9f && sqrMag < 1.1f;
        }

        private static Quaternion NormalizeQuaternion(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < 0.0001f) return Quaternion.identity;
            return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
        }

        #endregion
    }
}