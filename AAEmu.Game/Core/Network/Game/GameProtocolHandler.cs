using System;
using System.Collections.Concurrent;
using System.Text;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;

using NLog;

namespace AAEmu.Game.Core.Network.Game
{
    public class GameProtocolHandler : BaseProtocolHandler
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>> _packets;

        public GameProtocolHandler()
        {
            _packets = new ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>();
            _packets.TryAdd(1, new ConcurrentDictionary<uint, Type>()); // ordinary
            _packets.TryAdd(2, new ConcurrentDictionary<uint, Type>()); // proxy
            _packets.TryAdd(3, new ConcurrentDictionary<uint, Type>()); // deflate
            _packets.TryAdd(4, new ConcurrentDictionary<uint, Type>()); // deflate
            _packets.TryAdd(5, new ConcurrentDictionary<uint, Type>()); // encrypt
            _packets.TryAdd(6, new ConcurrentDictionary<uint, Type>()); // encrypt
        }

        public override void OnConnect(Session session)
        {
            _log.Info("Connect from {0} established, session id: {1}", session.Ip.ToString(), session.SessionId.ToString());
            try
            {
                var con = new GameConnection(session);
                con.OnConnect();
                GameConnectionTable.Instance.AddConnection(con);
            }
            catch (Exception e)
            {
                session.Close();
                _log.Error(e);
            }
        }

        public override void OnDisconnect(Session session)
        {
            try
            {
                var con = GameConnectionTable.Instance.GetConnection(session.SessionId);
                if (con != null)
                {
                    if (con.ActiveChar != null)
                    {
                        // On crash, force people out of the chat channels so we don't get phantom or duplicates
                        Managers.ChatManager.Instance.LeaveAllChannels(con.ActiveChar);
                        // ObjectIdManager.Instance.ReleaseId(con.ActiveChar.BcId);
                    }
                    con.OnDisconnect();
                    StreamManager.Instance.RemoveToken(con.Id);
                    GameConnectionTable.Instance.RemoveConnection(session.SessionId);
                }
            }
            catch (Exception e)
            {
                session.Close();
                _log.Error(e);
            }

            _log.Info("Client from {0} disconnected", session.Ip.ToString());
        }

        public override void OnReceive(Session session, byte[] buf, int bytes)
        {
            try
            {
                var connection = GameConnectionTable.Instance.GetConnection(session.SessionId);
                if (connection == null)
                {
                    return;
                }

                OnReceive(connection, buf, bytes);
            }
            catch (Exception e)
            {
                session.Close();
                _log.Error(e);
            }
        }

        // получаем пакеты от клиента
        public void OnReceive(GameConnection connection, byte[] buf, int bytes)
        {
            try
            {
                var stream = new PacketStream();
                if (connection.LastPacket != null)
                {
                    stream.Insert(0, connection.LastPacket);
                    connection.LastPacket = null;
                }
                stream.Insert(stream.Count, buf, 0, bytes);
                while (stream != null && stream.Count > 0)
                {
                    ushort len;
                    try
                    {
                        len = stream.ReadUInt16();
                    }
                    catch (MarshalException)
                    {
                        //_log.Warn("Error on reading type {0}", type);
                        stream.Rollback();
                        connection.LastPacket = stream;
                        stream = null;
                        continue;
                    }
                    var packetLen = len + stream.Pos;
                    if (packetLen <= stream.Count)
                    {
                        stream.Rollback();
                        var stream2 = new PacketStream();
                        stream2.Replace(stream, 0, packetLen);
                        if (stream.Count > packetLen)
                        {
                            var stream3 = new PacketStream();
                            stream3.Replace(stream, packetLen, stream.Count - packetLen);
                            stream = stream3;
                        }
                        else
                        {
                            stream = null;
                        }

                        stream2.ReadUInt16(); //len
                        stream2.ReadByte(); //unk
                        var level = stream2.ReadByte();

                        byte crc = 0;
                        byte counter = 0;
                        if (level == 1)
                        {
                            crc = stream2.ReadByte(); // TODO 1.2 crc
                            counter = stream2.ReadByte(); // TODO 1.2 counter
                        }
                        if (level == 5)
                        {
                            //пакет от клиента, дешифруем
                            //------------------------------
                            var input = new byte[stream2.Count - 2];
                            Buffer.BlockCopy(stream2, 2, input, 0, stream2.Count - 2);
                            var output = EncryptionManager.Instance.Decode(input, connection.Id, connection.AccountId);
                            var OutBytes = new byte[output.Length + 5];
                            Buffer.BlockCopy(stream2, 0, OutBytes, 0, 5);
                            Buffer.BlockCopy(output, 1, OutBytes, 5, output.Length - 1); // сформируем полный расшифрованные пакет
                            // заменим шифрованные данные на дешифрованные
                            var strm = new PacketStream();
                            strm.Write(OutBytes);
                            stream2.Replace(strm, 0, OutBytes.Length);
                            stream2.ReadUInt16();
                        }

                        var type = stream2.ReadUInt16();
                        _packets[level].TryGetValue(type, out var classType);
                        if (classType == null)
                        {
                            HandleUnknownPacket(connection, type, level, stream2);
                        }
                        else
                        {
                            var packet = (GamePacket)Activator.CreateInstance(classType);
                            packet.Level = level;
                            packet.Connection = connection;
                            packet.Decode(stream2);
                        }
                    }
                    else
                    {
                        stream.Rollback();
                        connection.LastPacket = stream;
                        stream = null;
                    }
                }
            }
            catch (Exception e)
            {
                connection?.Shutdown();
                _log.Error(e);
            }
        }

        public void RegisterPacket(uint type, byte level, Type classType)
        {
            if (_packets[level].ContainsKey(type))
            {
                _packets[level].TryRemove(type, out _);
            }

            _packets[level].TryAdd(type, classType);
        }

        private void HandleUnknownPacket(GameConnection connection, uint type, byte level, PacketStream stream)
        {
            var dump = new StringBuilder();
            for (var i = stream.Pos; i < stream.Count; i++)
                dump.AppendFormat("{0:x2} ", stream.Buffer[i]);
            _log.Error("Unknown packet 0x{0:x2}({3}) from {1}:\n{2}", type, connection.Ip, dump, level);
        }
    }
}
