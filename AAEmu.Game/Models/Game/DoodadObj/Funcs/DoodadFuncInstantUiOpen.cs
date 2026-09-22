using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>Server-side representation of the instant-game UI descriptor.</summary>
public sealed class DoodadFuncInstantUiOpen : DoodadFuncTemplate
{
    public uint ZoneGroupId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        // The descriptor itself does not change doodad state; queue actions use the instant-game packets.
    }
}
