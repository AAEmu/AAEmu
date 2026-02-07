using System.Net.Sockets;

var builder = DistributedApplication.CreateBuilder(args);

var mySql = builder
    .AddMySql("db")
    .WithImageTag("8.0")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

// Idempotent creation script for login database
var loginInitScriptPath = Path.Join(Path.GetDirectoryName(typeof(Program).Assembly.Location), "init_aaemu_login.sql");
var loginInitScript = File.ReadAllText(loginInitScriptPath);
var mySqlLoginDb = mySql
    .AddDatabase("aaemu-login", "aaemu_login")
    .WithCreationScript(loginInitScript);

// Idempotent creation script for game database
var gameInitScriptPath = Path.Join(Path.GetDirectoryName(typeof(Program).Assembly.Location), "init_aaemu_game.sql");
var gameInitScript = File.ReadAllText(gameInitScriptPath);
var mySqlGameDb = mySql
    .AddDatabase("aaemu-game", "aaemu_game")
    .WithCreationScript(gameInitScript);

var loginServer = builder.AddProject<Projects.AAEmu_Login>("login-server")
    .WithEndpoint(name: "login-public", port: 1237, targetPort: 1237, isProxied: false, protocol: ProtocolType.Tcp,
        isExternal: true)
    .WithEndpoint(name: "login-internal", port: 1234, targetPort: 1234, isProxied: false, protocol: ProtocolType.Tcp,
        isExternal: false)
    .WithEndpoint(name: "http-internal", scheme: "http", isExternal: false)
    .WithHttpHealthCheck("/health/ready", endpointName: "http-internal")
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
    .WithOtlpExporter();

var gameServer = builder.AddProject<Projects.AAEmu_Game>("game-server")
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
    .WithOtlpExporter();

builder.Build().Run();
