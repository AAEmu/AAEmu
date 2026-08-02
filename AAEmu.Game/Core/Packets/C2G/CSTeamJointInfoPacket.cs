using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// ulong type, sbyte mode, string name, sbyte worldId
/// </remarks>
public class CSTeamJointInfoPacket() : GamePacket(CSOffsets.CSTeamJointInfoPacket, 1)
{
    public ulong Type { get; private set; }
    public sbyte Mode { get; private set; }
    public string Name { get; private set; }
    public sbyte WorldId { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadUInt64();
        Mode = stream.ReadSByte();
        Name = stream.ReadString();
        WorldId = stream.ReadSByte();
    }
}
