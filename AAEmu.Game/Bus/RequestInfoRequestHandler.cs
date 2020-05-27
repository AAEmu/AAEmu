using System.Collections.Generic;
using System.Threading.Tasks;
using AAEmu.Commons.Messages;
using AAEmu.Commons.Network.Share.Character;
using SlimMessageBus;

namespace AAEmu.Game.Bus
{
    public class RequestInfoRequestHandler : IRequestHandler<RequestInfoRequest, RequestInfoResponse>
    {
        private readonly IAccountManager _accountManager;

        public RequestInfoRequestHandler(IAccountManager accountManager)
        {
            _accountManager = accountManager;
        }

        public Task<RequestInfoResponse> OnHandle(RequestInfoRequest request, string name)
        {
            if (!_accountManager.Contains(request.AccountId))
            {
                return Task.FromResult(new RequestInfoResponse {Characters = new List<CharacterInfo>()});
            }

            var characters = _accountManager.GetCharacterInfosFromAccount(request.AccountId);
            return Task.FromResult(new RequestInfoResponse {Characters = characters});
        }
    }
}
