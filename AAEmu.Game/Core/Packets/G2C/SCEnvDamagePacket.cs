using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCEnvDamagePacket : GamePacket
{
    private readonly EnvSource _source;
    private readonly uint _target;
    private readonly uint _amount;
    private readonly uint _gimmickId;

    public SCEnvDamagePacket(EnvSource source, uint target, uint amount, uint gimmickId = 0) : base(SCOffsets.SCEnvDamagePacket, 1)
    {
        _source = source;
        _target = target;
        _amount = amount;
        _gimmickId = gimmickId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_source);
        stream.WriteBc(_target);
        stream.Write(_amount);
        if (_source == EnvSource.Gimmick)
            stream.Write(_gimmickId);
        return stream;
    }
}
