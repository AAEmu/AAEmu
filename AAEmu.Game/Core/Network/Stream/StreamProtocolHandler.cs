using System;
using System.Collections.Concurrent;
using System.Text;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Connections;
using NLog;

namespace AAEmu.Game.Core.Network.Stream;

public class StreamProtocolHandler : BaseProtocolHandler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private ConcurrentDictionary<uint, Type> _packets;

    public StreamProtocolHandler()
    {
        _packets = new ConcurrentDictionary<uint, Type>();
    }

    public override void OnConnect(ISession session)
    {
        Logger.Info("Connect from {0} established, session id: {1}", session.Ip.ToString(), session.SessionId.ToString());
        try
        {
            var con = new StreamConnection(session);
            StreamConnection.OnConnect();
            StreamConnectionTable.Instance.AddConnection(con);
        }
        catch (Exception e)
        {
            session.Close();
            Logger.Error(e);
        }
    }

    public override void OnDisconnect(ISession session)
    {
        try
        {
            var con = StreamConnectionTable.Instance.GetConnection(session.SessionId);
            if (con != null)
                StreamConnectionTable.Instance.RemoveConnection(session.SessionId);
        }
        catch (Exception e)
        {
            session.Close();
            Logger.Error(e);
        }

        Logger.Info("Client from {0} disconnected", session.Ip.ToString());
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        try
        {
            var connection = StreamConnectionTable.Instance.GetConnection(session.SessionId);
            if (connection == null)
                return;
            OnReceive(connection, buf, offset, bytes);
        }
        catch (Exception e)
        {
            session.Close();
            Logger.Error(e);
        }
    }

    public void OnReceive(StreamConnection connection, byte[] buf, int offset, int bytes)
    {
        try
        {
            var stream = new PacketStream();
            if (connection.LastPacket != null)
            {
                stream.Insert(0, connection.LastPacket);
                connection.LastPacket = null;
            }

            stream.Insert(stream.Count, buf, offset, bytes);
            while (stream != null && stream.Count > 0)
            {
                ushort len;
                try
                {
                    len = stream.ReadUInt16();
                }
                catch (MarshalException)
                {
                    //Logger.Warn("Error on reading type {0}", type);
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
                        var packet = (StreamPacket)Activator.CreateInstance(classType);
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
            Logger.Error(e);
        }
    }

    public void RegisterPacket(uint type, Type classType)
    {
        if (_packets.ContainsKey(type))
            _packets.TryRemove(type, out _);
        _packets.TryAdd(type, classType);
    }

    private static void HandleUnknownPacket(StreamConnection connection, uint type, PacketStream stream)
    {
        var dump = new StringBuilder();
        for (var i = stream.Pos; i < stream.Count; i++)
            dump.AppendFormat("{0:x2} ", stream.Buffer[i]);
        Logger.Error("Unknown packet 0x{0:x2} from {1}:\n{2}", (object)type, (object)connection.Ip, (object)dump);
    }
}
