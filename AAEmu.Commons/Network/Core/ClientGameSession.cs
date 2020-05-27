using System;
using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Commons.Network.Core
{
    public class ClientGameSession<TOpcode, TMessageAttribute, TMessageHandlerAttribute> : Session
        where TOpcode : Enum
        where TMessageAttribute : Attribute, IMessageAttribute<TOpcode>
        where TMessageHandlerAttribute : Attribute, IMessageHandlerAttribute<TOpcode>
    {
        public ClientGameSession(Server server) : base(server)
        {
        }

        public override void SendMessage(IWritable message)
        {
            ProtocolHandler.OnSend(this, message);
        }
    }
}
