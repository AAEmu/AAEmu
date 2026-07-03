using System.IO;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCWorldContentPacket : GamePacket
{
    // Body: "filterBufferSize" u32 + raw "filterBuffer" bytes.
    // The buffer is the server's content-filter table —
    // the enabled/blocked content groups and zone gates the client parses into its world-content data on entering
    // the world. Sending it empty leaves that data uninitialized, which nulls the downstream per-feature data the
    // HUD reads (e.g. the world-level exp modifier). The table is the live 10.0.2.13 server's SC_PACKET_WORLD_CONTENT
    // payload, loaded once from Data/world_content_filter.bin.
    private static byte[] _defaultBuffer;
    private readonly byte[] _filterBuffer;

    public SCWorldContentPacket(byte[] filterBuffer = null) : base(SCOffsets.SCWorldContentPacket, 1)
    {
        _filterBuffer = filterBuffer ?? LoadDefaultBuffer();
    }

    private static byte[] LoadDefaultBuffer()
    {
        if (_defaultBuffer != null)
            return _defaultBuffer;
        var path = Path.Combine("Data", "world_content_filter.bin");
        _defaultBuffer = File.Exists(path) ? File.ReadAllBytes(path) : [];
        return _defaultBuffer;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)_filterBuffer.Length);
        if (_filterBuffer.Length > 0)
            stream.Write(_filterBuffer, false);
        return stream;
    }
}
