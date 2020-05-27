using System.Threading.Tasks;
using AAEmu.Commons.Messages;
using SlimMessageBus;

namespace AAEmu.Game.Bus
{
    public class PlayerEnterRequestHandler : IRequestHandler<PlayerEnterRequest, PlayerEnterResponse>
    {
        private readonly IGameManager _gameManager;
        private readonly IAccountManager _accountManager;

        public PlayerEnterRequestHandler(IGameManager gameManager, IAccountManager accountManager)
        {
            _gameManager = gameManager;
            _accountManager = accountManager;
        }

        public async Task<PlayerEnterResponse> OnHandle(PlayerEnterRequest request, string name)
        {
            // TODO: enum result and actual logic for reasons you would be denied
            // Result: 1 = denied, 0 = allowed
            if (request.AccountId > 0)
            {
                if (_gameManager.AddAccount(request.AccountId, request.Token))
                {
                    _accountManager.Add(request.AccountId);
                    return new PlayerEnterResponse {Result = 0};
                }
            }

            return new PlayerEnterResponse { Result = 1};
        }
    }
}
