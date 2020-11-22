using System;
using System.Collections.Concurrent;
using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.Network.Connections;
using NLog;

namespace AAEmu.Login.Core.Network.Login
{
    public class LoginProtocolHandler : BaseProtocolHandler
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private ConcurrentDictionary<uint, Type> _packets;

        public LoginProtocolHandler()
        {
            _packets = new ConcurrentDictionary<uint, Type>();
        }

        public override void OnConnect(Session session)
        {
            _log.Info("Connect from {0} established, session id: {1}", session.Ip.ToString(), session.Id.ToString());
            try
            {
                var con = new LoginConnection(session);
                con.OnConnect();
                LoginConnectionTable.Instance.AddConnection(con);
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
                var con = LoginConnectionTable.Instance.GetConnection(session.Id);
                if (con != null)
                    LoginConnectionTable.Instance.RemoveConnection(session.Id);
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
                var connection = LoginConnectionTable.Instance.GetConnection(session.Id);
                if (connection == null)
                    return;
                OnReceive(connection, buf, bytes);
            }
            catch (Exception e)
            {
                session.Close();
                _log.Error(e);
            }
        }

        public void OnReceive(LoginConnection connection, byte[] buf, int bytes)
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
                            stream = null;

                        stream2.ReadUInt16(); //len
                        var type = stream2.ReadUInt16();
                        _packets.TryGetValue(type, out var classType);
                        if (classType == null)
                        {
                            HandleUnknownPacket(connection, type, stream2);
                        }
                        else
                        {
                            var packet = (LoginPacket) Activator.CreateInstance(classType);
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

        public void RegisterPacket(uint type, Type classType)
        {
            if (_packets.ContainsKey(type))
                _packets.TryRemove(type, out _);

            _packets.TryAdd(type, classType);
        }

        private void HandleUnknownPacket(LoginConnection connection, uint type, PacketStream stream)
        {
            var dump = new StringBuilder();
            for (var i = stream.Pos; i < stream.Count; i++)
                dump.AppendFormat("{0:x2} ", stream.Buffer[i]);
            _log.Error("Unknown packet 0x{0:x2} from {1}:\n{2}", (object) type, (object) connection.Ip, (object) dump);
        }
    }
}
