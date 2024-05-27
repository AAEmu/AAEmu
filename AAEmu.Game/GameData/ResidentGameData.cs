using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Residents;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class ResidentGameData : Singleton<ResidentGameData>, IGameDataLoader
{
    private Dictionary<uint, ResidentConditions> _residentConditions;
    private Dictionary<uint, LocalDevelopments> _localDevelopments;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _residentConditions = new Dictionary<uint, ResidentConditions>();
        _localDevelopments = new Dictionary<uint, LocalDevelopments>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM resident_conditions";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var template = new ResidentConditions();
                template.Id = reader.GetUInt32("id");
                template.CategoryId = reader.GetUInt32("category_id", 0);

                _residentConditions.Add(template.Id, template);
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM local_developments";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var template = new LocalDevelopments();
                template.Id = reader.GetUInt32("id");
                template.DoodadAlmightyId = reader.GetUInt32("doodad_almighty_id", 0);
                template.DoodadPhase0 = reader.GetUInt32("doodad_phase_0", 0);
                template.DoodadPhase1 = reader.GetUInt32("doodad_phase_1", 0);
                template.DoodadPhase2 = reader.GetUInt32("doodad_phase_2", 0);
                template.DoodadPhase3 = reader.GetUInt32("doodad_phase_3", 0);
                template.ZoneGroupId = reader.GetUInt16("zone_group_id");
                template.FactionId = reader.GetUInt32("faction_id", 0);

                _localDevelopments.Add(template.Id, template);
            }
        }
    }

    public void PostLoad()
    {
    }

    public ushort GetZoneGroupId(uint id)
    {
        foreach (var ld in _localDevelopments)
        {
            if (ld.Key == id)
            {
                return ld.Value.ZoneGroupId;
            }
        }
        return 0xffff; // -1
    }

    public uint GetDoodadPhase(uint id, byte DevelopmentStage)
    {
        var doodadPhase = 0u;
        foreach (var ld in _localDevelopments)
        {
            if (ld.Key != id) { continue; }

            doodadPhase = DevelopmentStage switch
            {
                0 => ld.Value.DoodadPhase0,
                1 => ld.Value.DoodadPhase1,
                2 => ld.Value.DoodadPhase2,
                3 => ld.Value.DoodadPhase3,
                _ => doodadPhase
            };
            break;
        }
        return doodadPhase;
    }
}
