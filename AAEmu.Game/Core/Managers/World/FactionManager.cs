using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class FactionManager(ILocalizationManager localizationManager) : Singleton<FactionManager>, IFactionManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded = false;

    private Dictionary<FactionsEnum, SystemFaction> _systemFactions = [];
    private List<FactionRelation> _relations = [];
    /// <summary>Content state under each hero agreement overlay; null when the pair had no content row.</summary>
    private readonly Dictionary<(uint, uint), RelationState?> _diplomacyBaseStates = [];

    public SystemFaction GetFaction(FactionsEnum id)
    {
        return _systemFactions.GetValueOrDefault(id);
    }

    /// <summary>All loaded system factions (Zone bring-online WZFactionList).</summary>
    public IReadOnlyList<SystemFaction> GetSystemFactions() =>
        _systemFactions?.Values.ToList() ?? [];

    /// <summary>Faction relations with both ids ≥ 100 (Zone WZFactionRelationList filter).</summary>
    public IReadOnlyList<FactionRelation> GetZoneRelations() =>
        _relations?.Where(r => (uint)r.Id >= 100 && (uint)r.Id2 >= 100).ToList() ?? [];

    public void AddFaction(SystemFaction faction)
    {
        _systemFactions.TryAdd(faction.Id, faction);
    }

    public void Load()
    {
        if (_loaded)
            return;

        _systemFactions = [];
        _relations = [];
        using (var connection = SQLite.CreateConnection())
        {
            Logger.Info("Loading system factions...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM system_factions";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var faction = new SystemFaction
                        {
                            Id = (FactionsEnum)reader.GetUInt32("id"),
                            Name = localizationManager.Get("system_factions", "name", reader.GetUInt32("id")),
                            OwnerName = reader.GetString("owner_name"),
                            UnitOwnerType = (sbyte)reader.GetInt16("owner_type_id"),
                            OwnerId = reader.GetUInt32("owner_id"),
                            PoliticalSystem = reader.GetByte("political_system_id"),
                            MotherId = (FactionsEnum)reader.GetUInt32("mother_id"),
                            AggroLink = reader.GetBoolean("aggro_link", true),
                            GuardHelp = reader.GetBoolean("guard_help", true),
                            DiplomacyTarget = reader.GetBoolean("is_diplomacy_tgt", true),
                            ShowCreateExpedition = reader.GetBoolean("show_create_expedition", true)
                        };
                        _systemFactions.Add(faction.Id, faction);
                    }
                }
            }

            Logger.Info($"Loaded {_systemFactions.Count} system factions");
            Logger.Info("Loading faction relations...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM system_faction_relations";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var relation = new FactionRelation
                        {
                            Id = (FactionsEnum)reader.GetUInt32("faction1_id"),
                            Id2 = (FactionsEnum)reader.GetUInt32("faction2_id"),
                            State = (RelationState)reader.GetByte("state_id")
                        };
                        _relations.Add(relation);

                        var faction = _systemFactions[relation.Id];
                        faction.Relations.Add(relation.Id2, relation);
                        faction = _systemFactions[relation.Id2];
                        faction.Relations.Add(relation.Id, relation);
                    }
                }
            }

            Logger.Info("Loaded {0} faction relations", _relations.Count);
        }

        _loaded = true;
    }

    /// <summary>Adds a relation row the way Load does: once to the list and to both factions' Relations.</summary>
    internal void AddRelation(FactionRelation relation)
    {
        if (relation == null)
            return;
        _relations.Add(relation);
        _systemFactions.GetValueOrDefault(relation.Id)?.Relations.TryAdd(relation.Id2, relation);
        _systemFactions.GetValueOrDefault(relation.Id2)?.Relations.TryAdd(relation.Id, relation);
    }

    /// <summary>The loaded row for a pair in either direction, or null.</summary>
    public FactionRelation GetRelation(FactionsEnum a, FactionsEnum b)
    {
        foreach (var relation in _relations)
        {
            if ((relation.Id == a && relation.Id2 == b) || (relation.Id == b && relation.Id2 == a))
                return relation;
        }

        return null;
    }

    /// <summary>
    /// Overlays a hero agreement on the loaded table. The same row object sits in both factions'
    /// Relations, so GetRelationState (combat, targeting, chat) and the login and zone lists all see
    /// the new state at once. A pair without a content row gets one that ClearDiplomacy removes.
    /// </summary>
    public FactionRelation ApplyDiplomacy(FactionDiplomacyAgreement agreement)
    {
        if (agreement == null || _relations == null)
            return null;

        var id = (FactionsEnum)agreement.Faction1;
        var id2 = (FactionsEnum)agreement.Faction2;
        var relation = GetRelation(id, id2);
        if (relation == null)
        {
            relation = new FactionRelation { Id = id, Id2 = id2, State = RelationState.Neutral };
            _relations.Add(relation);
            _systemFactions.GetValueOrDefault(id)?.Relations.TryAdd(id2, relation);
            _systemFactions.GetValueOrDefault(id2)?.Relations.TryAdd(id, relation);
            _diplomacyBaseStates[(agreement.Faction1, agreement.Faction2)] = null;
        }
        else if (!relation.HasDiplomacy)
        {
            _diplomacyBaseStates[(agreement.Faction1, agreement.Faction2)] = relation.State;
        }

        relation.State = agreement.State;
        relation.NextState = agreement.NextState;
        relation.UpdateTime = agreement.UpdateTime;
        relation.ChangeTime = agreement.ChangeTime;
        relation.UpdaterId = agreement.UpdaterId;
        relation.UpdaterName = agreement.UpdaterName ?? string.Empty;
        relation.ConfirmerId = agreement.ConfirmerId;
        relation.ConfirmerName = agreement.ConfirmerName ?? string.Empty;
        return relation;
    }

    /// <summary>Puts the content relation back; the row is removed again when the pair had none.</summary>
    public FactionRelation ClearDiplomacy(uint faction1, uint faction2)
    {
        var id = (FactionsEnum)faction1;
        var id2 = (FactionsEnum)faction2;
        var relation = GetRelation(id, id2);
        if (relation == null)
            return null;

        relation.ClearDiplomacy();
        if (_diplomacyBaseStates.Remove((faction1, faction2), out var baseState) && baseState == null)
        {
            _relations.Remove(relation);
            _systemFactions.GetValueOrDefault(id)?.Relations.Remove(id2);
            _systemFactions.GetValueOrDefault(id2)?.Relations.Remove(id);
            return relation;
        }

        if (baseState != null)
            relation.State = baseState.Value;
        return relation;
    }

    public void SendRelations(Character character)
    {
        if (_relations.Count == 0)
            character.SendPacket(new SCFactionRelationListPacket());
        else
        {
            var factions = _relations.ToArray();
            for (var i = 0; i < factions.Length; i += 200)
            {
                var temp = new FactionRelation[factions.Length - i <= 200 ? factions.Length - i : 200];
                Array.Copy(factions, i, temp, 0, temp.Length);
                character.SendPacket(new SCFactionRelationListPacket(temp));
            }
        }
    }
}
