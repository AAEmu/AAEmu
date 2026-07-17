using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitsRemovedPacket(uint[] ids) : GamePacket(SCOffsets.SCUnitsRemovedPacket, 5)
{
    public const int MaxCountPerPacket = 500; // Suggested Maximum Size (originally 300)

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)ids.Length);
        foreach (var id in ids)
            stream.WriteBc(id);

        return stream;
    }

    public override string Verbose()
    {
        //if (_ids?.Length > 1)
            return $" - Removed {ids.Length} objects";
        //if (_ids?.Length == 1)
        //    return " - " + WorldManager.Instance.GetGameObject(_ids[0])?.DebugName();
        //return base.Verbose();
    }
}
