using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSMakeTeamOfficerPacket() : GamePacket(CSOffsets.CSMakeTeamOfficerPacket, 1)
{
    public int TeamId { get; private set; }
    public ulong MemberId { get; private set; }

    public override void Read(PacketStream stream)
    {
        TeamId = stream.ReadInt32(); // Native wire name: tid.
        MemberId = stream.ReadUInt64(); // Native wire name: type.

        TeamManager.Instance.MakeTeamOfficer(Connection.ActiveChar, TeamId, MemberId);
    }
}
