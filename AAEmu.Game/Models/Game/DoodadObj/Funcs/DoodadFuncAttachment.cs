using AAEmu.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncAttachment : DoodadFuncTemplate
{
    // doodad_funcs
    public AttachPointKind AttachPointId { get; init; }
    public int Space { get; init; }
    public BondKind BondKindId { get; init; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncAttachment");
        if (caster is Character character)
        {
            if (BondKindId > BondKind.BondInvalid)
            {
                var spot = owner.Seat.LoadPassenger(character, owner.ObjId, Space); // ask for a free meta number for landing
                if (spot == -1)
                {
                    return; // we leave if there is no place
                }

                character.Bonding = new BondDoodad(owner, AttachPointId, BondKindId, Space, spot);
                character.BroadcastPacket(new SCBondDoodadPacket(caster.ObjId, character.Bonding), true);
                WorldIntegration.RelayBondDoodadToZone?.Invoke(character.ObjId, character.Bonding, true);
                // SCBond is the client attach. Free seats stay unparented (CSMove world-space).
                // Transfer/slave/house seats parent to the resolved carrier (not the seat doodad).
                var carrier = BondDoodad.ResolveCarrierUnit(owner);
                if (carrier != null)
                    character.Transform.Parent = carrier.Transform;
            }
            // Ships // TODO Check how sit on the ship
            else
            {
                character.ParentWorld.SlaveManager.BindSlave(
                    character, owner.ParentObjId, AttachPointId, AttachUnitReason.BoardTransfer,
                    occupySkillId: (int)skillId);
            }
        }
    }
}
