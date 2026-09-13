using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncClimb : DoodadFuncTemplate
{
    // doodad_funcs
    public uint ClimbTypeId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncClimb");
        if (caster is not Character character || owner == null)
            return;

        // Start the hang the same way CSHangPacket/Hang do: bind the character to the ladder's
        // transform and announce it, so the client attaches with the doodad's (now resolved) rotation.
        character.Transform.StickyParent = owner.Transform;

        // No facing is set here, on purpose. The client recomputes the hang orientation every frame from
        // the doodad's own world rotation (climb_type 1 reduces it to yaw), so anything written here is
        // overwritten immediately and only risks a one-frame flicker. The doodad has to be spawned with
        // the rotation its attach helper carries - see ModelAttachPointGameData.
        character.BroadcastPacket(new SCHungPacket(character.ObjId, owner.ObjId), true);
        Logger.Info($"DoodadFuncClimb: {character.Name} hung on doodad {owner.TemplateId}/{owner.ObjId} climbType={ClimbTypeId}");
    }
}
