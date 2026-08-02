using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// packet has no body. Every parameterless C2S type folds onto that one function, so the
/// shared address is identical-COMDAT folding, not a base-class fall-through.
/// The paired 10.0.2.13 response writes one u8 field named worldId. This is the configured
/// game-server shard ID, matching the value sent during character selection.
/// </remarks>
public class CSShowCurrentWorld() : GamePacket(CSOffsets.CSShowCurrentWorld, 1)
{
    public override void Read(PacketStream stream)
    {
        Connection.SendPacket(new SCShowCurrentWorldPacket(AppConfiguration.Instance.Id));
    }
}
