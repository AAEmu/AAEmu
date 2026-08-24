using AAEmu.BillServer.Admin;
using AAEmu.BillServer.Cash;
using AAEmu.BillServer.Network;
using NLog;

namespace AAEmu.BillServer;

public sealed class BillServerRuntime
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly BillWorldListener _world;
    private readonly AdminHttpServer? _admin;
    private readonly AdminHttpServer? _web;
    private readonly CatalogMutationGate _catalogGate;
    private readonly TaskCompletionSource _shutdownRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _shutdownStarted;

    public BillServerRuntime(
        BillWorldListener world,
        AdminHttpServer? admin,
        AdminHttpServer? web,
        CatalogMutationGate catalogGate)
    {
        _world = world;
        _admin = admin;
        _web = web;
        _catalogGate = catalogGate;
    }

    public CatalogMutationGate CatalogGate => _catalogGate;
    public Task ShutdownRequested => _shutdownRequested.Task;

    public void RequestShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            return;

        _ = Task.Run(ShutdownCoreAsync);
    }

    private async Task ShutdownCoreAsync()
    {
        Log.Info("BillServer graceful shutdown requested");
        _catalogGate.BeginShutdown();

        try
        {
            await _catalogGate.WaitForIdleAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "waiting for catalog mutations during shutdown");
        }

        try { _web?.Stop(); } catch (Exception ex) { Log.Debug(ex, "web listener stop"); }
        try { _admin?.Stop(); } catch (Exception ex) { Log.Debug(ex, "admin listener stop"); }
        try { _world.Stop(); } catch (Exception ex) { Log.Debug(ex, "world listener stop"); }

        _shutdownRequested.TrySetResult();
        Log.Info("BillServer stopped");
    }
}
