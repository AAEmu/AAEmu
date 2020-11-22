using System.Collections.Generic;
using System.Net;
using System.Threading;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Network.Connections
{
    public class StreamConnection
    {
        private Session _session;
        private int _requestId;
        private readonly Dictionary<int, Doodad[]> _requests;

        public uint Id => _session.Id;
        public IPAddress Ip => _session.Ip;
        public GameConnection GameConnection { get; set; }
        public PacketStream LastPacket { get; set; }

        public StreamConnection(Session session)
        {
            _session = session;
            _requestId = -1;
            _requests = new Dictionary<int, Doodad[]>();
        }

        public int GetNextRequestId(Doodad[] doodads)
        {
            Interlocked.Increment(ref _requestId);
            _requests.Add(_requestId, doodads);
            return _requestId;
        }

        public Doodad[] GetRequest(int requestId)
        {
            if(_requests.ContainsKey(requestId))
                return _requests[requestId];
            return null;
        }

        public void RemoveRequest(int requestId)
        {
            if(_requests.ContainsKey(requestId))
                _requests.Remove(requestId);
        }
        
        public void SendPacket(StreamPacket packet)
        {
            SendPacket(packet.Encode());
        }

        public void SendPacket(byte[] packet)
        {
            _session?.SendPacket(packet);
        }

        public void OnConnect()
        {
        }

        public void Shutdown()
        {
            _session?.Close();
        }
    }
}
