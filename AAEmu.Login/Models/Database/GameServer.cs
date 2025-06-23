namespace AAEmu.Login.Models.Database;

/// <summary>
/// Server list
/// </summary>
public partial class GameServer
{
    public GameServerId Id { get; set; }

    public required string Name { get; set; }

    public required string Host { get; set; }

    public ushort Port { get; set; }

    public bool Hidden { get; set; }
}
