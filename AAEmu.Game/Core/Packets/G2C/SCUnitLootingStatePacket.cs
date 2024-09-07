using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitLootingStatePacket : GamePacket
{
    private readonly uint _bc;
    private readonly byte _looting;
    private readonly bool _autoLoot;

    public SCUnitLootingStatePacket(uint bc, byte looting, bool autoLoot) : base(SCOffsets.SCUnitLootingStatePacket, 5)
    {
        _bc = bc;
        _looting = looting;
        _autoLoot = autoLoot;
    }
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_bc);
        stream.Write(_looting);
        stream.Write(_autoLoot);
        return stream;
    }
}
