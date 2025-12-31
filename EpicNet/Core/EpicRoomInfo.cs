using System.Collections.Generic;

namespace EpicNet
{
    /// <summary>
    /// Information about a room in the lobby list
    /// </summary>
    public class EpicRoomInfo
    {
        public string Name { get; set; }
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }
        public bool IsOpen { get; set; }
        public bool IsVisible { get; set; }
        public Dictionary<string, object> CustomProperties { get; set; }

        public EpicRoomInfo()
        {
            CustomProperties = new Dictionary<string, object>();
            IsOpen = true;
            IsVisible = true;
        }

        public override string ToString()
        {
            return $"{Name} ({PlayerCount}/{MaxPlayers})";
        }
    }
}