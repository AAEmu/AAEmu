using AAEmu.Commons.Network.Core;

namespace AAEmu.Commons.Network;

public interface IBaseProtocolHandler
{
    void OnConnect(ISession session);
    void OnReceive(ISession session, byte[] buf, int offset, int bytes);
    void OnSend(ISession session, byte[] buf, int offset, int bytes);
    void OnDisconnect(ISession session);
}
