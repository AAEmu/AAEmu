using System;
using AAEmu.Commons.Messages;
using AAEmu.Login.Login;
using AAEmu.Login.Network.Message.Packets.Incoming;
using AAEmu.Login.Network.Message.Packets.Outgoing;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace AAEmu.Login.Network.Message.Handlers
{
    [LoginMessageHandler(LoginMessageOpcode.ClientEnterWorld)]
    public class ClientEnterWorldHandler : LoginHandler
    {
        private readonly ILogger<ClientEnterWorldHandler> _logger;
        private readonly IServerManager _manager;
        private readonly IRequestResponseBus _bus;

        public ClientEnterWorldHandler(ILogger<ClientEnterWorldHandler> logger, IServerManager manager, IMessageBus bus)
        {
            _logger = logger;
            _manager = manager;
            _bus = bus;
        }

        public override async void Handler(LoginSession session, LoginReadable message)
        {
            if (message is ClientEnterWorld packet)
            {
                var server = _manager.GetServer(packet.GameServerId);
                if (!server.Active)
                {
                    session.SendMessage(new ServerEnterWorldDenied {Reason = 0});
                    return;
                }

                try
                {
                    var res = await _bus.Send(new PlayerEnterRequest
                    {
                        AccountId = session.Account.Id, Token = (uint)session.Id.GetHashCode()
                    });

                    if (res.Result == 0)
                    {
                        session.SendMessage(new ServerWorldCookie
                        {
                            ConnectionId = (uint)session.Id.GetHashCode(), GameServer = server
                        });
                    }
                    else
                    {
                        session.SendMessage(new ServerEnterWorldDenied {Reason = 0});
                    }
                }
                catch (Exception e)
                {
                    session.SendMessage(new ServerEnterWorldDenied {Reason = 0});

                    _logger.LogError(e, e.Message);
                }
            }
        }
    }
}
