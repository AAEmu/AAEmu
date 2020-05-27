using System;
using System.Threading.Tasks;
using AAEmu.Commons.Messages;
using AAEmu.Login.Login;
using SlimMessageBus;

namespace AAEmu.Login.Bus
{
    public class PlayerReconnectRequestHandler : IRequestHandler<PlayerReconnectRequest, PlayerReconnectResponse>
    {
        private readonly IServerManager _serverManager;

        public PlayerReconnectRequestHandler(IServerManager serverManager)
        {
            _serverManager = serverManager;
        }

        public Task<PlayerReconnectResponse> OnHandle(PlayerReconnectRequest request, string name)
        {
            if (_serverManager.AddReconnectionToken((byte)request.GameServerId, request.AccountId, request.Token))
                return Task.FromResult(new PlayerReconnectResponse {Success = true, Token = request.Token});

            throw new Exception(); // TODO : Implement
        }
    }
}
