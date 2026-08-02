using System.Net;
using System.Collections.Concurrent;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Network.Connections;

public class StreamConnection(ISession session)
{
    private int _requestId = -1;
    private readonly ConcurrentDictionary<uint, CellRequest> _requests = [];

    public sealed class CellRequest(int instanceId, uint cellX, uint cellY, Doodad[] doodads, uint next)
    {
        public int InstanceId { get; } = instanceId;
        public uint CellX { get; } = cellX;
        public uint CellY { get; } = cellY;
        public Doodad[] Doodads { get; } = doodads;
        public uint Next { get; set; } = next;
    }

    public uint Id => session.SessionId;
    public IPAddress Ip => session.Ip;
    public GameConnection GameConnection { get; set; }
    public PacketStream LastPacket { get; set; }

    public uint ReserveRequestId()
    {
        return unchecked((uint)Interlocked.Increment(ref _requestId));
    }

    public void AddRequest(uint requestId, CellRequest request)
    {
        _requests[requestId] = request;
    }

    public bool TryGetRequest(uint requestId, out CellRequest request)
    {
        return _requests.TryGetValue(requestId, out request);
    }

    public void RemoveRequest(uint requestId)
    {
        _requests.TryRemove(requestId, out _);
    }

    public void CancelCell(int instanceId, uint cellX, uint cellY)
    {
        foreach (var (requestId, request) in _requests)
        {
            if (request.InstanceId == instanceId && request.CellX == cellX && request.CellY == cellY)
                _requests.TryRemove(requestId, out _);
        }
    }

    public void ClearRequests()
    {
        _requests.Clear();
    }

    public void SendPacket(StreamPacket packet)
    {
        SendPacket(packet.Encode());
    }

    public void SendPacket(byte[] packet)
    {
        session?.SendPacket(packet);
    }

    public static void OnConnect()
    {
    }

    public void Shutdown()
    {
        ClearRequests();
        session?.Close();
    }
}
