using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.World.Core.Relay;

using NLog;

namespace AAEmu.World.Core.Network;

/// <summary>
/// Optional TCP Game→World bridge (legacy hybrid). Prefer in-process WorldIntegration.
/// Frame: [u16 len][u16 opcode][body].
/// </summary>
public class GameBridgeProtocolHandler : BaseProtocolHandler
{
    public const ushort OpPlayerEnter = 0x0001;
    public const ushort OpPlayerLeave = 0x0002;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly PlayerEnterService _enter = new();
    private readonly Dictionary<uint, PacketStream> _buffers = new();

    public override void OnConnect(ISession session)
    {
        Logger.Info("Game bridge connect from {0}", session.Ip);
    }

    public override void OnDisconnect(ISession session)
    {
        _buffers.Remove(session.SessionId);
        Logger.Info("Game bridge disconnect from {0}", session.Ip);
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        var stream = new PacketStream();
        if (_buffers.TryGetValue(session.SessionId, out var leftover))
        {
            stream.Insert(0, leftover);
            _buffers.Remove(session.SessionId);
        }

        stream.Insert(stream.Count, buf, offset, bytes);

        PacketStream? cursor = stream;
        while (cursor is { Count: > 0 })
        {
            switch (LengthPrefixedFrames.TryTake(ref cursor, LengthPrefixedFrames.MinOpcodePayloadBytes, out var frame))
            {
                case LengthPrefixedFrameResult.NeedMore:
                    if (cursor != null)
                        _buffers[session.SessionId] = cursor;
                    return;
                case LengthPrefixedFrameResult.DroppedInvalidLength:
                    Logger.Warn("Dropped invalid game-bridge frame from {0}", session.Ip);
                    continue;
                case LengthPrefixedFrameResult.GotFrame:
                    frame!.ReadUInt16();
                    var opcode = frame.ReadUInt16();
                    var bodyLen = frame.Count - frame.Pos;
                    var body = bodyLen > 0 ? frame.ReadBytes(bodyLen) : [];
                    Handle(opcode, body);
                    break;
            }
        }
    }

    private void Handle(ushort opcode, byte[] body)
    {
        switch (opcode)
        {
            case OpPlayerEnter:
            {
                if (body.Length < 8)
                    return;
                var bcId = BitConverter.ToUInt32(body, 0);
                var stateLen = BitConverter.ToInt32(body, 4);
                if (stateLen <= 0 || body.Length < 8 + stateLen)
                {
                    Logger.Warn("PlayerEnter bad stateLen={0} body={1}", stateLen, body.Length);
                    return;
                }

                var state = new byte[stateLen];
                Buffer.BlockCopy(body, 8, state, 0, stateLen);
                Logger.Info("Game bridge PlayerEnter bcId={0} stateLen={1}", bcId, stateLen);
                if (!_enter.EnterZone(bcId, state))
                    Logger.Warn("Game bridge PlayerEnter refused (no ZoneLoaded?)");
                break;
            }
            case OpPlayerLeave:
            {
                if (body.Length < 4)
                    return;
                var bcId = BitConverter.ToUInt32(body, 0);
                Logger.Info("Game bridge PlayerLeave bcId={0}", bcId);
                _enter.LeaveZone(bcId);
                break;
            }
            default:
                Logger.Info("Game bridge unknown opcode 0x{0:X4} len={1}", opcode, body.Length);
                break;
        }
    }
}
