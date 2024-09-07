using System;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Network.Game;

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

    // send encrypted packets from the server
    public override PacketStream Encode()
    {
        //lock (Connection.WriteLock)
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
                            // We encrypt the packet from the DD05 server using XOR & AES
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
                            //var encrypt = EncryptionManager.Instance.StoCEncrypt(data);
                            //var body = new PacketStream()
                            //    .Write(encrypt, false); // encrypted packet body
                            packet
                                .Write(EncryptionManager.Instance.StoCEncrypt(data), false);
                            break;
                        }
                }
                ps.Write(packet); // отправляем весь пакет
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex);
                throw;
            }

            var logString = $"GamePacket: S->C type [{Level}:{TypeId:X3}] C:[{count}:{EncryptionManager.Instance.GetSCMessageCount(Connection.Id, Connection.AccountId)}] {ToString()?.Substring(23)}{Verbose()}";
            switch (LogLevel)
            {
                case PacketLogLevel.Trace:
                    Logger.Trace(logString);
                    break;
                case PacketLogLevel.Debug:
                    Logger.Debug(logString);
                    break;
                case PacketLogLevel.Info:
                    Logger.Info(logString);
                    break;
                case PacketLogLevel.Warning:
                    Logger.Warn(logString);
                    break;
                case PacketLogLevel.Error:
                    Logger.Error(logString);
                    break;
                case PacketLogLevel.Fatal:
                    Logger.Fatal(logString);
                    break;
                case PacketLogLevel.Off:
                default:
                    break;
            }

            if (TypeId == 0xFFF)
            {
                Logger.Error("UNKNOWN OPCODE FOR PACKET");
                Logger.Debug($"GamePacket: S->C type [{Level}:{TypeId:X3}] C:[{count}:{EncryptionManager.Instance.GetSCMessageCount(Connection.Id, Connection.AccountId)}] {ToString()?.Substring(23)}{Verbose()}");
                throw new SystemException();
            }
            if (EncryptionManager.Instance.GetSCMessageCount(Connection.Id, Connection.AccountId) == count && Level == 5)
            {
                Logger.Error($"Looks like we got double count {count}");
            }
            if (EncryptionManager.Instance.GetSCMessageCount(Connection.Id, Connection.AccountId) - count > 1 && Level == 5)
            {
                Logger.Error($"Looks like we've had a counting glitch [{Level}:{TypeId:X3}] C:[{count}:{EncryptionManager.Instance.GetSCMessageCount(Connection.Id, Connection.AccountId)}] {ToString()?.Substring(23)}{Verbose()}");
            }

            Connection.LastCount = count;

            return ps;
        }
    }

    public override PacketBase<GameConnection> Decode(PacketStream ps)
    {
        //lock (Connection.ReadLock)
        {
            if (TypeId == 0xFFF)
            {
                Logger.Error("UNKNOWN OPCODE FOR PACKET");
                Logger.Debug($"GamePacket: S->C type [{Level}:{TypeId:X3}] {ToString()?.Substring(23)}{Verbose()}");
                throw new SystemException();
            }

            try
            {
                Read(ps);

                var logString = $"GamePacket: C->S type [{Level}:{TypeId:X3}] {ToString()?.Substring(23)}{Verbose()}";
                switch (LogLevel)
                {
                    case PacketLogLevel.Trace:
                        Logger.Trace(logString);
                        break;
                    case PacketLogLevel.Debug:
                        Logger.Debug(logString);
                        break;
                    case PacketLogLevel.Info:
                        Logger.Info(logString);
                        break;
                    case PacketLogLevel.Warning:
                        Logger.Warn(logString);
                        break;
                    case PacketLogLevel.Error:
                        Logger.Error(logString);
                        break;
                    case PacketLogLevel.Fatal:
                        Logger.Fatal(logString);
                        break;
                    case PacketLogLevel.Off:
                    default:
                        break;
                }

                Execute();
            }
            catch (Exception ex)
            {
                Logger.Error($"GamePacket: C->S type {TypeId:X3} {ToString()?.Substring(23)}{Verbose()}");
                Logger.Fatal(ex);
                throw;
            }

            return this;
        }
    }
}
