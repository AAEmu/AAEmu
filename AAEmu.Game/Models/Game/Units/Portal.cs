using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Units;

public sealed class Portal : Npc
{
    public Transform TeleportPosition { get; set; }
    public Npc LinkedPortal { get; set; }

    private void KillLinkedPortal()
    {
        // Make sure to mark this portal as "dead" to avoid loops
        Hp = 0;
        // Remove the linked portal as well if it's still alive
        if (LinkedPortal is { Hp: > 0 })
        {
            LinkedPortal.Delete();
        }
    }

    public override void DoDie(BaseUnit killer, KillReason killReason)
    {
        base.DoDie(killer, killReason);
        KillLinkedPortal();
    }

    public override void Delete()
    {
        // Broadcast its kill effect to be sure it's removed 
        BroadcastPacket(new SCUnitDeathPacket(ObjId, KillReason.PortalTimeout), false);
        // Do normal despawn handling
        base.Delete();
        KillLinkedPortal();
    }
}
