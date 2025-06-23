namespace AAEmu.Login.Models.Database;

/// <summary>
/// Table containing SQL update script information
/// </summary>
public partial class Update
{
    public required string ScriptName { get; set; }

    public bool Installed { get; set; }

    public DateTime InstallDate { get; set; }

    public string LastError { get; set; } = string.Empty;
}
