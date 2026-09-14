using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

public sealed class BattlefieldGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        using var command = Connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE instance_rank_details (id INTEGER PRIMARY KEY, instance_id INTEGER NOT NULL, rating_only TEXT);";
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task LoadInstanceRankDetailIds_IndexesRatingTypeByInstanceIdentity()
    {
        using (var seed = Connection.CreateCommand())
        {
            seed.CommandText = "INSERT INTO instance_rank_details(id,instance_id,rating_only) " +
                               "VALUES(41,69,'f'),(30,4,'f')";
            seed.ExecuteNonQuery();
        }

        var mapping = BattlefieldGameData.LoadInstanceRankDetailIds(Connection);

        await Assert.That(mapping[69]).IsEqualTo(41u);
        await Assert.That(mapping[4]).IsEqualTo(30u);
        await Assert.That(mapping.ContainsKey(30)).IsFalse();
    }
}
