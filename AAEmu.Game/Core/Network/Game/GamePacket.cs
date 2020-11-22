﻿using System;
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
            if (!(TypeId == 0x013 && Level == 2) && // Pong
                !(TypeId == 0x016 && Level == 2) && // FastPong
                !(TypeId == 0x06B && Level == 1) && // SCUnitMovements
                !(TypeId == 0x06C && Level == 1)) // SCOneUnitMovement
            {
                //_log.Debug("GamePacket: S->C type {0:X} {2}\n{1}", TypeId, ps, this.ToString().Substring(23));
                _log.Trace("GamePacket: S->C type {0:X3} {1}", TypeId, this.ToString().Substring(23));

            }
            return ps;
        }

        public override PacketBase<GameConnection> Decode(PacketStream ps)
        {
            // CS here you can set the filter to hide packets
            if (!(TypeId == 0x012 && Level == 2) && // Ping
                !(TypeId == 0x015 && Level == 2) && // FastPing
                !(TypeId == 0x089 && Level == 1)) // CSMoveUnit
            {
                //_log.Debug("GamePacket: C->S type {0:X} {2}\n{1}", TypeId, ps, this.ToString().Substring(23));
                _log.Trace("GamePacket: C->S type {0:X3} {1}", TypeId, this.ToString().Substring(23));
            }
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
