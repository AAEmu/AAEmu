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
            if (mate?.ObjId != objId) { continue; }
            var mateEffect = mate.Buffs.GetEffectByIndex(buffId);
            if (mateEffect == null)
                return;
            if (mateEffect.Template.Kind == BuffKind.Good)
                mateEffect.Exit();
        }

        if (Connection.ActiveChar.ObjId != objId)
            return;
        var effect = Connection.ActiveChar.Buffs.GetEffectByIndex(buffId);
        if (effect == null)
            return;
        if (effect.Template.Kind == BuffKind.Good)
            effect.Exit();
    }
}
