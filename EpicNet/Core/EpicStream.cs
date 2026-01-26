using System.Collections.Generic;

namespace EpicNet
{
    /// <summary>
    /// A bidirectional stream for serializing and deserializing data in
    /// <see cref="IEpicObservable.OnEpicSerializeView"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="IsWriting"/> is true, you are the owner and should call
    /// <see cref="SendNext"/> to write values.
    /// </para>
    /// <para>
    /// When <see cref="IsWriting"/> is false, you are receiving data and should call
    /// <see cref="ReceiveNext"/> to read values in the same order they were written.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
    /// {
    ///     if (stream.IsWriting)
    ///     {
    ///         stream.SendNext(transform.position);
    ///         stream.SendNext(health);
    ///     }
    ///     else
    ///     {
    ///         transform.position = (Vector3)stream.ReceiveNext();
    ///         health = (float)stream.ReceiveNext();
    ///     }
    /// }
    /// </code>
    /// </example>
    public class EpicStream
    {
        /// <summary>
        /// True when writing (local player owns the object), false when reading (receiving data).
        /// </summary>
        public bool IsWriting { get; private set; }

        /// <summary>
        /// True when reading data from the network.
        /// </summary>
        public bool IsReading => !IsWriting;

        /// <summary>
        /// The number of items available to read (read mode only).
        /// </summary>
        public int Count => _data.Count;

        private readonly Queue<object> _data = new Queue<object>();
        private readonly List<object> _dataList = new List<object>();

        /// <summary>
        /// Creates a new stream instance.
        /// </summary>
        /// <param name="isWriting">True if this is a write stream, false for read.</param>
        internal EpicStream(bool isWriting)
        {
            IsWriting = isWriting;
        }

        /// <summary>
        /// Writes a value to the stream (write mode only).
        /// Supported types: int, float, string, bool, Vector3, Quaternion, byte[].
        /// </summary>
        /// <param name="obj">The value to send.</param>
        public void SendNext(object obj)
        {
            if (IsWriting)
            {
                _dataList.Add(obj);
            }
        }

        /// <summary>
        /// Reads the next value from the stream (read mode only).
        /// Values must be read in the same order they were written.
        /// </summary>
        /// <returns>The next value, or null if no more data is available.</returns>
        public object ReceiveNext()
        {
            if (!IsWriting && _data.Count > 0)
            {
                return _data.Dequeue();
            }
            return null;
        }

        /// <summary>
        /// Attempts to read the next value with type safety.
        /// </summary>
        /// <typeparam name="T">The expected type.</typeparam>
        /// <param name="value">The received value.</param>
        /// <returns>True if a value was read successfully, false otherwise.</returns>
        public bool TryReceiveNext<T>(out T value)
        {
            value = default;
            if (IsWriting || _data.Count == 0) return false;

            object obj = _data.Dequeue();
            if (obj is T typedValue)
            {
                value = typedValue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Peeks at the next value without removing it from the queue.
        /// </summary>
        /// <returns>The next value, or null if empty.</returns>
        public object PeekNext()
        {
            if (!IsWriting && _data.Count > 0)
            {
                return _data.Peek();
            }
            return null;
        }

        /// <summary>
        /// Returns true if there is data to send (write mode).
        /// </summary>
        public bool HasData()
        {
            return _dataList.Count > 0;
        }

        /// <summary>
        /// Gets the data list for serialization. Internal use only.
        /// </summary>
        internal List<object> GetDataList() => _dataList;

        /// <summary>
        /// Adds data to the read queue. Internal use only.
        /// </summary>
        internal void EnqueueData(object data) => _data.Enqueue(data);

        /// <summary>
        /// Clears all data in the stream.
        /// </summary>
        public void Clear()
        {
            _data.Clear();
            _dataList.Clear();
        }
    }
}