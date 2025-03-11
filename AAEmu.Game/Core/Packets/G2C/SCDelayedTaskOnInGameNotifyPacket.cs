using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDelayedTaskOnInGameNotifyPacket : GamePacket
{
    public SCDelayedTaskOnInGameNotifyPacket() : base(SCOffsets.SCDelayedTaskOnInGameNotifyPacket, 5)
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        // empty body
        return stream;
    }
}
