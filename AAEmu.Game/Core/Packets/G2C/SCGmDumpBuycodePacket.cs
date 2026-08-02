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
public class SCGmDumpBuycodePacket(string name, IReadOnlyList<(sbyte TypeValue, ulong ItemId, ulong BuyCode, sbyte Status, ulong Registered, ulong Updated)> entries) : GamePacket(SCOffsets.SCGmDumpBuycodePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(name);
        stream.Write((uint)entries.Count);
        foreach (var e in entries)
        {
            stream.Write(e.TypeValue);
            stream.Write(e.ItemId);
            stream.Write(e.BuyCode);
            stream.Write(e.Status);
            stream.Write(e.Registered);
            stream.Write(e.Updated);
        }
        return stream;
    }
}
