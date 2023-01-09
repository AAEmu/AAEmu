using System;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
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
        public virtual void Execute() { }

        // отправляем шифрованные пакеты от сервера
        public override PacketStream Encode()
        {
            lock (Connection.WriteLock)
            {
                byte count = 0;
                var ps = new PacketStream();
                try
                {
                    var packet = new PacketStream()
                    .Write((byte)0xdd)
                    .Write(Level);

                    switch (Level)
                    {
                        case 1:
                            {
                                packet
                                    .Write((byte)0) // hash
                                    .Write((byte)0) // count
                                    .Write(TypeId)
                                    .Write(this);
                                break;
                            }
                        case 2:
                            {
                                packet
                                    .Write(TypeId)
                                    .Write(this);
                                break;
                            }
                        case 3:
                        case 4:
                        case 6:
                            break;
                        case 5:
                            {
                                //пакет от сервера DD05 шифруем с помощью XOR & AES
                                var bodyCrc = new PacketStream();
                                count = EncryptionManager.Instance.GetSCMessageCount(Connection.Id, Connection.AccountId);
                                bodyCrc.Write(count)
                                    .Write(TypeId)
                                    .Write(this);
                                EncryptionManager.Instance.IncSCMsgCount(Connection.Id, Connection.AccountId);
                                var crc8 = EncryptionManager.Instance.Crc8(bodyCrc); //посчитали CRC пакета
                                var data = new PacketStream();
                                data
                                    .Write(crc8) // CRC
                                    .Write(bodyCrc, false); // data
                                var encrypt = EncryptionManager.Instance.StoCEncrypt(data);
                                var body = new PacketStream()
                                    .Write(encrypt, false); // шифрованное тело пакета
                                packet
                                    .Write(body, false);
                                break;
                            }
                    }
                    ps.Write(packet); // отправляем весь пакет
                }
                catch (Exception ex)
                {
                    _log.Fatal(ex);
                    throw;
                }

                // SC here you can set the filter to hide packets
                if (
                       !(TypeId == 0x13 && Level == 2) // PongPacket
                    && !(TypeId == 0x16 && Level == 2) // FastPongPacket
                    && !(TypeId == SCOffsets.SCUnitMovementsPacket && Level == 1)
                    && !(TypeId == SCOffsets.SCOneUnitMovementPacket && Level == 1)
                    && !(TypeId == SCOffsets.SCGimmickMovementPacket && Level == 1)
                    && !(TypeId == SCOffsets.SCUnitPointsPacket && Level == 5)
                   )
                {
                    //_log.Debug("GamePacket: S->C type {0:X} {2}\n{1}", TypeId, ps, this.ToString().Substring(23));
                    //_log.Trace("GamePacket: S->C type {0:X3} {1}", TypeId, this.ToString().Substring(23));
                    //_log.Debug("GamePacket: S->C type {0:X} {2}\n{1}", TypeId, ps, this.ToString().Substring(23));
                    if (Level == 5)
                    {
                        _log.Debug("GamePacket: S->C type {0:X3} {1}. C: {2}{3}", TypeId, ToString()?.Substring(23), count, Verbose());
                    }
                    else
                    {
                        _log.Debug("GamePacket: S->C type {0:X3} {1}", TypeId, ToString()?.Substring(23));
                    }
                }

                if (TypeId == 0xFFF)
                {
                    _log.Error("UNKNOWN OPCODE FOR PACKET");
                    _log.Debug("GamePacket: S->C type {0:X3} {1}", TypeId, ToString()?.Substring(23));
                    throw new SystemException();
                }

                if (Connection.LastCount == count && Level == 5)
                {
                    _log.Error("Looks like we got double count my guy", count);
                }

                Connection.LastCount = count;
                return ps;
            }
        }

        public override PacketBase<GameConnection> Decode(PacketStream ps)
        {
            lock (Connection.ReadLock)
            {
                // CS here you can set the filter to hide packets
                if (!(TypeId == PPOffsets.PingPacket && Level == 2) &&
                !(TypeId == PPOffsets.FastPingPacket && Level == 2) &&
                !(TypeId == CSOffsets.CSMoveUnitPacket && Level == 5))
                {
                    //_log.Debug("GamePacket: C->S type {0:X} {2}\n{1}", TypeId, ps, this.ToString().Substring(23));
                    //_log.Trace("GamePacket: C->S type {0:X3} {1}", TypeId, this.ToString().Substring(23));
                    _log.Debug("GamePacket: C->S type {0:X3} {1}", TypeId, ToString()?.Substring(23));
                }

                if (TypeId == 0xFFF)
                {
                    _log.Error("UNKNOWN OPCODE FOR PACKET");
                    _log.Debug("GamePacket: S->C type {0:X3} {1}", TypeId, ToString()?.Substring(23));
                    throw new SystemException();
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
            }
            return this;
        }
    }
}
