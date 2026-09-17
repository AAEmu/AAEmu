using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for the key tables of the second password window the client is about to open. The request carries
/// which window that is, and the answer echoes it back.
/// </summary>
public class CSRequestSecondPasswordKeyTablesPacket() : GamePacket(CSOffsets.CSRequestSecondPasswordKeyTablesPacket, 1)
{
    public byte Pktm { get; private set; }

    public override void Read(PacketStream stream)
    {
        Pktm = stream.ReadByte();
        Logger.Debug("RequestSecondPasskeytable, pktm: {0}", Pktm);

        var connection = Connection;
        if (connection == null)
            return;

        var tables = SecondPasswordManager.Instance.Issue(connection.AccountId, out var time);
        connection.SendPacket(new SCSecondPassKeyTablesPacket(Pktm, time, tables));
    }
}
