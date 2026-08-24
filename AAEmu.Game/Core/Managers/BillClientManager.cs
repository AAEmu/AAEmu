using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Bill;
using AAEmu.Game.Models;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Game.Core.Managers;

public interface IBillClientManager
{
    bool IsConfigured { get; }
    bool RequireConnection { get; }
    bool IsConnected { get; }
    void Start();
    void Stop();
    Task<(int Cash, int Bonus)?> TryGetCashAsync(uint accountId, string accountName, uint charId);
    Task<BillBuyResult?> TryBuyAsync(BillBuyRequest request);
}

/// <summary>Maintains the World→Bill TCP session and drives ICS maintenance gating.</summary>
public sealed class BillClientManager : Singleton<BillClientManager>, IBillClientManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly BillServerConfig _config;
    private readonly byte _worldId;
    private BillClient? _client;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private volatile bool _connected;
    private CancellationTokenSource? _maintenanceCts;

    public BillClientManager(IOptions<AppConfiguration> options)
    {
        var app = options.Value;
        _config = app.BillServer ?? new BillServerConfig();
        _worldId = app.Id;
        ApplyEnvironmentOverrides();
    }

    public bool IsConfigured => _config.Enabled;
    public bool RequireConnection => _config.Enabled && _config.RequireConnection;
    public bool IsConnected => _connected && _client?.IsConnected == true;

    public void Start()
    {
        if (!_config.Enabled)
        {
            Logger.Info("Bill client disabled (BillServer.Enabled=false)");
            return;
        }

        if (_loopTask is not null)
            return;

        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => ConnectLoopAsync(_loopCts.Token));
        Logger.Info("Bill client starting host={0} port={1} require={2}", _config.Host, _config.Port, _config.RequireConnection);
    }

    public void Stop()
    {
        _loopCts?.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore shutdown race
        }

        _client?.Dispose();
        _client = null;
        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
        SetConnected(false);
    }

    public async Task<(int Cash, int Bonus)?> TryGetCashAsync(uint accountId, string accountName, uint charId)
    {
        if (!IsConnected || _client is null)
            return null;

        try
        {
            return await _client.GetCashAsync(accountId, accountName, (int)charId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Bill GetCash failed account={0}", accountId);
            return null;
        }
    }

    public async Task<BillBuyResult?> TryBuyAsync(BillBuyRequest request)
    {
        if (!IsConnected || _client is null)
            return null;

        try
        {
            return await _client.BuyAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Bill Buy failed account={0}", request.AccountId);
            return null;
        }
    }

    private async Task ConnectLoopAsync(CancellationToken cancellationToken)
    {
        var reconnect = TimeSpan.FromSeconds(Math.Max(1, _config.ReconnectSeconds));
        var heartbeat = TimeSpan.FromSeconds(Math.Max(5, _config.HeartbeatSeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            _client?.Dispose();
            _client = new BillClient(_config, _worldId);

            var joined = await _client.ConnectAndJoinAsync(cancellationToken);
            if (joined)
            {
                SetConnected(true);
                if (await RunSessionAsync(_client, heartbeat, cancellationToken))
                    continue;
            }

            SetConnected(false);
            try
            {
                await Task.Delay(reconnect, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> RunSessionAsync(BillClient client, TimeSpan heartbeatInterval, CancellationToken cancellationToken)
    {
        var nextHeartbeat = DateTime.UtcNow + heartbeatInterval;
        while (!cancellationToken.IsCancellationRequested && client.IsConnected)
        {
            if (DateTime.UtcNow >= nextHeartbeat)
            {
                try
                {
                    await client.SendHeartbeatAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Bill heartbeat failed");
                    return false;
                }

                nextHeartbeat = DateTime.UtcNow + heartbeatInterval;
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private void SetConnected(bool connected)
    {
        if (_connected == connected)
            return;

        _connected = connected;
        if (connected)
        {
            _maintenanceCts?.Cancel();
            _maintenanceCts = null;
            Logger.Info("Bill session up — ICS may open when catalog is loaded");
            if (CashShopManager.Instance.MenuItems.Count > 0)
                CashShopManager.Instance.EnabledShop();
        }
        else
        {
            Logger.Warn("Bill session down — scheduling ICS maintenance check");
            if (!RequireConnection)
                return;

            _maintenanceCts?.Cancel();
            _maintenanceCts = new CancellationTokenSource();
            var token = _maintenanceCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(3, _config.ReconnectSeconds)), token);
                    if (token.IsCancellationRequested || _connected)
                        return;
                    Logger.Warn("Bill still down — closing ICS until reconnect");
                    CashShopManager.Instance.DisableShop();
                }
                catch (OperationCanceledException)
                {
                    // reconnect won the race
                }
            }, token);
        }
    }

    private void ApplyEnvironmentOverrides()
    {
        var host = Environment.GetEnvironmentVariable("AAEMU_BILL_HOST");
        if (!string.IsNullOrWhiteSpace(host))
            _config.Host = host;

        if (int.TryParse(Environment.GetEnvironmentVariable("AAEMU_BILL_PORT"), out var port))
            _config.Port = port;

        if (TryParseBoolEnv("AAEMU_BILL_ENABLED", out var enabled))
            _config.Enabled = enabled;

        if (TryParseBoolEnv("AAEMU_BILL_REQUIRE", out var require))
            _config.RequireConnection = require;
    }

    private static bool TryParseBoolEnv(string name, out bool value)
    {
        value = false;
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        if (raw is "1" or "true" or "TRUE" or "yes" or "YES")
        {
            value = true;
            return true;
        }

        if (raw is "0" or "false" or "FALSE" or "no" or "NO")
        {
            value = false;
            return true;
        }

        return false;
    }
}
