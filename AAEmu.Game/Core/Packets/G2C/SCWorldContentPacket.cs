using System.IO;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCWorldContentPacket : GamePacket
{
    // The content groups the client parses into the per-category filter its features consult. The client
    // reads a received name as content that is BLOCKED, so this carries the server's block list - not the
    // world_contents catalog, whose thousands of rows name every content group the build ships across
    // every region and season. Replaying the catalog as a block list hid nearly everything it named: the
    // instance window showed only the handful of instances no catalog row mentioned.
    //
    // The block list the live server sends is loaded from Data/world_content_filter.bin. When that file
    // is absent the filter is left empty - the client's own default of nothing blocked.
    private const string PayloadPath = "Data/world_content_filter.bin";

    private static byte[] _defaultBuffer;
    private readonly byte[] _filterBuffer;

    public SCWorldContentPacket(byte[] filterBuffer = null) : base(SCOffsets.SCWorldContentPacket, 1)
    {
        _filterBuffer = filterBuffer ?? LoadDefaultBuffer();
    }

    /// <summary>
    /// The payload carried for <paramref name="payloadPath"/>, or an empty pack (nothing blocked) when the
    /// file is missing or empty. Never fabricated from the content catalog: a name on this wire is content
    /// the client hides, so cataloguing the table would hide the content it names.
    /// </summary>
    public static byte[] ResolveFilterBuffer(string payloadPath)
    {
        if (!string.IsNullOrEmpty(payloadPath) && File.Exists(payloadPath))
        {
            var payload = File.ReadAllBytes(payloadPath);
            if (payload.Length > 0)
                return payload;
        }

        return WorldContentFilterPack.Serialize([]);
    }

    private static byte[] LoadDefaultBuffer()
    {
        if (_defaultBuffer != null)
            return _defaultBuffer;

        var buffer = ResolveFilterBuffer(PayloadPath);
        if (buffer.Length > 2)
            Logger.Info("World content: sending the {0}-byte content-filter payload from {1}", buffer.Length, PayloadPath);
        else
            Logger.Info("World content: no {0}; sending an empty content filter (nothing blocked)", PayloadPath);

        return _defaultBuffer = buffer;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)_filterBuffer.Length);
        if (_filterBuffer.Length > 0)
            stream.Write(_filterBuffer, false);
        return stream;
    }
}


