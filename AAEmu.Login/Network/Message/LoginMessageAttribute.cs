using System;
using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Login.Network.Message
{
    [AttributeUsage(AttributeTargets.Class)]
    public class LoginMessageAttribute : Attribute, IMessageAttribute<LoginMessageOpcode>
    {
        public LoginMessageOpcode Opcode { get; set; }
        public byte Level { get; set; }

        public LoginMessageAttribute(LoginMessageOpcode opcode)
        {
            Opcode = opcode;
        }
    }
}
