using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Tasks.ServerLoad;
using NLog;

namespace AAEmu.Game.Core.Network.Login;

public class LoginProtocolHandler : BaseProtocolHandler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private ConcurrentDictionary<uint, Type> _packets;
    private PacketStream _lastPacket;
    private LoadTask _loadTask;

    public LoginProtocolHandler()
    {
        _packets = new ConcurrentDictionary<uint, Type>();
    }

    public override void OnConnect(ISession session)
    {
        Logger.Info("Connect to {0} established, session id: {1}", session.Ip.ToString(), session.SessionId.ToString(CultureInfo.InvariantCulture));
        var con = new LoginConnection(session);
        con.OnConnect();
        LoginNetwork.Instance.SetConnection(con);

        _loadTask = new LoadTask();
        TaskManager.Instance.Schedule(_loadTask, null, TimeSpan.FromMinutes(1));
    }

    public override void OnDisconnect(ISession session)
    {
        Logger.Info("Connect to LoginServer has been lost");
        LoginNetwork.Instance.SetConnection(null);
        session.Close();
        if (_loadTask != null)
        {
            TaskManager.Instance.Cancel(_loadTask);
            _loadTask = null;
        }

        // TODO Hard Restart
        LoginNetwork.Instance.Stop();
        LoginNetwork.Instance.Start();
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        var stream = new PacketStream();
        var connection = LoginNetwork.Instance.GetConnection();
        if (_lastPacket != null)
        {
            stream.Insert(0, _lastPacket);
            _lastPacket = null;
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
                    var packet = (LoginPacket)Activator.CreateInstance(classType);
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

    public void RegisterPacket(uint type, Type classType)
    {
        if (_packets.ContainsKey(type))
            _packets.TryRemove(type, out _);

        _packets.TryAdd(type, classType);
    }

    private static void HandleUnknownPacket(LoginConnection connection, uint type, PacketStream stream)
    {
        var dump = new StringBuilder();
        for (var i = stream.Pos; i < stream.Count; i++)
            dump.AppendFormat("{0:x2} ", stream.Buffer[i]);
        Logger.Error("Unknown packet 0x{0:x2} from {1}:\n{2}", type, connection.Ip, dump);
    }
}
