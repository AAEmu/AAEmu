namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// One combat-relation record. Character relations use <see cref="Faction1"/> as the single key.
/// Faction relations use both ids. <see cref="Code"/> 0 removes the record; <see cref="Reason"/> travels with it.
/// </summary>
public readonly record struct CombatRelationEntry(uint Faction1, uint Faction2, byte Code, byte Reason)
{
    public uint RelationType => Code;

    public uint Flags => Reason;
}
