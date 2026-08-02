namespace AAEmu.Game.Models.Game.Heirs;

/// <summary>
/// Native reset modes accepted by <c>CSResetHeirSkillPacket</c>. The 10.0.2.13 client handler
/// </summary>
public enum HeirSkillResetKind : uint
{
    All = 1,
    Ability = 2,
    Successor = 3
}
