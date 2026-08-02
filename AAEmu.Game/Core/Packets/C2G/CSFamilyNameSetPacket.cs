using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// string name
/// </remarks>
public class CSFamilyNameSetPacket() : GamePacket(CSOffsets.CSFamilyNameSetPacket, 1)
{
    public string Name { get; private set; }

    public override void Read(PacketStream stream)
    {
        Name = stream.ReadString();
    }
}
