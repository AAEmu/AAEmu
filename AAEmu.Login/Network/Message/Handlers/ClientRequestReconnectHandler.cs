using AAEmu.Login.Login;
using AAEmu.Login.Models;
using AAEmu.Login.Network.Message.Packets.Incoming;
using AAEmu.Login.Network.Message.Packets.Outgoing;
using AAEmu.Login.Network.Message.Static;
using AAEmu.Login.Utils;
using Microsoft.Extensions.Logging;

namespace AAEmu.Login.Network.Message.Handlers
{
    [LoginMessageHandler(LoginMessageOpcode.ClientRequestReconnect)]
    public class ClientRequestReconnectHandler : LoginHandler
    {
        private readonly ILogger<ClientRequestReconnectHandler> _logger;
        private readonly AuthContext _context;
        private readonly IServerManager _serverManager;

        public ClientRequestReconnectHandler(ILogger<ClientRequestReconnectHandler> logger, AuthContext context,
            IServerManager serverManager)
        {
            _logger = logger;
            _context = context;
            _serverManager = serverManager;
        }

        public override void Handler(LoginSession session, LoginReadable message)
        {
            if (message is ClientRequestReconnect packet)
            {
                if (_serverManager.ReconnectContainsKey(packet.GameServerId, packet.AccountId, (uint)packet.Token))
                {
                    session.Account = _context.GetAccount(packet.AccountId); // TODO : Implement

                    session.SendMessage(new ServerJoinResponse {Reason = 0, Afs = 6});
                    session.SendMessage(new ServerAuthResponse
                    {
                        AccountId = packet.AccountId,
                        WebSessionKey =
                            "65CCBF5AF8DB8B633D3C03C5A8735601", // TODO : Implement, generate web session key
                        SlotCount = 6
                    });
                }
                else
                {
                    // TODO : Implement
                }
            }
        }
    }
}
