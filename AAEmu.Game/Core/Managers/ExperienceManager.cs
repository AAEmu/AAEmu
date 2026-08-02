#nullable enable

using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class ExperienceManager : Singleton<ExperienceManager>, IExperienceManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>List of experience templates, indexed by zero-based level (level 1 is index 0).</summary>
    private readonly List<ExperienceLevelTemplate> _levelTemplatesByLevel = [];
    /// <summary>Sorted list of total experience amounts from lowest level to highest level, indexed by zero-based level (level 1 is index 0).</summary>
    private readonly List<int> _expByLevel = [];
    /// <summary>Sorted list of total mate experience amounts from lowest level to highest level, indexed by zero-based mate level (level 1 is index 0).</summary>
    private readonly List<int> _mateExpByLevel = [];

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
        => Load(
            new SqliteExperienceLevelTemplateLoader(Logger),
            AppConfiguration.Instance.World.PlayerLevelCap,
            AppConfiguration.Instance.World.MateLevelCap);

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

        // The streaming loader enforces strict, monotonically increasing total_exp/total_mate_exp
        // and contiguous levels. The 10.0.2.13 game DB contains malformed rows in the unused tail
        // (e.g. level 56 has an out-of-place total_mate_exp spike, level 57 drops below it), which
        // trips the sortedness assertion. Those rows are far beyond both level caps and are never
        // used, so once we have already loaded every level we will ever need, we tolerate the
        // loader's data-integrity exception for the remaining (unused) tail rather than abort.
        var requiredLevelCount = Math.Max(playerLevelCap, mateLevelCap);
        using var loadedTemplates = loader.Load().GetEnumerator();
        while (true)
        {
            ExperienceLevelTemplate levelTemplate;
            try
            {
                if (!loadedTemplates.MoveNext())
                    break;
                levelTemplate = loadedTemplates.Current;
            }
            catch (InvalidDataException ex) when (_levelTemplatesByLevel.Count >= requiredLevelCount)
            {
                // We already have all the levels within the caps; the remaining rows are unused (e.g. the
                // mate-XP plateau past the mate cap), so this is expected rather than a problem.
                Logger.Debug(ex, "Ignoring unused experience data beyond level {0}", _levelTemplatesByLevel.Count);
                break;
            }

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
