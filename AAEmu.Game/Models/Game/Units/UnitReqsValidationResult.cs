using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Units;

public class UnitReqsValidationResult(SkillResultKeys result, ushort uShort, uint uInt)
{
    public SkillResultKeys ResultKey { get; set; } = result;
    public ushort ResultUShort { get; set; } = uShort;
    public uint ResultUInt { get; set; } = uInt;

    /// <summary>
    /// The client's display gate, byte 8 of the native nine-byte result. The list evaluator
    /// x2game-dev.dll 0x39796DA0 copies the failing row's unit_reqs.display_msg into it on an AND
    /// failure; success and an OR group that fails as a whole leave it true.
    /// </summary>
    public bool DisplayMessage { get; set; } = true;

    /// <summary>
    /// The result byte the client's own evaluator writes for this failure when ResultKey has no
    /// member that maps to it (the key table lives in SkillResult.cs, owned by the result-mapping
    /// work). Null whenever ResultKey already produces the native byte.
    /// </summary>
    public SkillResult? NativeResult { get; set; }
}
