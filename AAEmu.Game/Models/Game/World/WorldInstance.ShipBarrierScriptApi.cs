using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Public surface for GM commands and Roslyn-compiled scripts: they reference AAEmu.Game as metadata and cannot use
/// <c>internal</c> members (<see cref="ShipStaticBarriersMutationLock"/>, <see cref="ShipStaticBarrierBaiIngestor"/>, etc.).
/// </summary>
public partial class WorldInstance
{
    /// <summary>Barrier count while holding the mutation lock (e.g. script pre/post ingest deltas).</summary>
    public int GetShipStaticBarrierCountLocked()
    {
        lock (ShipStaticBarriersMutationLock)
            return ShipStaticBarriers?.Barriers.Count ?? 0;
    }

    /// <summary>Clears BAI-ingested ship barriers and per-world ingest bookkeeping (GM reset).</summary>
    public void ClearShipStaticBarriersAndBaiIngest(out int clearedBarrierCount, out int clearedIngestedCellCount)
    {
        lock (ShipStaticBarriersMutationLock)
        {
            clearedBarrierCount = ShipStaticBarriers?.Barriers.Count ?? 0;
            clearedIngestedCellCount = ShipBarrierBaiIngestedCells.Count;
            if (ShipStaticBarriers is null)
                return;
            ShipStaticBarriers.Barriers.Clear();
            ShipStaticBarriers.SpatialGridBuiltForBarrierCount = -1;
            ShipStaticBarriers.SpatialGrid = null;
            ShipBarrierBaiIngestedCells.Clear();
            ShipBarrierBaiNameSerial = 0;
        }
    }

    /// <summary>Snapshot barrier and ingested-cell counts for status output.</summary>
    public void GetShipStaticBarrierDebugCounts(out int barrierCount, out int ingestedCellCount)
    {
        lock (ShipStaticBarriersMutationLock)
        {
            barrierCount = ShipStaticBarriers?.Barriers.Count ?? 0;
            ingestedCellCount = ShipBarrierBaiIngestedCells.Count;
        }
    }

    /// <summary>Lazily ingests <c>areasmission</c> polygons for one world cell into <see cref="ShipStaticBarriers"/>.</summary>
    public void EnsureShipStaticBarrierBaiCell(int cellX, int cellY) =>
        ShipStaticBarrierBaiIngestor.EnsureCell(this, cellX, cellY);
}
