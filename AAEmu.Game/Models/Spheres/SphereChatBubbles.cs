using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Models.Spheres
{
    public class SphereChatBubbles
    {
        public uint Id { get; set; }
        public uint SphereBubbleId { get; set; }
        public bool IsStart { get; set; }
        public string Speech { get; set; }
        public uint NpcId { get; set; }
        public uint NpcSpawnerId { get; set; }
        public uint NextBubble { get; set; }
        public uint SoundId { get; set; }
        public uint Angle { get; set; }
        public ChatBubbleKind ChatBubbleKindId { get; set; }
        public string Facial { get; set; }
        public uint CameraId { get; set; }
        public string ChangeSpeakerName { get; set; }
    }
}
