using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncChangeOtherDoodadPhase : DoodadPhaseFuncTemplate
{
    public int NextPhase { get; set; }
    public uint TargetDoodadId { get; set; }
    public int TargetPhase { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Debug($"DoodadFuncChangeOtherDoodadPhase: NextPhase={NextPhase}, TargetDoodadId={TargetDoodadId}, TargetPhase={TargetPhase}");

        var otherDoodads = WorldManager.GetAround<Doodad>(owner, 10);
        foreach (var otherDoodad in otherDoodads)
        {
            if (otherDoodad.TemplateId != TargetDoodadId) { continue; }
            otherDoodad.DoChangeOtherDoodadPhase(caster, otherDoodad, TargetPhase);
            return false;
        }

        return false;
    }
}
