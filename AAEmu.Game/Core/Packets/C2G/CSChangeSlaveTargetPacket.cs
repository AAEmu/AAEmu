using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// <c>target</c> and <c>slave</c>, in that order.
/// </remarks>
public class CSChangeSlaveTargetPacket() : GamePacket(CSOffsets.CSChangeSlaveTargetPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var targetId = stream.ReadBc();
        var slaveId = stream.ReadBc();

        var character = Connection.ActiveChar;
        if (character?.ParentWorld?.GetUnit(slaveId) is not Slave slave ||
            slave.Summoner?.ObjId != character.ObjId)
        {
            Logger.Warn(
                "Rejected slave target source {0} from {1} ({2})",
                slaveId, character?.Name ?? "<disconnected>", character?.ObjId ?? 0);
            return;
        }

        var target = targetId == 0 ? null : character.ParentWorld.GetUnit(targetId);
        if (targetId != 0 && target == null)
        {
            Logger.Warn(
                "Rejected missing slave target {0} for slave {1} from {2}",
                targetId, slaveId, character.Name);
            return;
        }

        slave.CurrentTarget = target;
        slave.BroadcastPacket(new SCTargetChangedPacket(slaveId, target?.ObjId ?? 0), true);

        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayTargetChangedToZone?.Invoke(slaveId, target?.ObjId ?? 0, false);

        Logger.Debug("ChangeSlaveTarget, Target: {0}, Slave: {1}", targetId, slaveId);
    }
}
