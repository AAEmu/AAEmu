using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Shipyard;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCShipyardStatePacket(ShipyardData shipyardData) : GamePacket(SCOffsets.SCShipyardStatePacket, 5)
{
    private readonly int _step = shipyardData.Step;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(shipyardData);
        stream.Write(_step);

        return stream;
    }
}
