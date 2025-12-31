using UnityEngine;
using System.Collections.Generic;

namespace EpicNet
{
    /// <summary>
    /// Stream for serializing data in OnEpicSerializeView
    /// </summary>
    public class EpicStream
    {
        public bool IsWriting { get; private set; }
        private Queue<object> _data = new Queue<object>();
        private List<object> _dataList = new List<object>();

        public EpicStream(bool isWriting)
        {
            IsWriting = isWriting;
        }

        /// <summary>
        /// Send the next value (write mode)
        /// </summary>
        public void SendNext(object obj)
        {
            if (IsWriting)
            {
                _dataList.Add(obj);
            }
        }

        /// <summary>
        /// Receive the next value (read mode)
        /// </summary>
        public object ReceiveNext()
        {
            if (!IsWriting && _data.Count > 0)
            {
                return _data.Dequeue();
            }
            return null;
        }

        /// <summary>
        /// Check if there's data to send
        /// </summary>
        public bool HasData()
        {
            return _dataList.Count > 0;
        }

        /// <summary>
        /// Get the data list for serialization
        /// </summary>
        internal List<object> GetDataList()
        {
            return _dataList;
        }

        /// <summary>
        /// Add data to the read queue
        /// </summary>
        internal void EnqueueData(object data)
        {
            _data.Enqueue(data);
        }

        /// <summary>
        /// Clear all data
        /// </summary>
        public void Clear()
        {
            _data.Clear();
            _dataList.Clear();
        }
    }
}