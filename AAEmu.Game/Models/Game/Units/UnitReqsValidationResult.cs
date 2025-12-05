using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Units;

public class UnitReqsValidationResult(SkillResultKeys result, ushort uShort, uint uInt)
{
    public SkillResultKeys ResultKey { get; set; } = result;
    public ushort ResultUShort { get; set; } = uShort;
    public uint ResultUInt { get; set; } = uInt;
}
