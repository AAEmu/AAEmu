using System;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;

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
        public virtual void Execute() { }

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

                if (Level == 5)
                {
                    //пакет от сервера DD05 шифруем с помощью XOR
                    var bodyCrc = new PacketStream()
                        .Write(EncryptionManager.Instance.GetSCMessageCount(Connection.Id, Connection.AccountId))
                        .Write(TypeId)
                        .Write(this);

                    var crc8 = EncryptionManager.Instance.Crc8(bodyCrc); //посчитали CRC пакета

                    var data = new PacketStream();
                    data
                        .Write(crc8) // CRC
                        .Write(bodyCrc, false); // data

                    var encrypt = EncryptionManager.Instance.StoCEncrypt(data);
                    body = new PacketStream();
                    body.Write(encrypt, false);
                    EncryptionManager.Instance.IncSCMsgCount(Connection.Id, Connection.AccountId);
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
            if (!(TypeId == 0x013 && Level == 2)    // Pong
                && !(TypeId == 0x016 && Level == 2) // FastPong
                && !(TypeId == SCOffsets.SCUnitMovementsPacket && Level == 5)  // SCUnitMovements
                //&& !(TypeId == SCOffsets.SCOneUnitMovementPacket && Level == 5)   // SCOneUnitMovement
                )
            {
                //_log.Debug("GamePacket: S->C type {0:X} {2}\n{1}", TypeId, ps, ToString().Substring(23));
                //_log.Trace("GamePacket: S->C type {0:X3} {1}", TypeId, this.ToString().Substring(23));
                _log.Warn("GamePacket: S->C type {0:X3} {1}", TypeId, ToString().Substring(23));
            }

            return ps;
        }

        public override PacketBase<GameConnection> Decode(PacketStream ps)
        {
            // CS here you can set the filter to hide packets
            if (!(TypeId == 0x012 && Level == 2) && // Ping
                !(TypeId == 0x015 && Level == 2) //&& // FastPing
                //!(TypeId == CSOffsets.CSMoveUnitPacket && Level == 5)  // CSMoveUnit
                )
            {
                _log.Debug("GamePacket: C->S type {0:X} {2}\n{1}", TypeId, ps, ToString().Substring(23));
                //_log.Trace("GamePacket: C->S type {0:X3} {1}", TypeId, this.ToString().Substring(23));
                //_log.Warn("GamePacket: C->S type {0:X3} {1}", TypeId, ToString().Substring(23));
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
