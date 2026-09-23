using System.IO;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Content roster removal (10.0.2.13 <c>CSContentRosterDeletePacket</c>).
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// u32 count, then count x u64 value [array]
/// </remarks>
public class CSContentRosterDeletePacket() : GamePacket(CSOffsets.CSContentRosterDeletePacket, 1)
{
    public IReadOnlyList<ulong> RosterIds { get; private set; } = [];

    public override void Read(PacketStream stream)
    {
        if (stream.Pos + sizeof(uint) > stream.Count)
            throw new InvalidDataException(
                $"CSContentRosterDeletePacket: body is {stream.Count} byte(s); expected a leading u32 count.");

        var count = stream.ReadUInt32();
        var needed = (ulong)count * sizeof(ulong);
        if ((ulong)(stream.Count - stream.Pos) < needed)
            throw new InvalidDataException(
                $"CSContentRosterDeletePacket: count {count} needs {needed} byte(s) of roster ids, only {stream.Count - stream.Pos} remain.");

        var rosterIds = new List<ulong>((int)count);
        for (uint i = 0; i < count; i++)
            rosterIds.Add(stream.ReadUInt64());

        if (stream.Overran)
            throw new InvalidDataException("CSContentRosterDeletePacket: read past the end of the body.");

        RosterIds = rosterIds;

        if (Connection is { ActiveChar: not null } connection)
        {
            var outcome = ContentRosterService.Instance.Delete(
                connection.AccountId, RosterIds, ServerCalendar.UtcNow);

            connection.SendPacket(new SCContentRosterDeletePacket(
                outcome.Result,
                isExpired: false,
                outcome.Success ? ErrorMessageType.NoErrorMessage : ErrorMessageType.InternalError));
        }
    }
}
