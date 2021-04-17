using System;
using System.Threading;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.Proxy;

namespace AAEmu.Game.Core.Network.Game
{
    public abstract class GamePacket : PacketBase<GameConnection>
    {
        public byte Level { get; set; }

        protected GamePacket(ushort typeId, byte level) : base(typeId)
        {
            Level = level;
        }
        
        /// <summary>
        /// This is called in Encode after Read() in the case of GamePackets
        /// The purpose is to separate packet data from packet behavior
        /// </summary>
        public virtual void Execute(){}

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
                    packet
                        .Write((byte)0) // hash
                        .Write((byte)0); // count
                }

                packet.Write(body, false);

                ps.Write(packet);
            }
            catch (Exception ex)
            {
                _log.Fatal(ex);
                throw;
            }

            // SC here you can set the filter to hide packets
            if (!(TypeId == PPOffsets.PongPacket && Level == 2) && // Pong
                !(TypeId == PPOffsets.FastPongPacket && Level == 2) && // FastPong
                !(TypeId == SCOffsets.SCUnitMovementsPacket && Level == 1) && // SCUnitMovements
                !(TypeId == SCOffsets.SCOneUnitMovementPacket && Level == 1)) // SCOneUnitMovement
            {
                //_log.Debug("GamePacket: S->C type {0:X} {2}\n{1}", TypeId, ps, this.ToString().Substring(23));
                _log.Trace("GamePacket: S->C type {0:X3} {1}", TypeId, ToString().Substring(23));

            }
            return ps;
        }

        public override PacketBase<GameConnection> Decode(PacketStream ps)
        {
            // CS here you can set the filter to hide packets
            if (!(TypeId == PPOffsets.PingPacket && Level == 2) && // Ping
                !(TypeId == PPOffsets.FastPingPacket && Level == 2) && // FastPing
                !(TypeId == CSOffsets.CSMoveUnitPacket && Level == 1)) // CSMoveUnit
            {
                //_log.Debug("GamePacket: C->S type {0:X} {2}\n{1}", TypeId, ps, this.ToString().Substring(23));
                _log.Trace("GamePacket: C->S type {0:X3} {1}", TypeId, ToString().Substring(23));
            }
            try
            {
                Read(ps);
                Execute();
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
