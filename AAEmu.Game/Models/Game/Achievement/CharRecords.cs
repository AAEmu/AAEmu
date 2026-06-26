using AAEmu.Game.Models.Game.Achievement.Enums;

namespace AAEmu.Game.Models.Game.Achievement;

public partial class CharRecords
{
    public uint Id { get; set; }
    public CharRecordKind KindId { get; set; }
    // Signed: char_records stores -1 as a "no target"/unbounded sentinel (value2 = -1 for 3261 rows in
    // 10.0.2.13). Reading unsigned wraps -1 to 4294967295, so these are int, not uint.
    public int Value1 { get; set; }
    public int Value2 { get; set; }
}
