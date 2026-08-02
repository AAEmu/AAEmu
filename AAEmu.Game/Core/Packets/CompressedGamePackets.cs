using System.Diagnostics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets;

/// <summary>
/// had <b>zero</b> L4 frames; live World paths must send plain L5 SC instead.
/// Encode is hard-gated — set AAEMU_ALLOW_DD04=1 only for deliberate RE experiments.
/// </summary>
public class CompressedGamePackets() : GamePacket(0, 4)
{
    public List<GamePacket> Packets = [];

    private static bool AllowDd04 =>
        System.Environment.GetEnvironmentVariable("AAEMU_ALLOW_DD04") == "1";

    public void AddPacket(GamePacket packet)
    {
        Packets.Add(packet);
    }

    private static byte[] CompressPacketData(byte[] data)
    {
        var output = new MemoryStream();
        using (var deflateStream = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            deflateStream.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    public override PacketStream Encode()
    {
        if (!AllowDd04)
        {
            throw new InvalidOperationException(
                "DD04 (CompressedGamePackets / level-4 zip) is disabled. " +
                "Retail sniff used 0× L4 — send individual GamePackets. " +
                "Set AAEMU_ALLOW_DD04=1 only for experiments.");
        }

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
            Logger.Warn(
                "DD04 ALLOWED: Size {0} (compressed), {1} (uncompressed). Took {2}ms",
                packetsData.Length, packets.Count, stopwatch.ElapsedMilliseconds);
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
