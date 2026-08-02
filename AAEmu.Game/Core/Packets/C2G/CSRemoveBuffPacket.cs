using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// <c>u8 reason</c>. Despite its native name, <c>buffId</c> is the runtime buff index used by
/// SCBuffRemoved and the unit's effect collection.
/// </remarks>
public class CSRemoveBuffPacket() : GamePacket(CSOffsets.CSRemoveBuffPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var unitId = stream.ReadBc();
        var buffId = stream.ReadUInt32();
        var reason = stream.ReadByte();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        BaseUnit target = null;
        if (unitId == character.ObjId)
        {
            target = character;
        }
        else
        {
            var mate = character.ParentWorld.MateManager.GetActiveMateByMateObjId(unitId);
            if (mate?.OwnerObjId == character.ObjId)
            {
                target = mate;
            }
            else
            {
                var slave = character.ParentWorld.SlaveManager.GetSlaveByObjId(unitId);
                if (slave != null &&
                    (slave.Summoner?.ObjId == character.ObjId || slave.OwnerObjId == character.ObjId))
                {
                    target = slave;
                }
            }
        }

        if (target == null)
        {
            Logger.Warn(
                "Rejected buff removal for unit {0} from {1} ({2})",
                unitId, character.Name, character.ObjId);
            return;
        }

        var buff = target.Buffs.GetEffectByIndex(buffId);
        if (buff == null)
        {
            Logger.Debug(
                "Ignored missing buff index {0} on owned unit {1} from {2}",
                buffId, unitId, character.Name);
            return;
        }

        // Client cancellation may remove beneficial effects only; hostile effects require a dispel.
        if (buff.Template.Kind != BuffKind.Good)
        {
            Logger.Warn(
                "Rejected client cancellation of non-beneficial buff index {0} on unit {1} from {2}",
                buffId, unitId, character.Name);
            return;
        }

        Logger.Debug(
            "Client buff cancellation unit={0} buffIndex={1} reason={2} character={3}",
            unitId, buffId, reason, character.Name);
        buff.Exit();
    }
}
