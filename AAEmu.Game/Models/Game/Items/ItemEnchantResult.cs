namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Result code shared by regrade, tempering and the tempering broadcast. The client exports these to
/// Lua as <c>IGER_*</c>; the values below were read out of the 10.0.2.13 client's Lua constant
/// registration (the client serializer-0x1aa300), where each name is bound to a float literal.
/// </summary>
/// <remarks>
/// Note the gap: 6 is registered by the client but carries no exported name.
/// This is NOT the 1.2 layout - 1.2 had no <see cref="Disable"/> and everything from Fail up sat one
/// value lower.
/// </remarks>
public enum ItemGradeEnchantResult : byte
{
    Break = 0,
    Downgrade = 1,
    Disable = 2,
    Fail = 3,
    Success = 4,
    GreatSuccess = 5,
    RestoreDisable = 7
}

/// <summary>
/// Result of an awakening attempt (<c>ICMR_*</c> in the client's Lua constants).
/// </summary>
public enum ItemChangeMappingResult : byte
{
    Success = 0,
    Fail = 1,
    FailDisableEnchant = 2
}

/// <summary>
/// How a single random attribute survives an awakening, as shown in the awaken tab's preview
/// (<c>IAAIS_*</c> in the client's Lua constants).
/// </summary>
public enum ItemAwakenAttrInheritState : byte
{
    Inherit = 0,
    Random = 1,
    Delete = 2
}
