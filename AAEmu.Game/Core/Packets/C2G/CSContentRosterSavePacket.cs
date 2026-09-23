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

        var outcome = ContentRosterService.Instance.Save(connection.ActiveChar, SaveTitle, ServerCalendar.UtcNow);
        var recorded = outcome.RecordedAt == DateTime.UnixEpoch
            ? 0L
            : new DateTimeOffset(ServerCalendar.AsUtc(outcome.RecordedAt)).ToUnixTimeSeconds();
        connection.SendPacket(new SCContentRosterSavePacket(
            outcome.Success, outcome.Error, outcome.Id, recorded, outcome.Title));
    }
}
