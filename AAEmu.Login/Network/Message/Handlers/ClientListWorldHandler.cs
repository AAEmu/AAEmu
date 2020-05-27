using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AAEmu.Commons.Messages;
using AAEmu.Commons.Network.Share.Character;
using AAEmu.Login.Login;
using AAEmu.Login.Network.Message.Packets.Outgoing;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace AAEmu.Login.Network.Message.Handlers
{
    [LoginMessageHandler(LoginMessageOpcode.ClientListWorld)]
    public class ClientListWorldHandler : LoginHandler
    {
        private ILogger<ClientListWorldHandler> _logger;
        private readonly IServerManager _manager;
        private readonly IRequestResponseBus _bus;

        public ClientListWorldHandler(ILogger<ClientListWorldHandler> logger, IServerManager manager, IMessageBus bus)
        {
            _logger = logger;
            _manager = manager;
            _bus = bus;
        }

        public override async void Handler(LoginSession session, LoginReadable message)
        {
            session.Characters = new List<CharacterInfo>();
            var servers = _manager.GameServers.ToArray();

            var req = new RequestInfoRequest {AccountId = session.Account.Id};
            var responseTasks = servers
                .Where(x => x.Active)
                .Select(async x => await _bus.Send(req));

            try
            {
                var res = await Task.WhenAll(responseTasks).ConfigureAwait(false);
                foreach (var response in res)
                {
                    if (response == null)
                        continue;

                    var chars = response.Characters
                        .Where(character => !session.Characters
                            .Exists(x =>
                                x.GameServerId == character.GameServerId &&
                                x.CharacterId == character.CharacterId
                            )
                        );

                    session.Characters.AddRange(chars);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
            }

            session.SendMessage(new ServerWorldList {GameServers = servers, Characters = session.Characters});
        }
    }
}
