#nullable enable

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class ExperienceManager : Singleton<ExperienceManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>List of experience templates, indexed by zero-based level (level 1 is index 0).</summary>
    private readonly List<ExperienceLevelTemplate> _levelTemplatesByLevel = [];
    /// <summary>Sorted list of total experience amounts from lowest level to highest level, indexed by zero-based level (level 1 is index 0).</summary>
    private readonly List<int> _expByLevel = [];
    /// <summary>Sorted list of total mate experience amounts from lowest level to highest level, indexed by zero-based mate level (level 1 is index 0).</summary>
    private readonly List<int> _mateExpByLevel = [];

    // TODO: Put this in the configuration files
    /// <summary>Artificial level cap for players. If database contains more levels than this, they will be ignored.</summary>
    private static byte PlayerLevelCap => 55;
    /// <summary>Artificial level cap for mates (mounts, pets). If database contains more levels than this, they will be ignored.</summary>
    private static byte MateLevelCap => 50;

    /// <summary>
    /// Gets the maximum level for players.
    /// </summary>
    public byte MaxPlayerLevel { get; private set; }

    /// <summary>
    /// Gets the maximum level for mates (mounts, pets).
    /// </summary>
    public byte MaxMateLevel { get; private set; }

    /// <summary>
    /// Gets the total experience required to reach the given level.
    /// </summary>
    /// <param name="level">The level to reach.</param>
    /// <param name="mate"><c>true</c> to get the experience for a mate (mount, pet); <c>false</c> to get the experience for a player.</param>
    /// <returns>The total experience required to reach the given level, or 0 if the level is invalid.</returns>
    public int GetExpForLevel(byte level, bool mate = false)
    {
        if (GetTemplateForLevel(level) is { } levelTemplate)
            return mate ? levelTemplate.TotalMateExp : levelTemplate.TotalExp;

        return 0;
    }

    /// <summary>
    /// Gets the level that corresponds to the given experience amount.
    /// </summary>
    /// <param name="exp">The amount of experience.</param>
    /// <param name="overflow">The amount of experience that exceeds the level.</param>
    /// <param name="mate"><c>true</c> to get the level for a mate (mount, pet); <c>false</c> to get the level for a player.</param>
    /// <returns>The level that corresponds to the given experience amount, or the maximum level if the experience exceeds that of the maximum level.</returns>
    /// <remarks>Prefer the <see cref="GetLevelFromExp(int, byte, out int, bool)"/> overload if the current level is known.</remarks>
    /// <seealso cref="GetLevelFromExp(int, byte, out int, bool)"/>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="exp"/> is negative.</exception>
    public byte GetLevelFromExp(int exp, out int overflow, bool mate = false)
        => GetLevelFromExp(exp, mate, out overflow, minLevel: 0);

    /// <summary>
    /// Gets the level that corresponds to the given experience amount.
    /// </summary>
    /// <param name="exp">The amount of experience.</param>
    /// <param name="currentLevel">The current level of the unit.</param>
    /// <param name="overflow">The amount of experience that exceeds the level.</param>
    /// <param name="mate"><c>true</c> to get the level for a mate (mount, pet); <c>false</c> to get the level for a player.</param>
    /// <returns>The level that corresponds to the given experience amount, or the maximum level if the experience exceeds that of the maximum level.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="exp"/> is negative.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="currentLevel"/> is zero.</exception>
    public byte GetLevelFromExp(int exp, byte currentLevel, out int overflow, bool mate = false)
    {
        ArgumentOutOfRangeException.ThrowIfZero(currentLevel);
        return GetLevelFromExp(exp, mate, out overflow, minLevel: currentLevel);
    }

    /// <summary>
    /// Gets the level that corresponds to the given experience amount.
    /// </summary>
    /// <param name="exp">The amount of experience.</param>
    /// <param name="mate"><c>true</c> to get the level for a mate (mount, pet); <c>false</c> to get the level for a player.</param>
    /// <param name="minLevel">The minimum level of the unit to consider. Should usually be the current level of the unit.</param>
    /// <param name="overflow">The amount of experience that exceeds the level.</param>
    /// <returns>The level that corresponds to the given experience amount, or the maximum level if the experience exceeds that of the maximum level.</returns>
    /// <remarks>The <paramref name="minLevel"/> parameter is an optimization to speed up locating the level for a given experience value, by excluding certain levels.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="exp"/> is negative.</exception>
    private byte GetLevelFromExp(int exp, bool mate, out int overflow, byte minLevel = 0)
    {
        // This method relies on units being unable to lose levels (experience can be lost, but not causing de-levelling).
        ArgumentOutOfRangeException.ThrowIfNegative(exp);

        var expByLevel = mate ? _mateExpByLevel : _expByLevel;
        var maxLevel = mate ? MaxMateLevel : MaxPlayerLevel;

        // Check if minLevel is already at or beyond the maximum level.
        // This prevents out of bounds indexing below (or indexing into values beyond the max level, when the db contains more rows than needed)
        if (minLevel >= maxLevel)
        {
            overflow = Math.Max(0, exp - GetExpForLevel(maxLevel, mate));
            return maxLevel;
        }

        // Limit the binary search to the range between the min possible level and the max level of the unit (better for mates which have a lower max level)
        var count = Math.Min(expByLevel.Count - minLevel, maxLevel);
        var index = expByLevel.BinarySearch(minLevel, count, exp, null);

        // Found the exact exp value - add 1 to turn 0-based index into level
        if (index >= 0)
        {
            overflow = 0;
            return (byte)(index + 1);
        }

        // Get the index of the next-largest exp value
        var nextLargestIndex = ~index; // Will equal list.Count if the exp value is larger than all levels
        if (nextLargestIndex < expByLevel.Count)
        {
            var level = (byte)nextLargestIndex;
            overflow = exp - GetExpForLevel(level, mate);
            return level;
        }

        // Exp is greater than the largest level's exp.
        // We still provide overflow exp, even though this shouldn't be applied to the character.
        overflow = Math.Max(0, exp - GetExpForLevel(maxLevel, mate));
        return maxLevel;
    }

    /// <summary>
    /// Gets the experience needed to reach the given level from the current experience amount.
    /// </summary>
    /// <param name="currentExp">The current amount of experience.</param>
    /// <param name="targetLevel">The target level to reach.</param>
    /// <param name="mate"><c>true</c> to get the level for a mate (mount, pet); <c>false</c> to get the level for a player.</param>
    /// <returns>The amount of experience needed to reach the given level, or 0 if the target level is invalid or already reached.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="currentExp"/> is negative.</exception>
    public int GetExpNeededToGivenLevel(int currentExp, byte targetLevel, bool mate = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentExp);
        var targetExp = GetExpForLevel(targetLevel, mate);
        var diff = targetExp - currentExp;
        return Math.Max(0, diff);
    }

    /// <summary>
    /// Gets the total number of skill points for the given level.
    /// </summary>
    /// <param name="level">The level of the player.</param>
    /// <returns>The total number of skill points for the given level, or 0 if the level is invalid.</returns>
    public int GetSkillPointsForLevel(byte level)
        => GetTemplateForLevel(level)?.SkillPoints ?? 0;

    /// <summary>
    /// Loads the experience level templates from the default loader (Sqlite).
    /// </summary>
    public void Load()
        => Load(new SqliteExperienceLevelTemplateLoader(Logger), PlayerLevelCap, MateLevelCap);

    /// <summary>
    /// Loads the experience level templates from the given loader.
    /// </summary>
    /// <param name="loader">The loader for the experience level templates.</param>
    /// <param name="playerLevelCap">The maximum level for players.</param>
    /// <param name="mateLevelCap">The maximum level for mates (mounts, pets).</param>
    /// <remarks>
    /// The maximum levels for players and mates will be the lower of the number of levels loaded
    /// from <paramref name="loader"/>, and the corresponding level cap.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="playerLevelCap"/> is zero.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="mateLevelCap"/> is zero.</exception>
    public void Load(IExperienceLevelTemplateLoader loader, byte playerLevelCap, byte mateLevelCap)
    {
        ArgumentOutOfRangeException.ThrowIfZero(playerLevelCap);
        ArgumentOutOfRangeException.ThrowIfZero(mateLevelCap);

        _levelTemplatesByLevel.Clear();
        _expByLevel.Clear();
        _mateExpByLevel.Clear();

        Logger.Info("Loading experience data...");

        foreach (var levelTemplate in loader.Load())
        {
            _levelTemplatesByLevel.Add(levelTemplate);
            _expByLevel.Add(levelTemplate.TotalExp);
            _mateExpByLevel.Add(levelTemplate.TotalMateExp);
        }

        // Set the maximum levels for players and mates to either the number of levels in the database, or the configured level cap, whichever is lower
        MaxPlayerLevel = (byte)Math.Min(_levelTemplatesByLevel.Count, playerLevelCap);
        MaxMateLevel = (byte)Math.Min(_levelTemplatesByLevel.Count, mateLevelCap);

        Logger.Info("Experience data loaded");
    }

    private ExperienceLevelTemplate? GetTemplateForLevel(byte level)
    {
        if (level <= 0 || level > _levelTemplatesByLevel.Count)
            return null;
        return _levelTemplatesByLevel[level - 1];
    }
}
