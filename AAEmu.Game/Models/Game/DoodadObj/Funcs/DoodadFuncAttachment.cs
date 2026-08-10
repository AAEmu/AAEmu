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

    /// <summary>
    /// The pose the character takes up, and the thing that says this is somewhere you sit or lie rather
    /// than a station you pilot.
    /// </summary>
    public int AnimActionId { get; init; }

    /// <summary>
    /// Whether this attachment seats a character on the doodad, as opposed to strapping them into a
    /// slave's control station.
    /// </summary>
    /// <remarks>
    /// Branching on BondKindId alone got this wrong. Of the 524 shipped attachments with no usable bond
    /// kind, 431 still carry an animation - beds, sun loungers, a pipe organ, and the Hero Throne, whose
    /// row is byte-for-byte a single chair (attach point 2, anim 19, space 1) except that its bond kind
    /// is 0. All of those fell through to the slave branch, which binds against owner.ParentObjId; a
    /// free-standing doodad has no slave parent, so nothing happened and the interaction died silently
    /// after the func was found.
    ///
    /// The animation is the discriminator the data actually supports: no shipped row has a real bond kind
    /// and no animation, so this is a strict superset of the old condition and cannot take a seat away
    /// from anything that already worked. The 93 rows left on the slave path are the helms, siege
    /// controls and gunner seats, which have no animation because the vehicle supplies the pose.
    /// </remarks>
    private bool IsSeat => BondKindId > BondKind.BondInvalid || AnimActionId != 0;

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncAttachment");
        if (caster is Character character)
        {
            if (IsSeat)
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
            // A control station on a slave - a helm, a siege weapon, a gunner's seat. The pose comes from
            // the vehicle, which is why these are exactly the rows with no animation of their own.
            else
            {
                character.ParentWorld.SlaveManager.BindSlave(character, owner.ParentObjId, AttachPointId, AttachUnitReason.BoardTransfer);
            }
        }
    }
}
