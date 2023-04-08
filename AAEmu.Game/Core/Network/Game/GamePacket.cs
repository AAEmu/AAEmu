using System;
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
            
            var logString = $"GamePacket: S->C type {TypeId:X3} {ToString()?.Substring(23)}{Verbose()}";
            switch (LogLevel)
            {
                case PacketLogLevel.Trace:
                    _log.Trace(logString);
                    break;
                case PacketLogLevel.Debug:
                    _log.Debug(logString);
                    break;
                case PacketLogLevel.Info:
                    _log.Info(logString);
                    break;
                case PacketLogLevel.Warning:
                    _log.Warn(logString);
                    break;
                case PacketLogLevel.Error:
                    _log.Error(logString);
                    break;
                case PacketLogLevel.Fatal:
                    _log.Fatal(logString);
                    break;
                case PacketLogLevel.Off:
                default:
                    break;
            }
            
            return ps;
        }

        public override PacketBase<GameConnection> Decode(PacketStream ps)
        {
            try
            {
                Read(ps);
                
                var logString = $"GamePacket: C->S type {TypeId:X3} {ToString()?.Substring(23)}{Verbose()}";
                switch (LogLevel)
                {
                    case PacketLogLevel.Trace:
                        _log.Trace(logString);
                        break;
                    case PacketLogLevel.Debug:
                        _log.Debug(logString);
                        break;
                    case PacketLogLevel.Info:
                        _log.Info(logString);
                        break;
                    case PacketLogLevel.Warning:
                        _log.Warn(logString);
                        break;
                    case PacketLogLevel.Error:
                        _log.Error(logString);
                        break;
                    case PacketLogLevel.Fatal:
                        _log.Fatal(logString);
                        break;
                    case PacketLogLevel.Off:
                    default:
                        break;
                }

                Execute();
            }
            catch (Exception ex)
            {
                _log.Error("GamePacket: C->S type {0:X3} {1}", TypeId, ToString()?.Substring(23));
                _log.Fatal(ex);
                throw;
            }

            return this;
        }
    }
}
