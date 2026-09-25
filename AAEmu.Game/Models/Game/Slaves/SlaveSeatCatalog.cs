using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.Models.Game.Slaves;

public sealed class SlaveSeatCatalog
{
    private readonly Dictionary<uint, HashSet<AttachPointKind>> _seatsBySlave = [];

    public void Load(SqliteConnection connection)
    {
        _seatsBySlave.Clear();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sms.slave_id, mas.attach_point_id
            FROM slave_mount_skills AS sms
            INNER JOIN mount_skills AS ms ON ms.id = sms.mount_skill_id
            INNER JOIN mount_attached_skills AS mas ON mas.mount_skill_id = ms.id
            ORDER BY sms.slave_id, mas.attach_point_id
            """;
        command.Prepare();

        using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
        {
            while (reader.Read())
                Add(reader.GetUInt32("slave_id"), reader.GetInt32("attach_point_id"));
        }

        command.CommandText = """
            SELECT id
            FROM slaves
            WHERE mountable = 't'
            """;
        using (var mountable = new SQLiteWrapperReader(command.ExecuteReader()))
        {
            while (mountable.Read())
                Add(mountable.GetUInt32("id"), (int)AttachPointKind.Driver);
        }

        command.CommandText = """
            SELECT owner_id, attach_point_id
            FROM slave_doodad_bindings
            WHERE owner_type = 'Slave'
            """;
        using (var bindings = new SQLiteWrapperReader(command.ExecuteReader()))
        {
            while (bindings.Read())
                AddBinding(bindings.GetUInt32("owner_id"), bindings.GetInt32("attach_point_id"));
        }
    }

    public void LoadFromRows(IEnumerable<(uint TemplateId, int AttachPointId)> rows)
    {
        _seatsBySlave.Clear();
        foreach (var (templateId, attachPointId) in rows ?? [])
        {
            Add(templateId, attachPointId);
        }
    }

    public IReadOnlyList<AttachPointKind> GetSeats(uint slaveId) =>
        _seatsBySlave.TryGetValue(slaveId, out var seats)
            ? seats.OrderBy(point => (byte)point).ToArray()
            : Array.Empty<AttachPointKind>();

    public bool HasSeat(uint slaveId, AttachPointKind attachPoint) =>
        _seatsBySlave.TryGetValue(slaveId, out var seats) && seats.Contains(attachPoint);

    private void Add(uint slaveId, int rawAttachPoint)
    {
        if (!SeatTopologyRules.TryNormalize(rawAttachPoint, out var attachPoint))
            return;

        if (!_seatsBySlave.TryGetValue(slaveId, out var seats))
        {
            seats = [];
            _seatsBySlave[slaveId] = seats;
        }

        seats.Add(attachPoint);
    }

    private void AddBinding(uint slaveId, int rawAttachPoint)
    {
        if (rawAttachPoint <= byte.MinValue || rawAttachPoint > byte.MaxValue)
            return;

        if (!_seatsBySlave.TryGetValue(slaveId, out var seats))
        {
            seats = [];
            _seatsBySlave[slaveId] = seats;
        }

        seats.Add((AttachPointKind)(byte)rawAttachPoint);
    }
}
