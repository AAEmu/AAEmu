using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCGmDumpCompletedQuestsPacket(uint bytes, uint bc, string charName, IReadOnlyList<(int Idx, long Body)> entries) : GamePacket(SCOffsets.SCGmDumpCompletedQuestsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bytes);
        stream.WriteBc(bc);
        stream.Write(charName);
        stream.Write((uint)entries.Count);
        foreach (var e in entries)
        {
            stream.Write(e.Idx);
            stream.Write(e.Body);
        }
        return stream;
    }
}
