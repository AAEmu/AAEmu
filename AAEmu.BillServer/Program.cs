using System.Net;
using System.Text.Json;
using AAEmu.BillServer.Admin;
using AAEmu.BillServer.Cash;
using AAEmu.BillServer.Network;
using NLog;

namespace AAEmu.BillServer;

public static class Program
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static async Task<int> Main(string[] args)
    {
        LogManager.Setup().LoadConfigurationFromFile("NLog.config");
        var cfg = LoadConfig();

        ICashStore cash;
        ICatalogStore catalog;
        if (cfg.UseMysql)
        {
            Log.Info("Using MySQL stores");
            cash = new MysqlCashStore(cfg.MysqlConnectionString);
            catalog = new MysqlCatalogStore(cfg.MysqlConnectionString);
        }
        else
        {
            Log.Info("Using in-memory stores (set UseMysql=true + run SQL/aaemu_bill.sql for persistence)");
            cash = new MemoryCashStore();
            catalog = new MemoryCatalogStore();
        }

        cash.Credit("SEED-10001", 10001, 0, 0, 1000, 0, "gm_command");
        cash.Credit("SEED-10001-B", 10001, 0, 0, 100, 5, "gm_command");

        var host = IPAddress.Parse(cfg.WorldListenHost is "0.0.0.0" or "*" ? "0.0.0.0" : cfg.WorldListenHost);
        if (cfg.WorldListenHost is "0.0.0.0" or "*")
            host = IPAddress.Any;

        var world = new BillWorldListener(host, cfg.WorldListenPort, cash, catalog);
        world.Start();

        var catalogOptions = new BillCatalogOptions
        {
            ClientCompactPath = cfg.ClientCompactPath,
            DefaultLanguage = cfg.DefaultLanguage
        };

        var catalogGate = new CatalogMutationGate();
        BillServerRuntime? runtime = null;
        Action requestShutdown = () => runtime?.RequestShutdown();

        var adminPrefix = $"http://{cfg.AdminListenHost}:{cfg.AdminListenPort}/";
        AdminHttpServer? admin = null;
        try
        {
            admin = new AdminHttpServer(
                adminPrefix,
                cash,
                catalog,
                cfg.IcsSyncConnectionString,
                catalogOptions,
                () => $"world :{cfg.WorldListenPort}, products={catalog.ListAll().Count}",
                catalogGate,
                requestShutdown);
            admin.Start();
        }
        catch (HttpListenerException ex)
        {
            Log.Error(ex, "Admin HTTP failed to bind {0} — try elevating or UrlAcl", adminPrefix);
        }

        AdminHttpServer? web = null;
        if (cfg.WebListenPort > 0)
        {
            try
            {
                web = new AdminHttpServer(
                    $"http://{cfg.WebListenHost}:{cfg.WebListenPort}/",
                    cash,
                    catalog,
                    cfg.IcsSyncConnectionString,
                    catalogOptions,
                    () => "web",
                    catalogGate,
                    requestShutdown);
                web.Start();
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Web listener :{0} not started (admin still on {1})", cfg.WebListenPort, cfg.AdminListenPort);
            }
        }

        runtime = new BillServerRuntime(world, admin, web, catalogGate);

        Log.Info("X2 Bill Server ready. Protocol :{0}  Admin {1}", cfg.WorldListenPort, adminPrefix);
        Log.Info("Verify: python re/research/bill-server-10.0.2.13/test_client.py  (or Scripts/test_bill_client.py)");

        var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            runtime.RequestShutdown();
        };

        _ = runtime.ShutdownRequested.ContinueWith(_ => exit.TrySetResult(), TaskScheduler.Default);
        await exit.Task;

        LogManager.Shutdown();
        return 0;
    }

    private static BillConfig LoadConfig()
    {
        const string path = "Config.json";
        if (!File.Exists(path))
            return new BillConfig();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BillConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new BillConfig();
    }

    private sealed class BillConfig
    {
        public string WorldListenHost { get; set; } = "0.0.0.0";
        public int WorldListenPort { get; set; } = 12345;
        public string WebListenHost { get; set; } = "0.0.0.0";
        public int WebListenPort { get; set; } = 8080;
        public string AdminListenHost { get; set; } = "127.0.0.1";
        public int AdminListenPort { get; set; } = 18080;
        public bool UseMysql { get; set; }
        public string MysqlConnectionString { get; set; } = "";
        public string IcsSyncConnectionString { get; set; } = "";
        public string? ClientCompactPath { get; set; }
        public string DefaultLanguage { get; set; } = "en_us";
    }
}
