using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// </remarks>
public class CSDespawnSlavePacket() : GamePacket(CSOffsets.CSDespawnSlavePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var slaveObjId = stream.ReadBc();
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        character.ParentWorld.SlaveManager.Delete(character, slaveObjId, false);
    }
}
