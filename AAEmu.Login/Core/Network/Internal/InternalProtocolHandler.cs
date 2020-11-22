using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using NLog;

namespace AAEmu.Login.Core.Network.Internal
{
    public class InternalProtocolHandler : BaseProtocolHandler
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private ConcurrentDictionary<uint, Type> _packets;

        public InternalProtocolHandler()
        {
            _packets = new ConcurrentDictionary<uint, Type>();
        }

        public override void OnConnect(Session session)
        {
            _log.Info("GameServer from {0} connected, session id: {1}", session.Ip.ToString(),
                session.Id.ToString(CultureInfo.InvariantCulture));
            var con = new InternalConnection(session);
            con.OnConnect();
            InternalConnectionTable.Instance.AddConnection(con);
        }

        public override void OnDisconnect(Session session)
        {
            _log.Info("GameServer from {0} disconnected", session.Ip.ToString());
            var gsId = session.GetAttribute("gsId");
            if (gsId != null)
                GameController.Instance.Remove((byte) gsId);
            InternalConnectionTable.Instance.RemoveConnection(session.Id);
        }

        public override void OnReceive(Session session, byte[] buf, int bytes)
        {
            var connection = InternalConnectionTable.Instance.GetConnection(session.Id);
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

                    stream2.ReadUInt16();
                    var type = stream2.ReadUInt16();
                    _packets.TryGetValue(type, out var classType);
                    if (classType == null)
                    {
                        HandleUnknownPacket(session, type, stream2);
                    }
                    else
                    {
                        try
                        {
                            var packet = (InternalPacket) Activator.CreateInstance(classType);
                            packet.Connection = connection;
                            packet.Decode(stream2);
                        }
                        catch (Exception e)
                        {
                            _log.Error("Error on decode packet {0}", type);
                            _log.Error(e);
                        }
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

        public void RegisterPacket(uint type, Type classType)
        {
            if (_packets.ContainsKey(type))
                _packets.TryRemove(type, out _);

            _packets.TryAdd(type, classType);
        }

        private void HandleUnknownPacket(Session session, uint type, PacketStream stream)
        {
            var dump = new StringBuilder();
            for (var i = stream.Pos; i < stream.Count; i++)
                dump.AppendFormat("{0:x2} ", stream.Buffer[i]);
            _log.Error("Unknown packet 0x{0:x2} from {1}:\n{2}", type, session.Ip, dump);
        }
    }
}
