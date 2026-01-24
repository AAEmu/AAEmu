using AAEmu.Login.Core.Network.Connections;
using Microsoft.AspNetCore.Connections;

namespace AAEmu.Login.Core.Network.Login;

public interface ILoginConnectionFactory
{
    LoginConnection Create(ConnectionContext connectionContext);
}
