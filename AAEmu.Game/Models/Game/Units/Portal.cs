using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Units;

public sealed class Portal : Npc
{
    /// <summary>Bit 1 of the SCUnitState NPC flag: walking into this unit sends CSUsePortal.</summary>
    private const byte EntranceFlag = 0x02;
    /// <summary>Bit 2: the client shows the destination name but does not auto-use the portal.</summary>
    private const byte ExitFlag = 0x04;

    public Transform TeleportPosition { get; set; }
    public Npc LinkedPortal { get; set; }

    /// <summary>
    /// The book entry this temporary portal was opened from. Identity is the entry object itself,
    /// never its id: private book ids and district_return_points ids share one id space, so the same
    /// id can name a private entry and a district entry at the same time.
    /// </summary>
    public AAEmu.Game.Models.Game.Portal SourcePortal { get; init; }

    /// <summary>The yellow portal that appears at the destination; it is not walked through.</summary>
    public bool IsExit { get; init; }

    /// <summary>
    /// Set once this portal is dead or has been deleted. It is the lifecycle state the cascade and the
    /// manager's cleanup read, replacing the old <c>hp &gt; 0</c> test, which depended on the portal
    /// template's stat formula rather than on what actually happened to the unit.
    /// </summary>
    public bool IsDeadOrDeleted { get; private set; }

    /// <summary>Set once the unit has been removed from the world; keeps Delete() idempotent.</summary>
    public bool IsDeleted { get; private set; }

    public override byte UnitStateFlag => IsExit ? ExitFlag : EntranceFlag;

    private void KillLinkedPortal()
    {
        // Make sure to mark this portal as "dead" to avoid loops
        Hp = 0;
        // Remove the linked portal as well if it is still alive
        if (LinkedPortal is Portal { IsDeadOrDeleted: false } linked)
        {
            linked.Delete();
        }
    }

    public override void DoDie(BaseUnit killer, KillReason killReason)
    {
        base.DoDie(killer, killReason);
        IsDeadOrDeleted = true;
        KillLinkedPortal();
    }

    public override void Delete()
    {
        if (IsDeleted)
            return;

        IsDeleted = true;
        IsDeadOrDeleted = true;
        // Broadcast its kill effect to be sure it's removed
        BroadcastPacket(new SCUnitDeathPacket(ObjId, KillReason.PortalTimeout), false);
        // Do normal despawn handling
        base.Delete();
        KillLinkedPortal();
    }
}
