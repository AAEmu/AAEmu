using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// bc bc (3 bytes), bool grant, ushort err
/// </remarks>
public class CSFollowRespPacket() : GamePacket(CSOffsets.CSFollowRespPacket, 1)
{
    public uint Bc { get; private set; }
    public bool Grant { get; private set; }
    public ushort Err { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();
        Grant = stream.ReadBoolean();
        Err = stream.ReadUInt16();
    }
}
