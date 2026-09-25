using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.Models.Game.Mate;

public sealed class MateSeatCatalog
{
    private readonly Dictionary<uint, HashSet<AttachPointKind>> _seatsByNpc = [];

    public void Load(SqliteConnection connection)
    {
        _seatsByNpc.Clear();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT nms.npc_id, mas.attach_point_id
            FROM npc_mount_skills AS nms
            INNER JOIN mount_skills AS ms ON ms.id = nms.mount_skill_id
            INNER JOIN mount_attached_skills AS mas ON mas.mount_skill_id = ms.id
            ORDER BY nms.npc_id, mas.attach_point_id
            """;
        command.Prepare();

        using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
        {
            while (reader.Read())
                Add(reader.GetUInt32("npc_id"), reader.GetInt32("attach_point_id"));
        }

        command.CommandText = "SELECT DISTINCT npc_id FROM item_summon_mates";
        using (var mates = new SQLiteWrapperReader(command.ExecuteReader()))
        {
            while (mates.Read())
                Add(mates.GetUInt32("npc_id"), (int)AttachPointKind.Driver);
        }
    }

    public void LoadFromRows(IEnumerable<(uint TemplateId, int AttachPointId)> rows)
    {
        _seatsByNpc.Clear();
        foreach (var (templateId, attachPointId) in rows ?? [])
        {
            Add(templateId, attachPointId);
        }
    }

    public IReadOnlyList<AttachPointKind> GetSeats(uint npcId) =>
        _seatsByNpc.TryGetValue(npcId, out var seats)
            ? seats.OrderBy(point => (byte)point).ToArray()
            : Array.Empty<AttachPointKind>();

    public bool HasSeat(uint npcId, AttachPointKind attachPoint) =>
        _seatsByNpc.TryGetValue(npcId, out var seats) && seats.Contains(attachPoint);

    private void Add(uint npcId, int rawAttachPoint)
    {
        if (!SeatTopologyRules.TryNormalize(rawAttachPoint, out var attachPoint))
            return;

        if (!_seatsByNpc.TryGetValue(npcId, out var seats))
        {
            seats = [];
            _seatsByNpc[npcId] = seats;
        }

        seats.Add(attachPoint);
    }
}
