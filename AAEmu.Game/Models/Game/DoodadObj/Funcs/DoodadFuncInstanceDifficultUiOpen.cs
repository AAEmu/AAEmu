using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncInstanceDifficultUiOpen : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character || owner == null)
            return;

        var world = character.ParentWorld;
        var dungeon = world?.DungeonInstance;
        if (dungeon == null || owner.ParentWorld != world ||
            !dungeon.BeginDifficultySelection(character,
                () => owner.DoChangePhase(character, nextPhase)))
            return;

        character.SendPacket(new SCSelectedInstanceDifficultPacket((sbyte)(dungeon.Difficult ?? 0), showUi: true));
    }
}
