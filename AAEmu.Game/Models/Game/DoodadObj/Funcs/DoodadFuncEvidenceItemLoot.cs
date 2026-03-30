using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncEvidenceItemLoot : DoodadFuncTemplate
{
    // doodad_funcs
    public uint SkillId { get; set; }
    public short CrimeValue { get; init; }
    public uint CrimeKindId { get; init; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Warn("DoodadFuncEvidenceItemLoot");

    }
}
