using AAEmu.Commons.IO;
using AAEmu.Web.Data;
using AAEmu.Web.Models;
using AAEmu.Web.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Mirrors the configuration layering used by AAEmu.Login: Config.json is committed with placeholder
// values, Config.Local.json holds the real credentials and is gitignored.
builder.Configuration
    .AddJsonFile(Path.Combine(FileManager.AppPath, "Config.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(FileManager.AppPath, "Config.Local.json"), optional: true, reloadOnChange: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.AddOptionsWithValidateOnStart<DbConnectionsConfig>()
    .BindConfiguration(DbConnectionsConfig.ConfigurationSectionName)
    .ValidateDataAnnotations();

builder.Services.AddOptionsWithValidateOnStart<ClientLauncherOptions>()
    .BindConfiguration(ClientLauncherOptions.ConfigurationSectionName)
    .ValidateDataAnnotations();

builder.Services.AddSingleton<IMySqlConnectionFactory, MySqlConnectionFactory>();
builder.Services.AddSingleton<IAccessLevelCatalog, AccessLevelCatalog>();
builder.Services.AddSingleton<IClientLauncher, ClientLauncher>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IGameRepository, GameRepository>();

builder.Services.AddHealthChecks()
    .AddCheck<LoginDatabaseHealthCheck>("login-database")
    .AddCheck<GameDatabaseHealthCheck>("game-database");

builder.Services.AddRazorPages();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapHealthChecks("/health/ready");

app.Run();

/// <summary>
/// Verifies that the configured login database is reachable.
/// </summary>
internal sealed class LoginDatabaseHealthCheck(IMySqlConnectionFactory connectionFactory) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        DatabaseHealthCheck.PingAsync(connectionFactory.CreateLoginConnectionAsync, "login", cancellationToken);
}

/// <summary>
/// Verifies that the configured game database is reachable.
/// </summary>
internal sealed class GameDatabaseHealthCheck(IMySqlConnectionFactory connectionFactory) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        DatabaseHealthCheck.PingAsync(connectionFactory.CreateGameConnectionAsync, "game", cancellationToken);
}

internal static class DatabaseHealthCheck
{
    public static async Task<HealthCheckResult> PingAsync(
        Func<CancellationToken, Task<MySql.Data.MySqlClient.MySqlConnection>> connect,
        string name, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await connect(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy($"Could not reach the {name} database.", e);
        }
    }
}
