using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client's answer to a zone-permission ask: 1 = OK, 0 = Cancel.
/// </summary>
/// <remarks>
/// The body is one byte. Nothing on the server opens an ask, and the permission-state refresh
/// is not sent until its body is implemented, so an answer is recorded and changes nothing.
/// </remarks>
public class CSAnswerZonePermissionPacket() : GamePacket(CSOffsets.CSAnswerZonePermissionPacket, 1)
{
    public byte Answer { get; private set; }

    public override void Read(PacketStream stream)
    {
        Answer = stream.ReadByte();

        if (Connection.ActiveChar == null)
            return;

        Logger.Debug("Zone permission answer from {0} ignored until an ask is opened (answer={1})",
            Connection.ActiveChar.Id, Answer);
    }
}
