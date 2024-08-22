using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTowerConfigPacket : GamePacket
{
    private readonly ushort _packetSize;
    private readonly ushort _unk1;
    private readonly ushort _unk2;
    private readonly ushort _unk3;
    private readonly string _msg1;
    private readonly string _msg2;

    public SCTowerConfigPacket() : base(SCOffsets.SCTowerConfigPacket, 5)
    {
        _packetSize = 46;
        _unk1 = 42;
        _unk2 = 0;
        _unk3 = 1;
        _msg1 = "tower_def";
        _msg2 = "tower_kraken_newserver";
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_packetSize);
        stream.Write(_packetSize);
        stream.Write(_unk1);
        stream.Write(_unk2);
        stream.Write(_unk3);
        stream.Write(_msg1);
        stream.Write(_unk3);
        stream.Write(_msg2);
        return stream;
    }
}
