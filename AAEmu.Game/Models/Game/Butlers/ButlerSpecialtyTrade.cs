namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>Typed row from <c>butler_specialty_trades</c> joined to its specialty NPC destination.</summary>
public sealed record ButlerSpecialtyTradeDefinition(
    uint Id,
    uint NpcId,
    uint CraftId,
    uint DeliveryMinTime,
    uint DeliveryMaxTime,
    uint ConsumeProductionCost,
    uint ZoneGroupId);

/// <summary>Durable state for one farmhand specialty-trade job.</summary>
public sealed record ButlerSpecialtyTradeJob(
    long JobId,
    uint NpcId,
    /// <summary>Static butler_specialty_trades.id, as used by the client request and wire.</summary>
    uint SpecialtyType,
    ushort ToZoneGroupType,
    uint ProductItemId,
    long CreatedTime,
    uint DeliveryTime);

/// <summary>A specialty-trade job before MySQL assigns its durable database id.</summary>
public readonly record struct ButlerSpecialtyTradeJobCandidate(
    uint NpcId,
    /// <summary>Static butler_specialty_trades.id.</summary>
    uint SpecialtyType,
    ushort ToZoneGroupType,
    uint ProductItemId,
    long CreatedTime,
    uint DeliveryTime);
