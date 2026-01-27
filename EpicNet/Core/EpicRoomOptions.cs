using System.Collections.Generic;

namespace EpicNet
{
    /// <summary>
    /// Configuration options for creating a new room.
    /// Similar to RoomOptions in Photon PUN2.
    /// </summary>
    /// <example>
    /// <code>
    /// var options = new EpicRoomOptions
    /// {
    ///     MaxPlayers = 4,
    ///     IsVisible = true,
    ///     Password = "secret123",
    ///     CustomRoomProperties = new Dictionary&lt;string, object&gt;
    ///     {
    ///         { "GameMode", "Deathmatch" },
    ///         { "Map", "Arena1" }
    ///     },
    ///     CustomRoomPropertiesForLobby = new[] { "GameMode", "Map" }
    /// };
    /// EpicNetwork.CreateRoom("MyRoom", options);
    /// </code>
    /// </example>
    public class EpicRoomOptions
    {
        /// <summary>
        /// Maximum number of players allowed in the room. Default: 20.
        /// EOS supports up to 64 members per lobby.
        /// </summary>
        public int MaxPlayers { get; set; } = 20;

        /// <summary>
        /// Whether the room appears in public room listings. Default: true.
        /// Set to false for private/invite-only rooms.
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// Whether the room accepts new players. Default: true.
        /// Set to false to prevent new joins (e.g., when a match is in progress).
        /// </summary>
        public bool IsOpen { get; set; } = true;

        /// <summary>
        /// Optional password required to join the room. Default: null (no password).
        /// Passwords are hashed before being stored in the lobby.
        /// </summary>
        public string Password { get; set; } = null;

        /// <summary>
        /// Custom properties stored on the room.
        /// Supported types: bool, int, long, float, double, string.
        /// </summary>
        public Dictionary<string, object> CustomRoomProperties { get; set; }

        /// <summary>
        /// Keys from CustomRoomProperties that should be visible in the lobby listing.
        /// Only these properties will appear in <see cref="EpicRoomInfo.CustomProperties"/>.
        /// </summary>
        public string[] CustomRoomPropertiesForLobby { get; set; }

        /// <summary>
        /// Whether a password is set for this room.
        /// </summary>
        public bool HasPassword => !string.IsNullOrEmpty(Password);

        /// <summary>
        /// Creates a new EpicRoomOptions with default values.
        /// </summary>
        public EpicRoomOptions()
        {
            CustomRoomProperties = new Dictionary<string, object>();
        }
    }
}