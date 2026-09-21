using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

public class NpcInteractionGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute("""
            CREATE TABLE npc_interaction_sets (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE npc_interactions (
                id INTEGER PRIMARY KEY, npc_interaction_set_id INTEGER, skill_id INTEGER);
            """);
    }

    [Test]
    public async Task Load_KeepsTheAuthoredRowOrder()
    {
        Execute("""
            INSERT INTO npc_interaction_sets VALUES (1, 'set-a');
            INSERT INTO npc_interactions VALUES (10, 1, 11), (20, 1, 12), (30, 1, 13);
            """);
        var data = new NpcInteractionGameData();

        data.Load(Connection);

        await Assert.That(data.SetCount).IsEqualTo(1);
        await Assert.That(data.EntryCount).IsEqualTo(3);
        await Assert.That(data.GetSkills(1)).IsEquivalentTo(new uint[] { 11, 12, 13 });
    }

    [Test]
    public async Task Load_AllowsASetWithoutRows()
    {
        Execute("INSERT INTO npc_interaction_sets VALUES (2, 'empty');");
        var data = new NpcInteractionGameData();

        data.Load(Connection);

        await Assert.That(data.SetCount).IsEqualTo(1);
        await Assert.That(data.EntryCount).IsEqualTo(0);
        await Assert.That(data.GetSkills(2)).IsEmpty();
    }

    [Test]
    public async Task GetSkills_AnswersEmptyForZeroAndUnknownSets()
    {
        var data = new NpcInteractionGameData();
        data.SetForTest(new Dictionary<uint, uint[]> { [1] = [11] });

        await Assert.That(data.GetSkills(0)).IsEmpty();
        await Assert.That(data.GetSkills(999)).IsEmpty();
        await Assert.That(data.GetSkills(1)).IsEquivalentTo(new uint[] { 11 });
    }

    [Test]
    public async Task Load_RejectsARowForAMissingSet()
    {
        Execute("INSERT INTO npc_interactions VALUES (1, 1, 11);");
        var data = new NpcInteractionGameData();

        await Assert.That(() => data.Load(Connection)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Load_RejectsARepeatedSkillInsideOneSet()
    {
        Execute("""
            INSERT INTO npc_interaction_sets VALUES (1, 'set-a');
            INSERT INTO npc_interactions VALUES (1, 1, 11), (2, 1, 11);
            """);
        var data = new NpcInteractionGameData();

        await Assert.That(() => data.Load(Connection)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Load_RejectsZeroSetOrSkillIds()
    {
        Execute("""
            INSERT INTO npc_interaction_sets VALUES (1, 'set-a');
            INSERT INTO npc_interactions VALUES (1, 1, 0);
            """);
        var data = new NpcInteractionGameData();

        await Assert.That(() => data.Load(Connection)).Throws<InvalidDataException>();
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
