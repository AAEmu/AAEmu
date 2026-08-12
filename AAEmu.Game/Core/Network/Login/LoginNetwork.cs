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
    /// <summary>True after <see cref="Start"/> until intentional <see cref="Stop"/> (process shutdown).</summary>
    private volatile bool _wantLink;
    private int _reconnectEpoch;

    /// <summary>
    /// True while the game server intends to stay linked to Login. Disconnect while wanted triggers
    /// reconnect; disconnect after intentional <see cref="Stop"/> must not, since the DI
    /// <see cref="System.IServiceProvider"/> backing <see cref="AppConfiguration.Instance"/> may
    /// already be disposed when the async disconnect callback fires.
    /// </summary>
    public bool IsRunning => _wantLink;

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
        _wantLink = true;
        BeginConnect(expectedEpoch: null);
    }

    public void Stop()
    {
        _wantLink = false;
        Interlocked.Increment(ref _reconnectEpoch);
        var client = _client;
        _client = null;
        _connection = null;
        SafeDisconnect(client);
    }

    /// <summary>
    /// Tear down the current socket and reconnect after a short delay. Cancelled if <see cref="Stop"/>
    /// runs (shutdown) or another reconnect is already scheduled.
    /// </summary>
    public void RequestReconnect()
    {
        if (!_wantLink)
            return;

        Interlocked.Increment(ref _reconnectEpoch);
        var epoch = Volatile.Read(ref _reconnectEpoch);
        var client = _client;
        _client = null;
        _connection = null;
        SafeDisconnect(client);
        _ = ReconnectAfterAsync(epoch);
    }

    private async Task ReconnectAfterAsync(int epoch)
    {
        while (_wantLink && Volatile.Read(ref _reconnectEpoch) == epoch)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                if (!_wantLink || Volatile.Read(ref _reconnectEpoch) != epoch)
                    return;

                BeginConnect(epoch);
                if (!_wantLink || Volatile.Read(ref _reconnectEpoch) != epoch)
                {
                    TearDownSocket();
                    return;
                }

                // ConnectAsync is fire-and-forget; wait for OnConnect or time out and retry.
                for (var i = 0; i < 20; i++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
                    if (!_wantLink || Volatile.Read(ref _reconnectEpoch) != epoch)
                        return;
                    if (_connection != null && (_client?.IsConnected ?? false))
                        return;
                }

                Logger.Warn("Login reconnect timed out waiting for link; retrying");
                TearDownSocket();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Login reconnect after disconnect failed");
                TearDownSocket();
            }
        }
    }

    /// <summary>
    /// DNS + connect. When <paramref name="expectedEpoch"/> is set, abort if <see cref="Stop"/> or a
    /// newer reconnect ran during the wait (including while DNS is in flight).
    /// </summary>
    private void BeginConnect(int? expectedEpoch)
    {
        if (ShouldAbort(expectedEpoch))
            return;

        var config = AppConfiguration.Instance.LoginNetwork;
        var address = Dns.GetHostAddresses(config.Host).First();
        if (ShouldAbort(expectedEpoch))
            return;

        var client = new Client(address, config.Port, _handler);
        if (ShouldAbort(expectedEpoch))
        {
            SafeDisconnect(client);
            return;
        }

        var previous = _client;
        _client = client;
        SafeDisconnect(previous);

        if (ShouldAbort(expectedEpoch))
        {
            AbandonConnect(client);
            return;
        }

        client.ConnectAsync();

        if (ShouldAbort(expectedEpoch))
            AbandonConnect(client);
    }

    private void AbandonConnect(Client client)
    {
        if (ReferenceEquals(_client, client))
            _client = null;
        SafeDisconnect(client);
    }

    private void TearDownSocket()
    {
        var client = _client;
        _client = null;
        _connection = null;
        SafeDisconnect(client);
    }

    private bool ShouldAbort(int? expectedEpoch) =>
        !_wantLink || (expectedEpoch is int epoch && Volatile.Read(ref _reconnectEpoch) != epoch);

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
