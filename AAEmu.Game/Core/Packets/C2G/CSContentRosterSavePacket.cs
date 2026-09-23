using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Saves one content roster under the title the client sent.
/// </summary>
/// <remarks>
/// Field order: string saveTitle.
/// </remarks>
public class CSContentRosterSavePacket() : GamePacket(CSOffsets.CSContentRosterSavePacket, 1)
{
    public string SaveTitle { get; private set; }

    public override void Read(PacketStream stream)
    {
        SaveTitle = stream.ReadString();
        if (Connection is not { ActiveChar: not null } connection)
            return;

        var id = ContentRosterService.Instance.Save(connection.AccountId, SaveTitle, ServerCalendar.UtcNow);
        connection.SendPacket(new SCContentRosterSavePacket(
            id != 0,
            id != 0 ? ErrorMessageType.NoErrorMessage : ErrorMessageType.InternalError));
    }
}
