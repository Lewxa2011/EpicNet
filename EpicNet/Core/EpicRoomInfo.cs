using System.Collections.Generic;

namespace EpicNet
{
    /// <summary>
    /// Read-only information about a room retrieved from the lobby list.
    /// Used when browsing available rooms before joining.
    /// </summary>
    /// <seealso cref="EpicNetwork.GetRoomList"/>
    /// <seealso cref="EpicNetwork.OnRoomListUpdate"/>
    public class EpicRoomInfo
    {
        /// <summary>
        /// The display name of the room.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Current number of players in the room.
        /// </summary>
        public int PlayerCount { get; set; }

        /// <summary>
        /// Maximum number of players allowed in the room.
        /// </summary>
        public int MaxPlayers { get; set; }

        /// <summary>
        /// Whether the room is accepting new players.
        /// </summary>
        public bool IsOpen { get; set; }

        /// <summary>
        /// Whether the room appears in public room listings.
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// Whether the room requires a password to join.
        /// </summary>
        public bool HasPassword { get; set; }

        /// <summary>
        /// Custom properties set on the room that are visible in the lobby.
        /// Only properties specified in <see cref="EpicRoomOptions.CustomRoomPropertiesForLobby"/> are included.
        /// </summary>
        public Dictionary<string, object> CustomProperties { get; set; }

        /// <summary>
        /// Creates a new EpicRoomInfo with default values.
        /// </summary>
        public EpicRoomInfo()
        {
            CustomProperties = new Dictionary<string, object>();
            IsOpen = true;
            IsVisible = true;
        }

        /// <summary>
        /// Returns a string representation of the room.
        /// </summary>
        public override string ToString()
        {
            string passwordIndicator = HasPassword ? " [Password]" : "";
            return $"{Name} ({PlayerCount}/{MaxPlayers}){passwordIndicator}";
        }
    }
}