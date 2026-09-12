namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>
/// Shared state body in <c>SCButlerInitInfo</c> and <c>SCButlerBound</c>. Field names follow the
/// 10.0.2.13 client serializer; <see cref="HouseTlId"/> is the house timeline id on the wire.
/// </summary>
public readonly record struct ButlerInfoWire(
    sbyte WorldId,
    string Name,
    ushort HouseTlId,
    uint LaborPower,
    ushort LpChargedAmount,
    ushort RemainProductionCost);
