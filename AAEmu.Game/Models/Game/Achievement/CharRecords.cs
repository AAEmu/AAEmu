using AAEmu.Game.Models.Game.Achievement.Enums;

namespace AAEmu.Game.Models.Game.Achievement
{
    public partial class CharRecords
    {
        public uint Id { get; set; }
        public CharRecordKind KindId { get; set; }
        public uint Value1 { get; set; }
        public uint Value2 { get; set; }
    }
}
