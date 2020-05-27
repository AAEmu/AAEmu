using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Commons.Network.Core
{
    public interface IProtocolHandler
    {
        abstract void OnConnected(Session session);
        abstract void OnReceived(Session session, byte[] buffer, long offset, long size);
        abstract void OnSend(Session session, byte[] buffer, long offset, long size);
        abstract void OnSend(Session session, IWritable message);
        abstract void OnDisconnected(Session session);

        // bool GetOpcode(IWritable message, out TOpcode opcode);
    }

    public class ProtocolHandler : IProtocolHandler
    {
        public virtual void OnConnected(Session session)
        {
        }

        public virtual void OnReceived(Session session, byte[] buffer, long offset, long size)
        {
        }

        public virtual void OnSend(Session session, byte[] buffer, long offset, long size)
        {
            session.SendAsync(buffer, offset, size);
        }

        public virtual void OnSend(Session session, IWritable message)
        {
        }

        public virtual void OnDisconnected(Session session)
        {
        }
    }
}
