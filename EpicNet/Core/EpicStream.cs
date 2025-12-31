using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace EpicNet
{
    /// <summary>
    /// Stream for serializing data
    /// </summary>
    public class EpicStream
    {
        public bool IsWriting { get; private set; }
        private Queue<object> _data = new Queue<object>();

        public EpicStream(bool isWriting)
        {
            IsWriting = isWriting;
        }

        public void SendNext(object obj)
        {
            if (IsWriting) _data.Enqueue(obj);
        }

        public object ReceiveNext()
        {
            return !IsWriting && _data.Count > 0 ? _data.Dequeue() : null;
        }
    }
}
