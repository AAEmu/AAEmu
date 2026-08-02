using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// </remarks>
public class CSBanVoteRequestPacket() : GamePacket(CSOffsets.CSBanVoteRequestPacket, 1)
{
    public uint UnitId { get; private set; }
    public bool VoteStart { get; private set; }
    public bool CheckEnable { get; private set; }
    public byte Reason { get; private set; }

    public override void Read(PacketStream stream)
    {
        UnitId = stream.ReadBc();
        VoteStart = stream.ReadBoolean();
        CheckEnable = stream.ReadBoolean();
        Reason = stream.ReadByte();
    }
}
