using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRemoveBuffPacket : GamePacket
{
    public CSRemoveBuffPacket() : base(CSOffsets.CSRemoveBuffPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var buffId = stream.ReadUInt32();
        var reason = stream.ReadByte();

        var mates = MateManager.Instance.GetActiveMates(Connection.ActiveChar.ObjId);
        foreach (var mate in mates)
        {
            if (mate?.ObjId == objId)
            {
                var mateEffect = mate.Buffs.GetEffectByIndex(buffId);
                if (RemoveEffect(mateEffect)) { return; }
            }
        }

        var slave = SlaveManager.Instance.GetSlaveByObjId(objId);
        if (slave != null)
        {
            var slaveEffect = slave.Buffs.GetEffectByIndex(buffId);
            if (RemoveEffect(slaveEffect)) { return; }
        }

        if (Connection.ActiveChar.ObjId != objId)
            return;
        var effect = Connection.ActiveChar.Buffs.GetEffectByIndex(buffId);
        if (RemoveEffect(effect)) { return; }

        return;

        bool RemoveEffect(Buff buffEffect)
        {
            if (buffEffect == null)
                return false;
            if (buffEffect.Template.Kind == BuffKind.Good)
                buffEffect.Exit();
            return true;
        }
    }
}
