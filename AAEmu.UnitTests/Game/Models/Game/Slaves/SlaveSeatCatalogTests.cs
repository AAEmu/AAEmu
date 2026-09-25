using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Slaves;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Slaves;

public class SlaveSeatCatalogTests
{
    [Test]
    public async Task OneSeat_DoesNotUseCapacityAsASecondSeat()
    {
        var catalog = new SlaveSeatCatalog();
        catalog.LoadFromRows([(101, (int)AttachPointKind.Driver)]);

        await Assert.That(catalog.GetSeats(101)).IsEquivalentTo(new[] { AttachPointKind.Driver });
        await Assert.That(catalog.HasSeat(101, AttachPointKind.Passenger0)).IsFalse();
    }

    [Test]
    public async Task MultiSeat_UsesOnlyExplicitJoinRows()
    {
        var catalog = new SlaveSeatCatalog();
        catalog.LoadFromRows([
            (202, (int)AttachPointKind.Passenger2),
            (202, (int)AttachPointKind.Driver),
            (202, (int)AttachPointKind.Passenger0),
        ]);

        await Assert.That(catalog.GetSeats(202)).IsEquivalentTo(new[]
        {
            AttachPointKind.Driver,
            AttachPointKind.Passenger0,
            AttachPointKind.Passenger2,
        });
    }

    [Test]
    public async Task InvalidRows_AreIgnoredInsteadOfCreatingEquipmentSlots()
    {
        var catalog = new SlaveSeatCatalog();
        catalog.LoadFromRows([
            (303, 0),
            (303, (int)AttachPointKind.Mast0),
            (303, (int)AttachPointKind.Box0),
            (303, (int)AttachPointKind.Passenger1),
        ]);

        await Assert.That(catalog.GetSeats(303)).IsEquivalentTo(new[] { AttachPointKind.Passenger1 });
    }

    [Test]
    public async Task Load_UsesTheSlaveMountJoinAndDropsNonSeatRows()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Execute(connection, "CREATE TABLE slave_mount_skills (slave_id INTEGER NOT NULL, mount_skill_id INTEGER NOT NULL)");
        Execute(connection, "CREATE TABLE mount_skills (id INTEGER PRIMARY KEY)");
        Execute(connection, "CREATE TABLE mount_attached_skills (mount_skill_id INTEGER NOT NULL, attach_point_id INTEGER NOT NULL)");
        Execute(connection, "INSERT INTO slave_mount_skills VALUES (404, 701)");
        Execute(connection, "INSERT INTO mount_skills VALUES (701)");
        Execute(connection, "INSERT INTO mount_attached_skills VALUES (701, 1), (701, 3), (701, 35)");

        var catalog = new SlaveSeatCatalog();
        catalog.Load(connection);

        await Assert.That(catalog.GetSeats(404)).IsEquivalentTo(new[]
        {
            AttachPointKind.Driver,
            AttachPointKind.Passenger1,
        });
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
