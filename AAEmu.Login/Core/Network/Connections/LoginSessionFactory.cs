using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Models;
using Microsoft.Extensions.Options;

namespace AAEmu.Login.Core.Network.Connections;

public class LoginSessionFactory(
    IGameController gameController,
    TimeProvider timeProvider,
    IOptions<AppConfiguration> appConfig,
    ILoggerFactory loggerFactory) : ILoginSessionFactory
{
    public ILoginSession Create(ILoginConnection connection)
    {
        return new LoginSession(
            connection,
            gameController,
            timeProvider,
            appConfig,
            loggerFactory.CreateLogger<LoginSession>());
    }
}
