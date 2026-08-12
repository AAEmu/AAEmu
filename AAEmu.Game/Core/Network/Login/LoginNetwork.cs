using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.L2G;
using AAEmu.Game.Models;
using NLog;

namespace AAEmu.Game.Core.Network.Login;

public class LoginNetwork : Singleton<LoginNetwork>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private Client _client;
    private readonly LoginProtocolHandler _handler;
    private LoginConnection _connection;
    private volatile bool _running;
    private int _reconnectEpoch;

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
        BeginConnect(expectedEpoch: null);
    }

    public void Stop()
    {
        _running = false;
        Interlocked.Increment(ref _reconnectEpoch);
        var client = _client;
        _client = null;
        SafeDisconnect(client);
    }

    /// <summary>
    /// Reconnect after a short delay. Cancelled if <see cref="Stop"/> runs during the wait
    /// (shutdown) or if another reconnect is already scheduled.
    /// </summary>
    public void RequestReconnect()
    {
        if (!_running)
            return;

        Stop();
        var epoch = Volatile.Read(ref _reconnectEpoch);
        _ = ReconnectAfterAsync(epoch);
    }

    private async Task ReconnectAfterAsync(int epoch)
    {
        while (Volatile.Read(ref _reconnectEpoch) == epoch)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                if (Volatile.Read(ref _reconnectEpoch) != epoch)
                    return;
                BeginConnect(epoch);
                if (Volatile.Read(ref _reconnectEpoch) != epoch)
                {
                    _running = false;
                    var client = _client;
                    _client = null;
                    SafeDisconnect(client);
                    return;
                }

                return;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Login reconnect after disconnect failed");
                _running = false;
            }
        }
    }

    /// <summary>
    /// DNS + connect. When <paramref name="expectedEpoch"/> is set, abort if <see cref="Stop"/> ran
    /// during the wait (including while DNS is in flight or the socket is still connecting).
    /// </summary>
    private void BeginConnect(int? expectedEpoch)
    {
        if (EpochMismatch(expectedEpoch))
            return;

        var config = AppConfiguration.Instance.LoginNetwork;
        var address = Dns.GetHostAddresses(config.Host).First();
        if (EpochMismatch(expectedEpoch))
            return;

        var client = new Client(address, config.Port, _handler);
        if (EpochMismatch(expectedEpoch))
        {
            SafeDisconnect(client);
            return;
        }

        var previous = _client;
        _client = client;
        _running = true;
        SafeDisconnect(previous);

        if (EpochMismatch(expectedEpoch))
        {
            AbandonConnect(client);
            return;
        }

        client.ConnectAsync();

        if (EpochMismatch(expectedEpoch))
            AbandonConnect(client);
    }

    private void AbandonConnect(Client client)
    {
        _running = false;
        if (ReferenceEquals(_client, client))
            _client = null;
        SafeDisconnect(client);
    }

    private bool EpochMismatch(int? expectedEpoch) =>
        expectedEpoch is int epoch && Volatile.Read(ref _reconnectEpoch) != epoch;

    private static void SafeDisconnect(Client client)
    {
        if (client == null)
            return;
        try
        {
            client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Login client disconnect failed");
        }
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
