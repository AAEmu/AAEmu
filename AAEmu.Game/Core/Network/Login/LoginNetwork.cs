using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.L2G;
using AAEmu.Game.Models;

namespace AAEmu.Game.Core.Network.Login;

public class LoginNetwork : Singleton<LoginNetwork>
{
    private Client _client;
    private readonly LoginProtocolHandler _handler;
    private LoginConnection _connection;
    private volatile bool _running;

    /// <summary>
    /// True between <see cref="Start"/> and <see cref="Stop"/>. A disconnect while running triggers a
    /// reconnect; a disconnect after an intentional <see cref="Stop"/> (server shutdown) must not, since
    /// the DI <see cref="System.IServiceProvider"/> backing <see cref="AppConfiguration.Instance"/> is
    /// already disposed by the time the async disconnect callback fires.
    /// </summary>
    public bool IsRunning => _running;

    private LoginNetwork()
    {
        _handler = new LoginProtocolHandler();

        RegisterPacket(LGOffsets.LGRegisterGameServerPacket, typeof(LGRegisterGameServerPacket));
        RegisterPacket(LGOffsets.LGPlayerEnterPacket, typeof(LGPlayerEnterPacket));
        RegisterPacket(LGOffsets.LGPlayerReconnectPacket, typeof(LGPlayerReconnectPacket));
        RegisterPacket(LGOffsets.LGRequestInfoPacket, typeof(LGRequestInfoPacket));
    }

    public void Start()
    {
        var config = AppConfiguration.Instance.LoginNetwork;
        _client = new Client(Dns.GetHostAddresses(config.Host).First(), config.Port, _handler);
        _running = true;
        _client.ConnectAsync();
    }

    public void Stop()
    {
        _running = false;
        if (_client?.IsConnected ?? false)
            _client.DisconnectAsync();
    }

    public void SetConnection(LoginConnection con)
    {
        _connection = con;
    }

    public LoginConnection GetConnection()
    {
        return _connection;
    }

    private void RegisterPacket(uint type, Type classType)
    {
        _handler.RegisterPacket(type, classType);
    }
}
