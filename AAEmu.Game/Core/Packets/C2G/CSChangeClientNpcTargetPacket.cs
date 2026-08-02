using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// controlled NPC) at +0x10. The wire body is therefore <c>bc target, bc sourceNpc</c>.
/// </remarks>
public class CSChangeClientNpcTargetPacket() : GamePacket(CSOffsets.CSChangeClientNpcTargetPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var targetId = stream.ReadBc();
        var sourceNpcId = stream.ReadBc();

        var character = Connection.ActiveChar;
        if (character?.ParentWorld?.GetUnit(sourceNpcId) is not Npc npc || npc.OwnerId != character.Id)
        {
            Logger.Warn(
                "Rejected client NPC target source {0} from {1} ({2})",
                sourceNpcId, character?.Name ?? "<disconnected>", character?.ObjId ?? 0);
            return;
        }

        var target = targetId == 0 ? null : character.ParentWorld.GetUnit(targetId);
        if (targetId != 0 && target == null)
        {
            Logger.Warn(
                "Rejected missing client NPC target {0} for owned NPC {1} from {2}",
                targetId, sourceNpcId, character.Name);
            return;
        }

        npc.CurrentTarget = target;
        npc.BroadcastPacket(new SCTargetChangedPacket(sourceNpcId, target?.ObjId ?? 0), true);

        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayTargetChangedToZone?.Invoke(sourceNpcId, target?.ObjId ?? 0, false);
    }
}
