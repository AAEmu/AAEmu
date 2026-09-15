using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client's notice that a criminal is locked up. Its single field is an 8-byte value the client's
/// own reader names "type" - the same leading field the rest of the crime family carries.
/// </summary>
public class CSCriminalLockedPacket() : GamePacket(CSOffsets.CSCriminalLockedPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var type = stream.ReadInt64();

        Logger.Debug("CriminalLocked, Type: {0}", type);
    }
}
