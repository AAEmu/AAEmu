using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers;
using AAEmu.Login.Models;
using NLog;

namespace AAEmu.Login.Core.Network.Internal;

public class InternalProtocolHandler(IGameController gameController, IInternalConnectionTable internalConnectionTable)
    : BaseProtocolHandler, IInternalProtocolHandler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<uint, (Type Type, IInternalPacketHandler PacketHandler)> _packets = [];

    public override void OnConnect(ISession session)
    {
        Logger.Info("GameServer from {0} connected, session id: {1}", session.Ip.ToString(),
            session.SessionId.ToString(CultureInfo.InvariantCulture));
        var con = new InternalConnection(session);
        InternalConnection.OnConnect();
        internalConnectionTable.AddConnection(con);
    }

    public override void OnDisconnect(ISession session)
    {
        Logger.Info("GameServer from {0} disconnected", session.Ip.ToString());
        if (session.GetAttribute("gsId") is { } gsId)
            gameController.Remove((GameServerId)gsId);
        internalConnectionTable.RemoveConnection(session.SessionId);
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        var connection = internalConnectionTable.GetConnection(session.SessionId);
        if (connection == null)
        {
            Logger.Error("Connection not found for session {0}", session.SessionId);
            return;
        }
        
        var stream = new PacketStream();
        if (connection.LastPacket != null)
        {
            stream.Insert(0, connection.LastPacket);
            connection.LastPacket = null;
        }

        stream.Insert(stream.Count, buf, offset, bytes);
        while (stream is { Count: > 0 })
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

                stream2.ReadUInt16();
                var type = stream2.ReadUInt16();
                if (!_packets.TryGetValue(type, out var tuple))
                {
                    HandleUnknownPacket(session, type, stream2); 
                }
                else
                {
                    var (classType, packetHandler) = tuple;
                    try
                    {
                        var packet = (InternalPacket)Activator.CreateInstance(classType)!;
                        packet.Connection = connection;
                        packet.Decode(stream2);

                        try
                        {
                            packetHandler.Execute(packet, connection);
                        }
                        catch (Exception e)
                        {
                            Logger.Error("Error on execute packet {0}", type);
                            Logger.Error(e);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error("Error on decode packet {0}", type);
                        Logger.Error(e);
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

    public void RegisterPacket<TPacket>(uint type, IInternalPacketHandler<TPacket> packetHandler) where TPacket : InternalPacket
    {
        _packets.AddOrUpdate(type,
            addValueFactory: static (_, arg) => arg,
            updateValueFactory: static (_, _, arg) => arg,
            factoryArgument: (typeof(TPacket), packetHandler));
    }

    private static void HandleUnknownPacket(ISession session, uint type, PacketStream stream)
    {
        var dump = new StringBuilder();
        for (var i = stream.Pos; i < stream.Count; i++)
            dump.AppendFormat("{0:x2} ", stream.Buffer[i]);
        Logger.Error("Unknown packet 0x{0:x2} from {1}:\n{2}", type, session.Ip, dump);
    }
}
