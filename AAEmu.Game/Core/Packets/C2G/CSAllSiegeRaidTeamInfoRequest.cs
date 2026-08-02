using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// short type
/// </remarks>
public class CSAllSiegeRaidTeamInfoRequest() : GamePacket(CSOffsets.CSAllSiegeRaidTeamInfoRequest, 1)
{
    public short Type { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadInt16();
    }
}
