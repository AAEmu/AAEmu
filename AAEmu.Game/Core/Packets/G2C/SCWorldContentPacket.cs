using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCWorldContentPacket : GamePacket
{
    // The content groups the client parses into the filter its per-category features consult, built from
    // the world_contents table. Sending it empty leaves that filter unconfigured, which is not what the
    // table says this server has: the table is the source, not a pre-serialized blob.
    private static byte[] _defaultBuffer;
    private readonly byte[] _filterBuffer;

    public SCWorldContentPacket(byte[] filterBuffer = null) : base(SCOffsets.SCWorldContentPacket, 1)
    {
        _filterBuffer = filterBuffer ?? LoadDefaultBuffer();
    }

    private static byte[] LoadDefaultBuffer()
    {
        return _defaultBuffer ??= WorldContentGameData.Instance.BuildPack();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)_filterBuffer.Length);
        if (_filterBuffer.Length > 0)
            stream.Write(_filterBuffer, false);
        return stream;
    }
}
