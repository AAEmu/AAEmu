using System.Numerics;
using AAEmu.Commons.Utils.DB;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj;

/// <summary>
/// Phase/progress persistence for system doodads (permanent world fixtures spawned from the level's
/// doodad_spawns, e.g. the faction statues). Keyed by template and rounded spawn position.
/// </summary>
public static class WorldDoodadPhaseStore
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public static (int X, int Y) Key(Vector3 worldPosition) =>
        ((int)Math.Round(worldPosition.X), (int)Math.Round(worldPosition.Y));

    /// <summary>Whether this doodad's phase is kept in the store: a system doodad placed by a world spawner.</summary>
    public static bool Tracks(Doodad doodad) =>
        doodad is { IsPersistent: false, Spawner: not null, Template.SystemDoodad: true };

    public static bool Save(Doodad doodad)
    {
        if (!Tracks(doodad) || doodad.Transform == null)
            return true;

        var (x, y) = Key(doodad.Transform.World.Position);
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO world_doodad_phases (template_id, x, y, func_group_id, data)
                VALUES (@t, @x, @y, @g, @d)
                ON DUPLICATE KEY UPDATE func_group_id=@g, data=@d
                """;
            command.Parameters.AddWithValue("@t", doodad.TemplateId);
            command.Parameters.AddWithValue("@x", x);
            command.Parameters.AddWithValue("@y", y);
            command.Parameters.AddWithValue("@g", doodad.FuncGroupId);
            command.Parameters.AddWithValue("@d", doodad.Data);
            command.Prepare();
            command.ExecuteNonQuery();
            return true;
        }
        catch (MySqlException ex)
        {
            Logger.Warn("World doodad phase not saved for template {0} at ({1},{2}): {3}", doodad.TemplateId, x, y, ex.Message);
            return false;
        }
    }

    /// <summary>Saved (phase, data) for a fixture, or null when it has never progressed.</summary>
    public static (uint FuncGroupId, int Data)? Load(uint templateId, Vector3 worldPosition)
    {
        var (x, y) = Key(worldPosition);
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT func_group_id, data FROM world_doodad_phases WHERE template_id=@t AND x=@x AND y=@y";
            command.Parameters.AddWithValue("@t", templateId);
            command.Parameters.AddWithValue("@x", x);
            command.Parameters.AddWithValue("@y", y);
            command.Prepare();
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;
            return ((uint)reader.GetInt32(0), reader.GetInt32(1));
        }
        catch (MySqlException ex)
        {
            Logger.Warn("World doodad phase not loaded for template {0} at ({1},{2}): {3}", templateId, x, y, ex.Message);
            return null;
        }
    }
}
