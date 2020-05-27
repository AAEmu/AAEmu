using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Login.Network.Message
{
    public interface ILoginHandler : IHandler
    {
        abstract void Handler(LoginSession session, LoginReadable message);
    }
    
    public abstract class LoginHandler : ILoginHandler
    {
        public void Handler(Session session, IReadable message)
        {
            Handler((LoginSession)session, (LoginReadable)message);
        }

        public abstract void Handler(LoginSession session, LoginReadable message);
    }
}
