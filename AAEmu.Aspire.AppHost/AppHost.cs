using System.Net.Sockets;
using Aspire.Hosting.Docker;
using Aspire.Hosting.Docker.Resources.ComposeNodes;

const string ComposeProjectName = "aaemu";
const string ComposeNetworkName = "aaemu-net";

var builder = DistributedApplication.CreateBuilder(args);

var compose = builder.AddDockerComposeEnvironment("aaemu-compose");
var dbPassword = builder.AddParameter(
    "aaemu-db-password",
    "password",
    secret: false,
    publishValueAsDefault: true);

compose.WithProperties(environment => environment.DefaultNetworkName = ComposeNetworkName);

compose.ConfigureComposeFile(file =>
{
    file.Name = ComposeProjectName;
    file.AddNetwork(new Network
    {
        Name = ComposeNetworkName,
        Driver = "bridge",
    });
});

compose.ConfigureEnvFile(environment =>
{
    SetDefaultEnvVar(environment, "COMPOSE_PROJECT_NAME", ComposeProjectName, "Docker Compose project name.");
    SetDefaultEnvVar(environment, "AAEMU_NETWORK_NAME", ComposeNetworkName, "Docker network used by AAEmu services.");
    SetDefaultEnvVar(environment, "AAEMU_DB_HOST_PORT", "3306", "Host port for MySQL.");
    SetDefaultEnvVar(environment, "AAEMU_ADMINER_HOST_PORT", "8080", "Host port for Adminer.");
    SetDefaultEnvVar(environment, "AAEMU_LOGIN_PUBLIC_PORT", "1237", "Host port for login service public endpoint.");
    SetDefaultEnvVar(environment, "AAEMU_GAME_PUBLIC_PORT", "1239", "Host port for game service public endpoint.");
    SetDefaultEnvVar(environment, "AAEMU_GAME_STREAM_PUBLIC_PORT", "1250", "Host port for game stream endpoint.");
    SetDefaultEnvVar(environment, "AAEMU_DASHBOARD_HOST_PORT", "18888", "Host port for the Aspire dashboard.");
});

compose.WithDashboard(dashboard =>
{
    dashboard
        .WithHostPort(18888)
        .WithComputeEnvironment(compose)
        .PublishAsDockerComposeService((_, service) =>
        {
            service.Name = "aaemu-dashboard";
            service.ContainerName = "aaemu-dashboard";
            service.Networks = [ComposeNetworkName];
            service.Ports = [BuildPortMapping("AAEMU_DASHBOARD_HOST_PORT", 18888)];
        });
});

var mySql = builder
    .AddMySql("aaemu-db", dbPassword)
    .WithImageTag("8.0")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

_ = builder
    .AddContainer("aaemu-adminer", "docker.io/library/adminer", "latest")
    .WithEnvironment("ADMINER_DEFAULT_DB_DRIVER", "mysql")
    .WithEnvironment("ADMINER_DEFAULT_DB_HOST", mySql.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("ADMINER_DESIGN", "nette")
    .WithEndpoint(
        name: "http",
        port: 8080,
        targetPort: 8080,
        scheme: "http",
        isExternal: true,
        isProxied: false)
    .WithReference(mySql)
    .WaitFor(mySql)
    .WithComputeEnvironment(compose)
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Name = "aaemu-adminer";
        service.ContainerName = "aaemu-adminer";
        service.Networks = [ComposeNetworkName];
        service.Ports = [BuildPortMapping("AAEMU_ADMINER_HOST_PORT", 8080)];
    });

mySql
    .WithComputeEnvironment(compose)
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Name = "aaemu-db";
        service.ContainerName = "aaemu-db";
        service.Networks = [ComposeNetworkName];
        service.Ports = [BuildPortMapping("AAEMU_DB_HOST_PORT", 3306)];
    });

// Idempotent creation script for login database
var loginInitScriptPath = Path.Join(Path.GetDirectoryName(typeof(Program).Assembly.Location), "init_aaemu_login.sql");
var loginInitScript = File.ReadAllText(loginInitScriptPath);
var mySqlLoginDb = mySql
    .AddDatabase("aaemu-login-db", "aaemu_login")
    .WithCreationScript(loginInitScript);

// Idempotent creation script for game database
var gameInitScriptPath = Path.Join(Path.GetDirectoryName(typeof(Program).Assembly.Location), "init_aaemu_game.sql");
var gameInitScript = File.ReadAllText(gameInitScriptPath);
var mySqlGameDb = mySql
    .AddDatabase("aaemu-game-db", "aaemu_game")
    .WithCreationScript(gameInitScript);

var loginServer = builder.AddProject<Projects.AAEmu_Login>("aaemu-login")
    .WithEndpoint(name: "login-public", port: 1237, targetPort: 1237, isProxied: false, protocol: ProtocolType.Tcp,
        isExternal: true)
    .WithEndpoint(name: "login-internal", port: 1234, targetPort: 1234, isProxied: false, protocol: ProtocolType.Tcp,
        isExternal: false)
    .WithEnvironment("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1")
    .WithEnvironment("Connections__MySQLProvider__Database", mySqlLoginDb.Resource.DatabaseName)
    .WithEnvironment("Connections__MySQLProvider__Host", mySql.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("Connections__MySQLProvider__Port", mySql.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("Connections__MySQLProvider__User", "root")
    .WithEnvironment("Connections__MySQLProvider__Password", mySql.Resource.PasswordParameter)
    .WithEnvironment("GameServers__0__ID", "1")
    .WithEnvironment("GameServers__0__Name", "AAEmu.Game")
    .WithEnvironment("GameServers__0__Host", "127.0.0.1")
    .WithEnvironment("GameServers__0__Port", "1239")
    .WithReference(mySqlLoginDb)
    .WaitFor(mySqlLoginDb)
    .WithOtlpExporter()
    .WithComputeEnvironment(compose)
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Name = "aaemu-login";
        service.ContainerName = "aaemu-login";
        service.Image = "aaemu-login:latest";
        service.Networks = [ComposeNetworkName];
        service.Ports = [BuildPortMapping("AAEMU_LOGIN_PUBLIC_PORT", 1237)];
    });

var gameServer = builder.AddProject<Projects.AAEmu_Game>("aaemu-game")
    .WithEndpoint(name: "game-public", port: 1239, targetPort: 1239, isProxied: false, protocol: ProtocolType.Tcp,
        isExternal: true)
    .WithEndpoint(name: "game-stream-public", port: 1250, targetPort: 1250, isProxied: false,
        protocol: ProtocolType.Tcp, isExternal: true)
    .WithEnvironment("Connections__MySQLProvider__Database", mySqlGameDb.Resource.DatabaseName)
    .WithEnvironment("Connections__MySQLProvider__Host", mySql.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("Connections__MySQLProvider__Port", mySql.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("Connections__MySQLProvider__User", "root")
    .WithEnvironment("Connections__MySQLProvider__Password", mySql.Resource.PasswordParameter)
    .WithEnvironment("LoginNetwork__Host", loginServer.GetEndpoint("login-internal").Property(EndpointProperty.Host))
    .WithEnvironment("LoginNetwork__Port", loginServer.GetEndpoint("login-internal").Property(EndpointProperty.Port))
    .WithReference(mySqlGameDb)
    .WithReference(loginServer)
    .WaitFor(mySqlGameDb)
    .WaitFor(loginServer)
    .WithOtlpExporter()
    .WithComputeEnvironment(compose)
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Name = "aaemu-game";
        service.ContainerName = "aaemu-game";
        service.Image = "aaemu-game:latest";
        service.Networks = [ComposeNetworkName];
        service.Ports =
        [
            BuildPortMapping("AAEMU_GAME_PUBLIC_PORT", 1239),
            BuildPortMapping("AAEMU_GAME_STREAM_PUBLIC_PORT", 1250),
        ];
    });

builder.Build().Run();

static string BuildPortMapping(string hostPortVariableName, int targetPort) =>
    $"${{{hostPortVariableName}:-{targetPort}}}:{targetPort}";

static void SetDefaultEnvVar(
    IDictionary<string, CapturedEnvironmentVariable> envFile,
    string name,
    string defaultValue,
    string description)
{
    envFile[name] = new CapturedEnvironmentVariable
    {
        Name = name,
        DefaultValue = defaultValue,
        Description = description,
    };
}
