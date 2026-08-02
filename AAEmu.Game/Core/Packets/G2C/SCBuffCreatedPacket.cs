using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBuffCreatedPacket(Buff buff) : GamePacket(SCOffsets.SCBuffCreatedPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        BuffCreatedWire.Write(stream, buff);
        return stream;
    }

    public override string Verbose()
    {
        return $" - {buff.Owner.DebugName()} <- {buff?.Template?.BuffId}";
    }
}
