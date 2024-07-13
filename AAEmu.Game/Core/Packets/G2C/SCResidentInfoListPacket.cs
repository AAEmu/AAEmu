using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Residents;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResidentInfoListPacket : GamePacket
{
    private readonly List<Resident> _residents;

    public SCResidentInfoListPacket(List<Resident> residents) : base(SCOffsets.SCResidentInfoListPacket, 5)
    {
        _residents = residents;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ResidentManager.MaxCountResident);
        stream.Write(ResidentManager.MaxCountResident);
        stream.Write(true);
        foreach (var resident in _residents)
            resident.Write(stream);

        return stream;
    }
}
