using System;
using AAEmu.Commons.DI;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Network.Message;
using Microsoft.Extensions.Logging;

namespace AAEmu.Login.Network
{
    public interface ILoginProtocolHandler : IProtocolHandler, ISingletonService
    {
    }

    public class LoginProtocolHandler
        : ClientGameProtocolHandler<LoginMessageOpcode, LoginMessageAttribute, LoginMessageHandlerAttribute>,
            ILoginProtocolHandler
    {
        private ILogger<LoginProtocolHandler> _logger;

        public LoginProtocolHandler(IServiceProvider provider, ILogger<LoginProtocolHandler> logger)
            : base(provider, logger)
        {
            _logger = logger;
        }
    }
}
