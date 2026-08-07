namespace AAEmu.Web.Models;

/// <summary>
/// Mirrors <c>AAEmu.Game.Models.Game.Char.Race</c>. Duplicated rather than referenced because
/// AAEmu.Game is an executable with a large dependency graph that the web front-end does not need.
/// Keep in sync with <c>AAEmu.Game/Models/Game/Char/Race.cs</c>.
/// </summary>
public enum Race : byte
{
    None = 0,
    Nuian = 1,
    Fairy = 2,
    Dwarf = 3,
    Elf = 4,
    Hariharan = 5,
    Ferre = 6,
    Returned = 7,
    Warborn = 8
}

/// <summary>
/// Mirrors <c>AAEmu.Game.Models.Game.Char.Gender</c>.
/// Keep in sync with <c>AAEmu.Game/Models/Game/Char/Gender.cs</c>.
/// </summary>
public enum Gender : byte
{
    Male = 1,
    Female = 2
}
