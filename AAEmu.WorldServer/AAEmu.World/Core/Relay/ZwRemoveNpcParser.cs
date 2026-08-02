using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// <c>npcId</c> section as a three-byte <c>Bc</c>, followed by signed <c>i32 removeDelay</c>.
/// The dedicate sends finite-lifetime removals immediately with seconds converted to
/// </summary>
public readonly record struct ZwRemoveNpcParsed(uint NpcId, int RemoveDelayMilliseconds);

public static class ZwRemoveNpcParser
{
    public const int BodySize = 7;

    public static ZwRemoveNpcParsed? TryParse(byte[] raw)
    {
        if (raw is not { Length: BodySize })
            return null;

        var stream = new PacketStream();
        stream.Insert(0, raw);
        var npcId = stream.ReadBc();
        var removeDelay = stream.ReadInt32();
        return npcId == 0 ? null : new ZwRemoveNpcParsed(npcId, removeDelay);
    }
}
