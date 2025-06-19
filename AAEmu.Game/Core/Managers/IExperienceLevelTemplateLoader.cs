#nullable enable

using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Defines a loader for experience level templates.
/// </summary>
public interface IExperienceLevelTemplateLoader
{
    /// <summary>
    /// Loads the experience level templates from a data source.
    /// </summary>
    /// <returns>An enumerable containing the experience level templates.</returns>
    /// <exception cref="InvalidDataException">Thrown when data in the underlying data source is invalid.</exception>
    /// <remarks>The experience level templates must be returned in order, from lowest level to highest level.</remarks>
    IEnumerable<ExperienceLevelTemplate> Load();
}
