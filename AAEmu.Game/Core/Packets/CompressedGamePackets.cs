using System;
using System.Collections.Generic;
using System.Diagnostics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using Ionic.Zlib;

namespace AAEmu.Game.Core.Packets
{
    public class CompressedGamePackets : GamePacket
    {
        public List<GamePacket> Packets;

        public CompressedGamePackets() : base(0, 4)
        {
            Packets = new List<GamePacket>();
        }

        public void AddPacket(GamePacket packet)
        {
            Packets.Add(packet);
        }

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

                var packetsData = DeflateStream.CompressBuffer(packets);
                stream.Write(packetsData);
                ps.Write(stream);
                stopwatch.Stop();
                _log.Trace("DD04 Size {0} (compressed), {1} (uncompressed). Took {2}ms to write", packetsData.Length, packets.Count, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _log.Fatal(ex);
                throw;
            }

            foreach (var packet in Packets)
                _log.Trace("DD04 - GamePacket: S->C type {0:X3} {1}", packet.TypeId, packet.ToString().Substring(23));
            return ps;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Encode(), false);
            return stream;
        }
    }

}
