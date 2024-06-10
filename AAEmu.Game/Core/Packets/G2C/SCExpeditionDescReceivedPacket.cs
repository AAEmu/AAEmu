using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionDescReceivedPacket : GamePacket
{
    private readonly Expedition _expedition;

    public SCExpeditionDescReceivedPacket(Expedition expedition) : base(SCOffsets.SCExpeditionDescReceivedPacket, 5)
    {
        _expedition = expedition;
    }

    public override PacketStream Write(PacketStream stream)
    {
        _expedition.WriteInfo(stream);
        return stream;
    }
}
