using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.UnitTests.Utils;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Mates;

/// <summary>
/// A mate's own HP/MP recovery: the two maxima it reconstructs, the rows its bars recover by, and the
/// state a recovered bar is read back from when the mate is summoned again.
/// </summary>
/// <remarks>
/// <para>
/// The rows and the multipliers below are deliberately synthetic. The shipped rows have a shape this file
/// only has to exercise — a maximum that multiplies by a per-kind variable, a persistent pair that reads
/// one attribute each — and the values here are chosen so that reading the wrong row, folding the two
/// maxima differently, or imposing a code-side minimum all land on an answer the test rejects.
/// </para>
/// <para>
/// The rows live in the FormulaManager singleton and the attribute bounds in
/// <see cref="UnitAttributeLimitGameData"/>; a parallel test swapping either would change the answer
/// mid-assertion.
/// </para>
/// </remarks>
[NotInParallel]
public class MateRecoveryTests
{
    /// <summary>
    /// Mate kinds the maximum tests compare. The first two carry the same pair of multipliers the other
    /// way round, which is the shape the shipped data has: a kind's health multiplier and its mana
    /// multiplier are independent, so a bar reading the other bar's row cannot land on the right answer.
    /// </summary>
    private const int HealthHeavyMateKind = 7;
    private const int ManaHeavyMateKind = 8;
    private const int EvenMateKind = 9;

    /// <summary>Truncates to nothing at the low levels, so a code-side minimum would be visible.</summary>
    private const string TruncatingPersistentRow = "sta / 100";
    private const string TruncatingPersistentManaRow = "spi / 100";

    /// <summary>Leaves room between the out-of-combat row and the persistent one at the test level.</summary>
    private const string PersistentRow = "sta / 10";
    private const string PersistentManaRow = "spi / 10";

    private static readonly (UnitFormulaKind Kind, string Text)[] FormulaRows =
    [
        (UnitFormulaKind.Str, "10 + (level - 1) * 4"),
        (UnitFormulaKind.Dex, "10 + (level - 1) * 4"),
        (UnitFormulaKind.Sta, "10 + (level - 1) * 4"),
        (UnitFormulaKind.Int, "10 + (level - 1) * 4"),
        (UnitFormulaKind.Spi, "10 + (level - 1) * 4"),
        (UnitFormulaKind.Fai, "10 + (level - 1) * 4"),
        (UnitFormulaKind.MaxHealth, "( level * 40 + sta * 6 ) * mate_kind"),
        (UnitFormulaKind.MaxMana, "( level * 40 + int * 6 ) * mate_kind"),
        (UnitFormulaKind.HealthRegen, "spi * 0.5 + 3"),
        (UnitFormulaKind.ManaRegen, "spi * 0.25 + 3")
    ];

    /// <summary>
    /// The kind multipliers, keyed by the mate kind they belong to. The health and mana maxima are the
    /// same pair reversed, so a mate of the other kind has its two bars exchanged.
    /// </summary>
    private static readonly Dictionary<UnitFormulaKind, Dictionary<int, float>> KindMultipliers = new()
    {
        [UnitFormulaKind.MaxHealth] = new Dictionary<int, float>
        {
            [HealthHeavyMateKind] = 1.5f,
            [ManaHeavyMateKind] = 0.5f,
            [EvenMateKind] = 1f
        },
        [UnitFormulaKind.MaxMana] = new Dictionary<int, float>
        {
            [HealthHeavyMateKind] = 0.5f,
            [ManaHeavyMateKind] = 1.5f,
            [EvenMateKind] = 1f
        }
    };

    private static SingletonScope<FormulaManager> WithFormulaRows(
        string persistentHealthRow = PersistentRow,
        string persistentManaRow = PersistentManaRow)
    {
        var manager = new FormulaManager();
        var byKind = new Dictionary<UnitFormulaKind, UnitFormula>();
        var variables = new Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>();

        var rows = FormulaRows
            .Append((UnitFormulaKind.PersistentHealthRegen, persistentHealthRow))
            .Append((UnitFormulaKind.PersistentManaRegen, persistentManaRow))
            .ToArray();

        var id = 1u;
        foreach (var (kind, text) in rows)
        {
            var formula = new UnitFormula
            {
                Id = id++,
                Kind = kind,
                Owner = FormulaOwnerType.Mate,
                TextFormula = text
            };
            formula.Prepare();
            byKind[kind] = formula;

            if (!KindMultipliers.TryGetValue(kind, out var multipliers))
                continue;

            var byKey = new Dictionary<uint, UnitFormulaVariable>();
            foreach (var (mateKind, value) in multipliers)
            {
                byKey[(uint)mateKind] = new UnitFormulaVariable
                {
                    FormulaId = formula.Id,
                    Type = UnitFormulaVariableType.MateKind,
                    Key = (uint)mateKind,
                    Value = value
                };
            }

            variables[formula.Id] = new Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>
            {
                [UnitFormulaVariableType.MateKind] = byKey
            };
        }

        SetField(manager, "_unitFormulas",
            new Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>>
            {
                [FormulaOwnerType.Mate] = byKind
            });
        SetField(manager, "_unitVariables", variables);

        return new SingletonScope<FormulaManager>(manager);
    }

    /// <summary>Installs attribute bounds for the two maxima, or none at all when both are zero.</summary>
    private static SingletonScope<UnitAttributeLimitGameData> WithAttributeLimit(long minimum, long maximum)
    {
        var data = new UnitAttributeLimitGameData();
        if (minimum == 0 && maximum == 0)
            return new SingletonScope<UnitAttributeLimitGameData>(data);

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE unit_attribute_limits (
                    id INTEGER PRIMARY KEY,
                    unit_attribute_id INTEGER NOT NULL,
                    minimum INTEGER NOT NULL,
                    maximum INTEGER NOT NULL);
                INSERT INTO unit_attribute_limits(id, unit_attribute_id, minimum, maximum)
                VALUES (1, 6, $min, $max), (2, 7, $min, $max);
                """;
            command.Parameters.AddWithValue("$min", minimum);
            command.Parameters.AddWithValue("$max", maximum);
            command.ExecuteNonQuery();
        }

        data.Load(connection);
        return new SingletonScope<UnitAttributeLimitGameData>(data);
    }

    private static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                continue;
            field.SetValue(target, value);
            return;
        }

        throw new InvalidOperationException($"No field {name} on {target.GetType().Name}");
    }

    private static Mate Spawn(int mateKind, int level) => new()
    {
        ObjId = 300,
        TlId = 300,
        Level = (byte)level,
        Template = new NpcTemplate { Id = 1, MateKindId = mateKind, Level = (byte)level }
    };

    private static void AddFlat(Mate mate, UnitAttribute attribute, long value, uint index) =>
        mate.AddBonus(index, new Bonus
        {
            Template = new BonusTemplate { Attribute = attribute, ModifierType = UnitModifierType.Value },
            Value = value
        });

    private static void AddPercent(Mate mate, UnitAttribute attribute, long value, uint index) =>
        mate.AddBonus(index, new Bonus
        {
            Template = new BonusTemplate { Attribute = attribute, ModifierType = UnitModifierType.Percent },
            Value = value
        });

    /// <summary>
    /// The two bars take the multiplier of their own formula. The two kinds carry the same pair the other
    /// way round, so an implementation that read the shared kind id, or the other bar's row, cannot
    /// produce both answers.
    /// </summary>
    [Test]
    public async Task Maxima_ReadTheMateKindMultiplierOfTheirOwnRow()
    {
        using var formulas = WithFormulaRows();
        using var limits = WithAttributeLimit(0, 0);

        var healthHeavy = Spawn(HealthHeavyMateKind, level: 10);
        var manaHeavy = Spawn(ManaHeavyMateKind, level: 10);
        // sta and int are the same row here, so a single shared multiplier would make the two bars equal.
        await Assert.That(healthHeavy.Sta).IsEqualTo(healthHeavy.Int);
        var bareBar = (10 * 40) + healthHeavy.Sta * 6;

        await Assert.That(healthHeavy.MaxHp).IsEqualTo((int)(bareBar * 1.5f));
        await Assert.That(healthHeavy.MaxMp).IsEqualTo((int)(bareBar * 0.5f));
        await Assert.That(manaHeavy.MaxHp).IsEqualTo((int)(bareBar * 0.5f));
        await Assert.That(manaHeavy.MaxMp).IsEqualTo((int)(bareBar * 1.5f));

        // The exchange has to show on the mate as well, not only on the two bars side by side.
        await Assert.That(healthHeavy.MaxHp).IsEqualTo(manaHeavy.MaxMp);
        await Assert.That(healthHeavy.MaxMp).IsEqualTo(manaHeavy.MaxHp);
        await Assert.That(healthHeavy.MaxHp).IsNotEqualTo(healthHeavy.MaxMp);
    }

    /// <summary>
    /// A mate kind no <c>unit_formula_variables</c> row covers is content corruption. Answering the
    /// missing row the way the general lookup does multiplies the whole bar by nothing, so the mate would
    /// own a maximum of zero, count as dead on arrival, and have every later heal and every regen tick
    /// clamped to nothing.
    /// </summary>
    [Test]
    public async Task Maxima_RefuseToGuessWhenTheMateKindRowIsMissing()
    {
        using var formulas = WithFormulaRows();
        using var limits = WithAttributeLimit(0, 0);

        var unknownKind = Spawn(99, level: 10);
        // Everything except the kind multiplier is readable, so the refusal below is about the missing
        // row and not about a fixture that never loaded.
        await Assert.That(unknownKind.Sta).IsEqualTo(10 + 9 * 4);

        Assert.Throws<KeyNotFoundException>(() => _ = unknownKind.MaxHp);
        Assert.Throws<KeyNotFoundException>(() => _ = unknownKind.MaxMp);
    }

    /// <summary>
    /// Both maxima finish on the <c>unit_attribute_limits</c> bound their own attribute carries. A bound
    /// below the reconstructed bar is the only thing that can show the clamp running at all, and it has to
    /// run on both bars: the two bars of one creature are clamped by the same table or by neither.
    /// </summary>
    [Test]
    public async Task Maxima_AreClampedToTheirAttributeLimitRow()
    {
        const long bound = 200;
        using var formulas = WithFormulaRows();
        using var limits = WithAttributeLimit(0, bound);

        var mate = Spawn(HealthHeavyMateKind, level: 10);
        // The reconstructions the rows produce on their own, so the bound below is shown to bite on both
        // bars rather than one of them merely happening to sit under it already.
        var reconstructedBar = (10 * 40) + mate.Sta * 6;
        await Assert.That((int)(reconstructedBar * 1.5f)).IsGreaterThan((int)bound);
        await Assert.That((int)(reconstructedBar * 0.5f)).IsGreaterThan((int)bound);

        await Assert.That(mate.MaxHp).IsEqualTo((int)bound);
        await Assert.That(mate.MaxMp).IsEqualTo((int)bound);
    }

    /// <summary>
    /// The same bonus set has to move both bars by the same amount, in the same order.
    /// </summary>
    /// <remarks>
    /// The percent row is registered before the flat one, so folding in table order composes the percent
    /// onto the bare row and adds the flat afterwards, while folding flat-then-percent composes the flat
    /// onto the bare row first and the percent onto that. The two orders disagree on these numbers, so a
    /// mate whose health maximum was folded one way and whose mana maximum the other would answer the same
    /// gear with two different bars.
    /// </remarks>
    [Test]
    public async Task Maxima_FoldTheSameBonusSetTheSameWay()
    {
        using var formulas = WithFormulaRows();
        using var limits = WithAttributeLimit(0, 0);

        var bare = Spawn(EvenMateKind, level: 10);
        var bareBar = bare.MaxHp;
        await Assert.That(bare.MaxMp).IsEqualTo(bareBar);

        var bonused = Spawn(EvenMateKind, level: 10);
        AddPercent(bonused, UnitAttribute.MaxHealth, 10, 1);
        AddFlat(bonused, UnitAttribute.MaxHealth, 200, 2);
        AddPercent(bonused, UnitAttribute.MaxMana, 10, 3);
        AddFlat(bonused, UnitAttribute.MaxMana, 200, 4);

        // Table order: the percent lands on the bare row, the flat is added to that.
        var tableOrder = bareBar + (int)(bareBar * 10 / 100f) + 200;
        await Assert.That(bonused.MaxHp).IsEqualTo(tableOrder);
        await Assert.That(bonused.MaxMp).IsEqualTo(tableOrder);
        await Assert.That(bonused.MaxHp).IsEqualTo(bonused.MaxMp);

        // Flat-then-percent is a different number, so the order is pinned and not only the total.
        var flatThenPercent = (bareBar + 200) + (int)((bareBar + 200) * 10 / 100f);
        await Assert.That(bonused.MaxHp).IsNotEqualTo(flatThenPercent);
    }

    /// <summary>
    /// The two persistent rows are one shape over a different attribute, so the two bars of one creature
    /// cannot recover at rates the content never described — and neither getter may add a floor the row
    /// does not ask for. The rows here truncate to nothing at the low levels, which is exactly where a
    /// code-side minimum shows itself: the health getter used to answer one where the row answers nothing.
    /// </summary>
    [Test]
    public async Task PersistentRegen_IsTheRowOnBothBarsAndCarriesNoCodeSideMinimum()
    {
        using var formulas = WithFormulaRows(TruncatingPersistentRow, TruncatingPersistentManaRow);
        using var limits = WithAttributeLimit(0, 0);

        foreach (var level in new[] { 1, 5, 40 })
        {
            var mate = Spawn(HealthHeavyMateKind, level);
            var stat = 10 + (level - 1) * 4;
            var row = (int)(stat / 100d);
            // The first two levels have to truncate to nothing, or a floor would not be visible.
            await Assert.That(row).IsEqualTo(level == 40 ? 1 : 0);

            await Assert.That(mate.PersistentHpRegen).IsEqualTo(row);
            await Assert.That(mate.PersistentMpRegen).IsEqualTo(row);
            await Assert.That(mate.PersistentHpRegen).IsEqualTo(mate.PersistentMpRegen);
        }
    }

    /// <summary>
    /// One tick recovers each bar by the out-of-combat row while the mate is out of battle and by the
    /// persistent row while it is in one, never past the maximum it reconstructed, and what it recovered
    /// reaches the owned row — so a save taken while the mate is recovering carries the bars it has
    /// reached rather than the ones it summoned with.
    /// </summary>
    [Test]
    public async Task RegenTick_RecoversFromTheRowsStopsAtTheMaximumAndWritesThrough()
    {
        using var formulas = WithFormulaRows();
        using var limits = WithAttributeLimit(0, 0);
        using var connection = CreateMatesConnection();
        InsertMate(connection, itemId: 1001, hp: 10, mp: 10);
        using var worldScope = TestDungeonWorld.InstallWorldManager();
        using var world = TestDungeonWorld.CreateSizedWorld(9920, 0, 2, 2, 133);
        using var taskScope = new SingletonScope<TaskManager>(new TaskManager(Mock.Of<ITickManager>().Object));

        var owner = new Character(new UnitCustomModelParams()) { Id = 77, ObjId = 7001 };
        TestDungeonWorld.Enter(world, owner);
        // The regen tick finds the owner through the manager's own character list, which is where an
        // entry puts a player; entering the world is not enough for that lookup.
        await Assert.That(WorldManager.Instance.TryAddCharacter(owner)).IsTrue();
        owner.Mates = new CharacterMates(owner);
        owner.Mates.Load(connection);

        var mate = Spawn(EvenMateKind, level: 10);
        mate.ParentWorld = world;
        mate.OwnerId = owner.Id;
        mate.OwnerObjId = owner.ObjId;
        mate.ItemId = 1001;
        mate.Name = "synthetic-recovering-mate";
        mate.Transform = owner.Transform.CloneDetached(mate);
        // Both bars start half-way, so one tick lands strictly inside the maximum and the write-through is
        // checked on a value the tick actually moved rather than on one it was already sitting on.
        mate.Hp = mate.MaxHp / 2;
        mate.Mp = mate.MaxMp / 2;
        var startHp = mate.Hp;
        var startMp = mate.Mp;

        // Out of battle: the out-of-combat rows move both bars, and both stay under the maximum.
        await Assert.That(mate.HpRegen).IsGreaterThan(1);
        mate.OnActiveRegionTick(TimeSpan.FromSeconds(1));
        await Assert.That(mate.Hp).IsEqualTo(startHp + mate.HpRegen);
        await Assert.That(mate.Mp).IsEqualTo(startMp + mate.MpRegen);
        await Assert.That(mate.Hp).IsLessThan(mate.MaxHp);
        await Assert.That(mate.Hp).IsGreaterThan(0);
        await Assert.That(mate.Mp).IsLessThan(mate.MaxMp);
        await Assert.That(mate.Mp).IsGreaterThan(0);

        var recovered = owner.Mates.GetMateInfo(1001);
        await Assert.That(recovered.Hp).IsEqualTo(mate.Hp);
        await Assert.That(recovered.Mp).IsEqualTo(mate.Mp);
        await Assert.That(recovered.Hp).IsNotEqualTo(startHp);

        // In battle: the persistent rows take over, and they are not the numbers the other pair moved.
        // The combat timeout is read on the same tick and would drop the flag again straight away, so the
        // combat activity is stamped the way a hit or an ordered attack stamps it.
        var inBattleStartHp = mate.Hp;
        var inBattleStartMp = mate.Mp;
        mate.IsInBattle = true;
        mate.LastCombatActivity = DateTime.UtcNow;
        await Assert.That(mate.PersistentHpRegen).IsGreaterThan(0);
        await Assert.That(mate.PersistentHpRegen).IsNotEqualTo(mate.HpRegen);
        mate.OnActiveRegionTick(TimeSpan.FromSeconds(1));
        await Assert.That(mate.Hp).IsEqualTo(inBattleStartHp + mate.PersistentHpRegen);
        await Assert.That(mate.Mp).IsEqualTo(inBattleStartMp + mate.PersistentMpRegen);
        await Assert.That(owner.Mates.GetMateInfo(1001).Hp).IsEqualTo(mate.Hp);

        // A bar left a hair under the maximum recovers onto it and stops there, whatever the row says.
        mate.Hp = mate.MaxHp - 1;
        mate.Mp = mate.MaxMp - 1;
        mate.OnActiveRegionTick(TimeSpan.FromSeconds(1));
        await Assert.That(mate.Hp).IsEqualTo(mate.MaxHp);
        await Assert.That(mate.Mp).IsEqualTo(mate.MaxMp);
    }

    /// <summary>
    /// "Never recorded" and "recorded zero" are different readings and must restore differently.
    /// </summary>
    /// <remarks>
    /// This is the collision the review named. A bar of 0 used to mean "this row never held one", so a
    /// mate captured dead, or at empty mana, came back at full health and full mana. The unrecorded
    /// state is now its own value and the column is nullable, so 0 is a real reading again.
    /// </remarks>
    [Test]
    public async Task RestorePoints_TellsNeverRecordedApartFromARecordedZero()
    {
        const int maximum = 1000;

        // Never recorded, as a state and as a missing column.
        await Assert.That(MateRecoveryRules.RestorePoints(MateRecoveryRules.Unrecorded, maximum))
            .IsEqualTo(maximum);
        await Assert.That(MateRecoveryRules.RestorePoints((int?)null, maximum)).IsEqualTo(maximum);

        // A real zero is restored as zero, at any maximum. This is the regression.
        await Assert.That(MateRecoveryRules.RestorePoints(0, maximum)).IsEqualTo(0);
        await Assert.That(MateRecoveryRules.RestorePoints(0, 0)).IsEqualTo(0);

        // And the two really are different answers, which is the whole point.
        await Assert.That(MateRecoveryRules.RestorePoints(0, maximum))
            .IsNotEqualTo(MateRecoveryRules.RestorePoints(MateRecoveryRules.Unrecorded, maximum));

        // The unrecorded marker must not be mistaken for a number content could have written.
        await Assert.That(MateRecoveryRules.Unrecorded).IsLessThan(0);
    }

    /// <summary>A recorded bar inside the maximum is kept, and one over it is cut down to it.</summary>
    [Test]
    public async Task RestorePoints_KeepsARecordedBarAndCapsWhatIsOver()
    {
        const int maximum = 1000;

        await Assert.That(MateRecoveryRules.RestorePoints(1, maximum)).IsEqualTo(1);
        await Assert.That(MateRecoveryRules.RestorePoints(500, maximum)).IsEqualTo(500);
        await Assert.That(MateRecoveryRules.RestorePoints(maximum, maximum)).IsEqualTo(maximum);
        await Assert.That(MateRecoveryRules.RestorePoints(maximum + 1, maximum)).IsEqualTo(maximum);
        await Assert.That(MateRecoveryRules.RestorePoints(int.MaxValue, maximum)).IsEqualTo(maximum);

        // An unrecorded bar and a bar that recovered to the cap are not the same statement, and the
        // rule that produces them must not quietly produce a third answer between the two.
        await Assert.That(MateRecoveryRules.RestorePoints(MateRecoveryRules.Unrecorded, maximum))
            .IsNotEqualTo(MateRecoveryRules.RestorePoints(1, maximum));

        Assert.Throws<ArgumentOutOfRangeException>(() => MateRecoveryRules.RestorePoints(1, -1));
    }

    private static SqliteConnection CreateMatesConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE mates (
                id INTEGER NOT NULL,
                item_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                xp INTEGER NOT NULL,
                level INTEGER NOT NULL,
                mileage INTEGER NOT NULL,
                hp INTEGER NOT NULL,
                mp INTEGER NOT NULL,
                owner INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                PRIMARY KEY (id, item_id, owner));
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static void InsertMate(SqliteConnection connection, ulong itemId, int hp, int mp)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mates(
                id, item_id, name, xp, level, mileage, hp, mp, owner, updated_at, created_at)
            VALUES (101, $item, 'synthetic-owned-mate', 0, 10, 0, $hp, $mp, 77, $now, $now);
            """;
        command.Parameters.AddWithValue("$item", itemId);
        command.Parameters.AddWithValue("$hp", hp);
        command.Parameters.AddWithValue("$mp", mp);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow);
        command.ExecuteNonQuery();
    }
}
