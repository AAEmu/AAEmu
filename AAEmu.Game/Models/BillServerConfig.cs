namespace AAEmu.Game.Models;

/// <summary>World-side Bill Server peer configuration.</summary>
public sealed class BillServerConfig
{
    /// <summary>When true, World opens a TCP client to Bill on boot.</summary>
    public bool Enabled { get; set; }

    /// <summary>When true, ICS stays closed until Bill is connected (maintenance when Bill is down).</summary>
    public bool RequireConnection { get; set; } = true;

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 12345;
    public int ReconnectSeconds { get; set; } = 5;
    public int HeartbeatSeconds { get; set; } = 30;
    public int RequestTimeoutMs { get; set; } = 8000;
}
