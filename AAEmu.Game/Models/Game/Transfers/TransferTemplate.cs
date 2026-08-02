namespace AAEmu.Game.Models.Game.Transfers;

public class TransferTemplate
{
    public uint Id { get; set; }     // TemplateId -> owner_id
    public string Name { get; set; } // comment
    public uint ModelId { get; set; }
    public double WaitTime { get; set; }
    public bool Cyclic { get; set; }
    public float PathSmoothing { get; set; }

    /// <summary>
    /// Forward speed along the route, from <c>transfers.velocity</c>. This is the authoritative
    /// source for a transfer: most transfer models (carriage 654, airship 657, lift 2094) have no
    /// <c>vehicle_models</c> row at all, so nothing can be read from there.
    /// </summary>
    public float Velocity { get; set; }
    public List<TransferBindings> TransferBindings { get; } = []; // selection by owner_id
    public List<TransferPaths> TransferAllPaths { get; } = []; // selection by owner_id, здесь список всех участков дорог
    public List<TransferRoads> TransferRoads { get; } = []; // здесь список участков дороги для конкретной модели транспорта
    public List<TransferBindingDoodads> TransferBindingDoodads { get; } = []; // selection by owner_id
}
