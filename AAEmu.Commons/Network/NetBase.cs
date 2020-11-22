using AAEmu.Commons.Network.Core;

namespace AAEmu.Commons.Network
{
    public interface INetBase
    {
        void OnConnect(Session session);
        void OnReceive(Session session, byte[] buf, int bytes);
        void OnSend(Session session, byte[] buf, int offset, int bytes);
        void OnDisconnect(Session session);
        void RemoveSession(Session session);
    }
}

