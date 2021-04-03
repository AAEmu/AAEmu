using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Char.Templates
{
    public class CharacterTemplateConfig
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public WorldSpawnPosition Pos { get; set; }
        public byte NumInventorySlot { get; set; }
        public short NumBankSlot { get; set; }
    }
}
