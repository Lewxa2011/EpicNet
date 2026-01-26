namespace EpicNet
{
    /// <summary>
    /// Interface for components that synchronize data across the network.
    /// Implement this interface on MonoBehaviours attached to objects with an EpicView
    /// to automatically synchronize state at the configured send rate.
    /// </summary>
    /// <example>
    /// <code>
    /// public class HealthSync : MonoBehaviour, IEpicObservable
    /// {
    ///     private float health = 100f;
    ///
    ///     public void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info)
    ///     {
    ///         if (stream.IsWriting)
    ///         {
    ///             stream.SendNext(health);
    ///         }
    ///         else
    ///         {
    ///             health = (float)stream.ReceiveNext();
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IEpicObservable
    {
        /// <summary>
        /// Called to serialize (write) or deserialize (read) synchronized data.
        /// </summary>
        /// <param name="stream">
        /// The stream to read from or write to. Check <see cref="EpicStream.IsWriting"/>
        /// to determine the direction.
        /// </param>
        /// <param name="info">Information about the message sender and timestamp.</param>
        void OnEpicSerializeView(EpicStream stream, EpicMessageInfo info);
    }
}