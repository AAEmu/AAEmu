using AAEmu.Commons.Network.Core;

namespace AAEmu.Commons.Network
{
    public abstract class BaseProtocolHandler
    {
        public virtual void OnConnect(Session session)
        {
        }

        public virtual void OnReceive(Session session, byte[] buf, int bytes)
        {
        }

        public virtual void OnSend(Session session, byte[] buf, int offset, int bytes)
        {
        }

        public virtual void OnDisconnect(Session session)
        {
        }
    }
}
