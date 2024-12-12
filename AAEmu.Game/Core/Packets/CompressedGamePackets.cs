using System;
using System.Collections.Generic;
using System.Diagnostics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using System.IO;

namespace AAEmu.Game.Core.Packets;

public class CompressedGamePackets : GamePacket
{
    public List<GamePacket> Packets;

    public CompressedGamePackets() : base(0, 4)
    {
        Packets = [];
    }

    public void AddPacket(GamePacket packet)
    {
        Packets.Add(packet);
    }

    // Replacement for Ionic.ZLib.Core function
    private static byte[] CompressPacketData(byte[] data)
    {
        var output = new MemoryStream();
        using (var deflateStream = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            deflateStream.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    /*
    // Unused
    private static byte[] DecompressPacketData(byte[] data)
    {
        var input = new MemoryStream(data);
        var output = new MemoryStream();
        using (var deflateStream = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
        {
            deflateStream.CopyTo(output);
        }
        return output.ToArray();
    }
    */

    public override PacketStream Encode()
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        var ps = new PacketStream();
        try
        {
            var stream = new PacketStream()
                .Write((byte)0xdd)
                .Write(Level)
                .Write((ushort)Packets.Count);

            var packets = new PacketStream();
            foreach (var packet in Packets)
            {
                packets.Write((ushort)0)
                    .Write(packet.TypeId)
                    .Write(packet);
            }

            var packetsData = CompressPacketData(packets);
            stream.Write(packetsData);
            ps.Write(stream);
            stopwatch.Stop();
            Logger.Trace("DD04 Size {0} (compressed), {1} (uncompressed). Took {2}ms to write", packetsData.Length, packets.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex);
            throw;
        }

        foreach (var packet in Packets)
            Logger.Trace("DD04 - GamePacket: S->C type {0:X3} {1}", packet.TypeId, packet.ToString().Substring(23));
        return ps;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Encode(), false);
        return stream;
    }
}
