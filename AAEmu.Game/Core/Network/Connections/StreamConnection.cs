using System.Net;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Network.Connections;

public class StreamConnection(ISession session)
{
    private int _requestId = -1;
    private readonly Dictionary<int, Doodad[]> _requests = [];

    public uint Id => session.SessionId;
    public IPAddress Ip => session.Ip;
    public GameConnection GameConnection { get; set; }
    public PacketStream LastPacket { get; set; }

    public int GetNextRequestId(Doodad[] doodads)
    {
        Interlocked.Increment(ref _requestId);
        _requests.Add(_requestId, doodads);
        return _requestId;
    }

    public Doodad[] GetRequest(int requestId)
    {
        if (_requests.TryGetValue(requestId, out var request))
            return request;
        return null;
    }

    public void RemoveRequest(int requestId)
    {
        _requests.Remove(requestId);
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
        session?.Close();
    }
}
