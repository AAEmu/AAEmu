namespace AAEmu.BillServer.Cash;

public sealed class BillCatalogOptions
{
    public string? ClientCompactPath { get; set; }
    public string DefaultLanguage { get; set; } = "en_us";

    public CompactItemNameCatalog OpenNameCatalog() =>
        new(ClientCompactPath, DefaultLanguage);
}
