using System;
using System.Threading;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Network.Game
{
    public abstract class GamePacket : PacketBase<GameConnection>
    {
        public byte Level { get; set; }

        protected GamePacket(ushort typeId, byte level) : base(typeId)
        {
            Level = level;
        }

        public override PacketStream Encode()
        {
            var ps = new PacketStream();
            try
            {
                var packet = new PacketStream()
                    .Write((byte)0xdd)
                    .Write(Level);

                var body = new PacketStream()
                    .Write(TypeId)
                    .Write(this);

                if (Level == 1)
                {
                    var connectionPacketCount = Connection.PacketCount;

                    var hash = Helpers.Crc8(body.GetBytes());

                    packet
                        .Write((byte)hash) // hash
                        .Write((byte)connectionPacketCount); // count


                    Interlocked.Increment(ref connectionPacketCount);
                    Connection.PacketCount = connectionPacketCount > byte.MaxValue ? 0 : connectionPacketCount;
                }

                packet.Write(body, false);
                
                ps.Write(packet);
            }
            catch (Exception ex)
            {
                _log.Fatal(ex);
                throw;
            }

            if (!(TypeId == 0x013 && Level == 2) && !(TypeId == 0x066 && Level == 1) && !(TypeId == 0x068 && Level == 1))
                _log.Debug("GamePacket: S->C type {0:X}\n{1}", TypeId, ps);

            return ps;
        }

        public override PacketBase<GameConnection> Decode(PacketStream ps)
        {
            if (!(TypeId == 0x012 && Level == 2) && !(TypeId == 0x089 && Level == 1))
                _log.Debug("GamePacket: C->S type {0:X}\n{1}", TypeId, ps);

            try
            {
                Read(ps);
            }
            catch (Exception ex)
            {
                _log.Fatal(ex);
                throw;
            }

            return this;
        }
    }
}
