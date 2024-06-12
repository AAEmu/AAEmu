using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCScheduledEventEndedPacket : GamePacket
{
    private readonly uint _id;
    private readonly string closeMsg;

    public SCScheduledEventEndedPacket() : base(SCOffsets.SCScheduledEventEndedPacket, 5)
    {
        _id = 1;
        closeMsg = "exp 500%";
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_id);
        stream.Write(closeMsg);

        return stream;
    }
}
