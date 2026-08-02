using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.World.Core.Relay;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Network;

/// <summary>
/// Client CS/SC stub — Phase 3/5 cutover target. Uses Game framing [u16 len][0xDD][level][…].
/// Relays CSMoveUnit to the primary zone; enter-world still needs a UnitState dump.
/// </summary>
public class ClientProtocolHandler : BaseProtocolHandler
{
    // Matches AAEmu.Game CSOffsets for 10.0.2.13
    private const ushort CSSelectCharacter = 0x04C;
    private const ushort CSSpawnCharacter = 0x04E;
    private const ushort CSNotifyInGame = 0x051;
    private const ushort CSMoveUnit = 0x0C8;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly Dictionary<uint, PacketStream> _buffers = new();
    private readonly MovementRelay _movementRelay = new();
    private readonly PlayerEnterService _playerEnter = new();
    private readonly Dictionary<uint, uint> _clientBcIds = new();

    public override void OnConnect(ISession session)
    {
        Logger.Info("Client connect from {0}", session.Ip);
    }

    public override void OnDisconnect(ISession session)
    {
        if (_clientBcIds.TryGetValue(session.SessionId, out var bcId))
        {
            _playerEnter.LeaveZone(bcId);
            _clientBcIds.Remove(session.SessionId);
        }

        _buffers.Remove(session.SessionId);
        Logger.Info("Client disconnect from {0}", session.Ip);
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        var stream = new PacketStream();
        if (_buffers.TryGetValue(session.SessionId, out var pending))
        {
            stream.Insert(0, pending);
            _buffers.Remove(session.SessionId);
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
                stream.Rollback();
                _buffers[session.SessionId] = stream;
                return;
            }

            var packetLen = len + stream.Pos;
            if (packetLen > stream.Count)
            {
                stream.Rollback();
                _buffers[session.SessionId] = stream;
                return;
            }

            stream.Rollback();
            var frame = new PacketStream();
            frame.Replace(stream, 0, packetLen);
            stream = stream.Count > packetLen
                ? new PacketStream().Replace(stream.Buffer, packetLen, stream.Count - packetLen)
                : null;

            frame.ReadUInt16(); // length
            _ = frame.ReadByte(); // 0xDD
            var level = frame.ReadByte();
            if (level == 1)
            {
                _ = frame.ReadByte();
                _ = frame.ReadByte();
            }

            var opcode = frame.ReadUInt16();
            var bodyLen = frame.Count - frame.Pos;
            var body = bodyLen > 0 ? frame.ReadBytes(bodyLen) : [];

            HandleCs(session, opcode, body);
        }
    }

    private void HandleCs(ISession session, ushort opcode, byte[] body)
    {
        switch (opcode)
        {
            case CSSelectCharacter:
                Logger.Info("CSSelectCharacter from {0} — lobby still via Game unless UnitState dump provided", session.Ip);
                break;
            case CSSpawnCharacter:
            case CSNotifyInGame:
                Logger.Info("CS 0x{0:X3} from {1} — enter-zone needs WZUnitState snapshot (see PlayerEnterService)", opcode, session.Ip);
                break;
            case CSMoveUnit:
                HandleMove(session, body);
                break;
            default:
                Logger.Info("CS opcode=0x{0:X3} len={1} from {2}", opcode, body.Length, session.Ip);
                break;
        }
    }

    private void HandleMove(ISession session, byte[] body)
    {
        // CSMoveUnit body is typically bc + movement body; forward movement portion as WZ.
        if (body.Length < 4)
            return;

        var bcId = (uint)(body[0] | (body[1] << 8) | (body[2] << 16));
        var zone = PlayerEnterService.ForUnit(bcId);
        if (zone == null)
        {
            Logger.Warn("CSMoveUnit but no ZoneLoaded for bcId={0}", bcId);
            return;
        }

        _clientBcIds[session.SessionId] = bcId;
        var movement = body.AsSpan(3).ToArray();
        _movementRelay.RelayClientMoveToZone(zone, bcId, movement);
    }
}
