using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Login.Network.Message
{
    public abstract class LoginReadable : IReadable
    {
        public abstract void Read(PacketStream stream);
    }
}
