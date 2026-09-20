namespace AAEmu.Game.Models.StaticValues;

/// <summary>
/// Discriminator on <c>CSInstantTime</c>. The client reuses that packet for several "do not receive"
/// checkboxes as well as instant-game clock writes. Only <see cref="MobilizationOrderNotRecv"/> is
/// handled here; other kinds stay no-ops until their own slice.
/// </summary>
public enum InstantTimeKind : uint
{
    MobilizationOrderNotRecv = 1,
    ExpeditionSummonNotRecv = 3,
}
