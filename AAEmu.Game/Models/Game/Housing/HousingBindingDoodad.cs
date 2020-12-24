using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Housing
{
    public class HousingBindingDoodad
    {
        public sbyte AttachPointId { get; set; }
        public uint DoodadId { get; set; }
        public WorldSpawnPosition Position { get; set; }
    }
}
