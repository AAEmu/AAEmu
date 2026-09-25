namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// One faction combat-relation record sent to a zone authority.
/// A zero <see cref="RelationType"/> removes the pair; a non-zero value installs it with <see cref="Flags"/>.
/// </summary>
public readonly record struct CombatRelationEntry(
    uint Faction1,
    uint Faction2,
    uint RelationType,
    uint Flags);
