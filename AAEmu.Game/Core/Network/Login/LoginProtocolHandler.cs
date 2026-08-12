using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
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

    private readonly ConcurrentDictionary<uint, Type> _packets = new();
    private PacketStream _lastPacket;
    private LoadTask _loadTask;

    public override void OnConnect(ISession session)
    {
        // Do not clear _lastPacket here: a rare race can deliver a complete frame before
        // SetConnection publishes; deferral must survive until the body can be decoded.
        Logger.Info("Connect to {0} established, session id: {1}", session.Ip.ToString(), session.SessionId.ToString(CultureInfo.InvariantCulture));
        var con = new LoginConnection(session);
        session.AddAttribute("LoginConnection", con);
        LoginNetwork.Instance.SetConnection(con);
        con.OnConnect();

        _loadTask = new LoadTask();
        TaskManager.Instance.Schedule(_loadTask, null, TimeSpan.FromMinutes(1));
    }

    public override void OnDisconnect(ISession session)
    {
        _lastPacket = null;
        Logger.Info("Connection to LoginServer has been lost");
        session.ClearAttribute("LoginConnection");
        LoginNetwork.Instance.SetConnection(null);
        session.Close();
        if (_loadTask != null)
        {
            TaskManager.Instance.Cancel(_loadTask);
            _loadTask = null;
        }

        // Intentional Stop clears IsRunning; do not reconnect after DI teardown.
        if (!LoginNetwork.Instance.IsRunning)
            return;

        LoginNetwork.Instance.RequestReconnect();
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        PacketStream? stream = new PacketStream();
        if (_lastPacket != null)
        {
            stream.Insert(0, _lastPacket);
            _lastPacket = null;
        }

        stream.Insert(stream.Count, buf, offset, bytes);
        while (stream is { Count: > 0 })
        {
            switch (LengthPrefixedFrames.TryTake(ref stream, LengthPrefixedFrames.MinOpcodePayloadBytes, out var frame))
            {
                case LengthPrefixedFrameResult.NeedMore:
                    _lastPacket = stream;
                    return;
                case LengthPrefixedFrameResult.DroppedInvalidLength:
                    Logger.Warn("Dropped invalid login-internal frame from {0}", session.Ip);
                    continue;
                case LengthPrefixedFrameResult.GotFrame:
                    var connection = ResolveConnection(session);
                    if (connection == null)
                    {
                        // Keep framing progress; defer body until OnConnect publishes the connection.
                        _lastPacket = PrependFrame(frame!, stream);
                        return;
                    }
                    frame!.ReadUInt16();
                    var type = frame.ReadUInt16();
                    _packets.TryGetValue(type, out var classType);
                    if (classType == null)
                    {
                        HandleUnknownPacket(connection, type, frame);
                    }
                    else
                    {
                        var packet = (LoginPacket)Activator.CreateInstance(classType);
                        packet.Connection = connection;
                        packet.Decode(frame);
                    }

                    break;
            }
        }
    }

    private static LoginConnection ResolveConnection(ISession session)
    {
        if (session.GetAttribute("LoginConnection") is LoginConnection fromSession)
            return fromSession;
        return LoginNetwork.Instance.GetConnection();
    }

    private static PacketStream PrependFrame(PacketStream frame, PacketStream? remainder)
    {
        var combined = new PacketStream();
        combined.Insert(0, frame);
        if (remainder is { Count: > 0 })
            combined.Insert(combined.Count, remainder);
        return combined;
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
