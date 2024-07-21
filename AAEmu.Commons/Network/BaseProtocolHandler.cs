using AAEmu.Commons.Network.Core;

namespace AAEmu.Commons.Network;

public abstract class BaseProtocolHandler
{
    public virtual void OnConnect(ISession session)
    {
    }

    public virtual void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
    }

    public virtual void OnSend(ISession session, byte[] buf, int offset, int bytes)
    {
    }

    public virtual void OnDisconnect(ISession session)
    {
    }
}
