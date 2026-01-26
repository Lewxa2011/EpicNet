using Epic.OnlineServices;
using System.Collections.Generic;

namespace EpicNet
{
    /// <summary>
    /// Represents a player connected to the network.
    /// Similar to Photon.Realtime.Player.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Access the local player via <see cref="EpicNetwork.LocalPlayer"/>.
    /// Access all players in the room via <see cref="EpicNetwork.PlayerList"/> or <see cref="EpicRoom.Players"/>.
    /// </para>
    /// <para>
    /// Players are compared by their <see cref="UserId"/>, so two EpicPlayer instances
    /// referring to the same EOS user are considered equal.
    /// </para>
    /// </remarks>
    public class EpicPlayer
    {
        /// <summary>
        /// The unique EOS Product User ID for this player.
        /// </summary>
        public ProductUserId UserId { get; private set; }

        /// <summary>
        /// The display name of this player.
        /// Can be changed via <see cref="EpicNetwork.SetNickName"/>.
        /// </summary>
        public string NickName { get; set; }

        /// <summary>
        /// A unique number assigned to this player when they join a room.
        /// Used for network ordering and ViewID generation.
        /// </summary>
        public int ActorNumber { get; private set; }

        /// <summary>
        /// Whether this player is the current master client (room host).
        /// The master client has authority over room state and can kick players.
        /// </summary>
        public bool IsMasterClient { get; internal set; }

        /// <summary>
        /// Whether this player is the local player on this device.
        /// </summary>
        public bool IsLocal => UserId != null && EpicNetwork.LocalPlayer?.UserId != null &&
                               UserId.Equals(EpicNetwork.LocalPlayer.UserId);

        /// <summary>
        /// Custom properties synchronized for this player.
        /// Use <see cref="EpicNetwork.SetLocalPlayerProperties"/> to modify your own properties.
        /// </summary>
        public Dictionary<string, object> CustomProperties { get; private set; }

        private static int _actorCounter = 1;
        private static readonly object _actorCounterLock = new object();

        /// <summary>
        /// Creates a new player instance.
        /// </summary>
        /// <param name="userId">The EOS Product User ID.</param>
        /// <param name="nickName">The display name.</param>
        internal EpicPlayer(ProductUserId userId, string nickName)
        {
            UserId = userId;
            NickName = nickName ?? "Unknown";
            CustomProperties = new Dictionary<string, object>();
            ActorNumber = GenerateActorNumber();
        }

        private static int GenerateActorNumber()
        {
            lock (_actorCounterLock)
            {
                return _actorCounter++;
            }
        }

        /// <summary>
        /// Reset the actor counter. Called when leaving a room or logging out.
        /// </summary>
        internal static void ResetActorCounter()
        {
            lock (_actorCounterLock)
            {
                _actorCounter = 1;
            }
        }

        /// <summary>
        /// Merges properties into this player's custom properties.
        /// For internal use - use <see cref="EpicNetwork.SetLocalPlayerProperties"/> instead.
        /// </summary>
        internal void SetCustomProperties(Dictionary<string, object> properties)
        {
            if (properties == null) return;

            foreach (var kvp in properties)
            {
                CustomProperties[kvp.Key] = kvp.Value;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"Player {NickName} (#{ActorNumber})";

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is EpicPlayer other)
            {
                if (UserId == null && other.UserId == null) return true;
                if (UserId == null || other.UserId == null) return false;
                return UserId.Equals(other.UserId);
            }
            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return UserId?.GetHashCode() ?? 0;
        }

        /// <summary>
        /// Compares two players by their UserId.
        /// </summary>
        public static bool operator ==(EpicPlayer a, EpicPlayer b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            if (a.UserId == null && b.UserId == null) return true;
            if (a.UserId == null || b.UserId == null) return false;
            return a.UserId.Equals(b.UserId);
        }

        /// <summary>
        /// Compares two players by their UserId.
        /// </summary>
        public static bool operator !=(EpicPlayer a, EpicPlayer b) => !(a == b);
    }
}