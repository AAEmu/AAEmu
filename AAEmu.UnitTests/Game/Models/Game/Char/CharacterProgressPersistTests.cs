using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using MySql.Data.MySqlClient;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

/// <summary>
/// Records and achievements are rewritten inside <see cref="Character.Save"/>'s transaction — their rows are
/// deleted and put back. A write that fails there therefore has to reach the caller: a save that reported
/// success and committed after a DELETE it could not follow with its INSERTs would leave the character with
/// no progress at all, and the periodic save would do it again on every pass.
/// </summary>
public sealed class CharacterProgressPersistTests
{
    [Test]
    public async Task Records_WriteFailure_ReachesTheCaller()
    {
        var records = new CharacterRecords(Owner());
        records.Set(400, 12);

        await Assert.That(ThrownBy(() => records.Save(UnusableConnection(), null))).IsNotNull();
    }

    [Test]
    public async Task Achievements_WriteFailure_ReachesTheCaller()
    {
        var achievements = new CharacterAchievements(Owner());
        await Assert.That(achievements.SetAmount(2333, 4)).IsEqualTo(4);
        await Assert.That(achievements.Complete(2333, DateTime.UtcNow)).IsTrue();

        await Assert.That(ThrownBy(() => achievements.Save(UnusableConnection(), null))).IsNotNull();
    }

    private static Character Owner() =>
        new(new UnitCustomModelParams()) { Id = 42, Name = "ProgressKeeper" };

    /// <summary>A real connection the driver refuses to run a statement on, so the first one fails.</summary>
    private static MySqlConnection UnusableConnection() => new();

    private static Exception ThrownBy(Action save)
    {
        try
        {
            save();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
