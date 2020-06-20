namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class DoodadFuncGroups
    {
        public enum DoodadFuncGroupKind : uint
        {
            Start = 1,
            Normal = 2,
            End = 3
        }

        public uint Id { get; set; }
        public uint Almighty { get; set; }
        public DoodadFuncGroupKind GroupKindId { get; set; }
        public uint SoundId { get; set; }
    }
}
