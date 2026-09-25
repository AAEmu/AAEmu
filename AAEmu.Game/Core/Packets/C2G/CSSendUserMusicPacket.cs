using System.IO;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSendUserMusicPacket() : GamePacket(CSOffsets.CSSendUserMusicPacket, 1)
{
    // The client serializes a signed size, clamps it to 0..0x8000, and writes exactly that many
    // length-prefixed MIDI bytes. A single trailing null is outside the declared data and optional.
    internal const int MaximumMidiBytes = 0x8000;
    private const int HeaderSize = sizeof(int) + sizeof(ushort);

    public override void Read(PacketStream stream)
    {
        var player = Connection?.ActiveChar ?? throw new InvalidOperationException("Music packet has no active character.");
        byte[] data;
        int songSize;
        try
        {
            data = ReadMidiBlock(stream, out songSize);
            if (!MusicManager.Instance.CacheMidi(player.Id, data))
                throw new InvalidDataException("The MIDI block was valid but could not be cached.");
        }
        catch
        {
            // A malformed replacement must not leave the previous performance available to the next
            // play skill. Valid packets replace it; rejected packets remove it.
            MusicManager.Instance.ClearMidiCache(player.Id);
            throw;
        }

        Logger.Debug("Caching MIDI data size: {0}/{1}", data.Length, songSize);
    }

    /// <summary>
    /// Reads the signed declared song size and the length-prefixed MIDI block. The serializer clamps
    /// the declaration to its protocol range and emits exactly that many bytes. A single trailing
    /// null is accepted separately because it is not included in the declared data length.
    /// </summary>
    internal static byte[] ReadMidiBlock(PacketStream stream, out int songSize)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.Overran || stream.LeftBytes < HeaderSize)
            throw new InvalidDataException("The MIDI packet header is truncated.");

        songSize = stream.ReadInt32();
        var blockSize = stream.ReadUInt16();
        if (stream.Overran)
            throw new InvalidDataException("The MIDI packet header could not be read.");

        if (songSize <= 0 || songSize > MaximumMidiBytes)
            throw new InvalidDataException($"The declared MIDI size {songSize} is outside the protocol range.");

        if (blockSize == 0 || blockSize != songSize)
            throw new InvalidDataException($"The MIDI block size {blockSize} does not match the declared size {songSize}.");

        if (stream.LeftBytes < blockSize)
            throw new InvalidDataException("The MIDI block is truncated.");

        var data = stream.ReadBytes(blockSize);
        if (stream.Overran || data.Length != blockSize)
            throw new InvalidDataException("The MIDI block could not be read completely.");

        if (stream.LeftBytes > 1)
            throw new InvalidDataException("The MIDI packet has unexpected trailing data.");

        if (stream.LeftBytes == 1 && stream.ReadByte() != 0)
            throw new InvalidDataException("The MIDI packet has a non-null trailing terminator.");

        return data;
    }
}
