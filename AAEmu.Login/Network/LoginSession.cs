using System.Collections.Generic;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Share.Character;
using AAEmu.Login.Models;
using AAEmu.Login.Network.Message;

namespace AAEmu.Login.Network
{
    public class
        LoginSession : ClientGameSession<LoginMessageOpcode, LoginMessageAttribute, LoginMessageHandlerAttribute>
    {
        public Account Account { get; set; }
        public List<CharacterInfo> Characters { get; set; }

        public LoginSession(LoginServer server) : base(server)
        {
        }
    }
}
