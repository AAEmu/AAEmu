using System;
using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Login.Network.Message
{
    [AttributeUsage(AttributeTargets.Class)]
    public class LoginMessageHandlerAttribute : Attribute, IMessageHandlerAttribute<LoginMessageOpcode>
    {
        public LoginMessageOpcode Opcode { get; set; }

        public LoginMessageHandlerAttribute(LoginMessageOpcode opcode)
        {
            Opcode = opcode;
        }
    }
}
