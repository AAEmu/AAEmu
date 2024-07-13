using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Units;

public class UnitReqsValidationResult
{
    public SkillResultKeys ResultKey { get; set; }
    public ushort ResultUShort { get; set; }
    public uint ResultUInt { get; set; }

    public UnitReqsValidationResult(SkillResultKeys result, ushort uShort, uint uInt)
    {
        ResultKey = result;
        ResultUShort = uShort;
        ResultUInt = uInt;
    }
}
