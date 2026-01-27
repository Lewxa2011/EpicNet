using System.Collections.Generic;

namespace EpicNet
{
    /// <summary>
    /// Represents the current room/lobby the player is in.
    /// Similar to Photon.Realtime.Room.
    /// </summary>
    /// <remarks>
    /// Access the current room via <see cref="EpicNetwork.CurrentRoom"/>.
    /// This class provides information about the room state and its players.
    /// </remarks>
    public class EpicRoom
    {
        /// <summary>
        /// The unique name/identifier of the room.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Current number of players in the room.
        /// </summary>
        public int PlayerCount => Players?.Count ?? 0;

        /// <summary>
        /// Maximum number of players allowed in this room.
        /// </summary>
        public int MaxPlayers { get; set; }

        /// <summary>
        /// Whether the room is currently accepting new players.
        /// Only the master client can modify this.
        /// </summary>
        public bool IsOpen { get; set; }

        /// <summary>
        /// Whether the room is visible in public room listings.
        /// Only the master client can modify this.
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// Custom properties stored on the room.
        /// Use <see cref="EpicNetwork.SetRoomProperties"/> to modify (master client only).
        /// </summary>
        public Dictionary<string, object> CustomProperties { get; private set; }

        /// <summary>
        /// List of all players currently in the room, including the local player.
        /// </summary>
        public List<EpicPlayer> Players { get; private set; }

        /// <summary>
        /// Creates a new room instance with the specified name.
        /// </summary>
        /// <param name="name">The room name/identifier.</param>
        internal EpicRoom(string name)
        {
            Name = name;
            CustomProperties = new Dictionary<string, object>();
            Players = new List<EpicPlayer>();
            IsOpen = true;
            IsVisible = true;
            MaxPlayers = 10;
        }

        /// <summary>
        /// Merges the provided properties into the room's custom properties.
        /// For internal use - use <see cref="EpicNetwork.SetRoomProperties"/> instead.
        /// </summary>
        internal void SetCustomProperties(Dictionary<string, object> properties)
        {
            if (properties == null) return;

            foreach (var kvp in properties)
            {
                CustomProperties[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Returns a string representation of the room.
        /// </summary>
        public override string ToString()
        {
            return $"Room '{Name}' ({PlayerCount}/{MaxPlayers})";
        }
    }
}