using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMateSpawnedPacket(Mate mate) : GamePacket(SCOffsets.SCMateSpawnedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mate.TlId);

        stream.Write(mate.Id);
        stream.Write(mate.ItemId);
        stream.Write(mate.UserState);
        stream.Write(mate.Experience);
        stream.Write(mate.Mileage);
        stream.Write(mate.SpawnDelayTime);

        // TODO - max 10 skills
        foreach (var skill in mate.Skills)
        {
            stream.Write(skill);
        }

        for (var i = 0; i < 10 - mate.Skills.Count; i++)
        {
            stream.Write(0);
        }

        return stream;
    }
}
