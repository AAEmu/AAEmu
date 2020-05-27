using System;
using System.Linq;
using System.Xml.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Login.Models;
using AAEmu.Login.Network.Message.Packets.Incoming;
using AAEmu.Login.Network.Message.Packets.Outgoing;
using AAEmu.Login.Network.Message.Static;
using AAEmu.Login.Utils;
using Microsoft.Extensions.Logging;

namespace AAEmu.Login.Network.Message.Handlers
{
    [LoginMessageHandler(LoginMessageOpcode.ClientRequestAuthTrion)]
    public class ClientRequestAuthTrionHandler : LoginHandler
    {
        private readonly ILogger<ClientRequestAuthTrionHandler> _logger;
        private readonly AuthContext _context;

        public ClientRequestAuthTrionHandler(ILogger<ClientRequestAuthTrionHandler> logger, AuthContext context)
        {
            _logger = logger;
            _context = context;
        }

        public override void Handler(LoginSession session, LoginReadable message)
        {
            if (message is ClientRequestAuthTrion packet)
            {
                void SendServerLoginDenied(LoginResponse error)
                {
                    session.SendMessage(new ServerLoginDenied {Reason = error, Vp = "", Message = ""});
                }

                var xmlDoc = XDocument.Parse(packet.Ticket);

                if (xmlDoc.Root == null)
                {
                    _logger.LogError("Catch parse ticket");
                    SendServerLoginDenied(LoginResponse.BadResponse);
                    return;
                }

                var username = xmlDoc.Root.Element("username")?.Value;
                var password = xmlDoc.Root.Element("password")?.Value;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    _logger.LogError("username or password is empty or white space");
                    SendServerLoginDenied(LoginResponse.BadResponse);
                    return;
                }

                var incomingPassword = Helpers.StringToByteArray(password);

                var account = _context.GetAccount(username);
                if (account == null)
                {
                    SendServerLoginDenied(LoginResponse.BadAccount);
                    return;
                }

                var dbPassword = Convert.FromBase64String(account.Password);
                if (!dbPassword.SequenceEqual(incomingPassword))
                {
                    SendServerLoginDenied(LoginResponse.BadAccount);
                    return;
                }

                session.Account = account;
                _logger.LogInformation($"Account {account.Username} logged in. Session : {session.Id}");

                session.SendMessage(new ServerJoinResponse {Reason = 0, Afs = 6});
                session.SendMessage(new ServerAuthResponse
                {
                    AccountId = account.Id, 
                    WebSessionKey = "65CCBF5AF8DB8B633D3C03C5A8735601", // TODO : Implement, generate web session key 
                    SlotCount = 6
                });
            }
        }
    }
}
