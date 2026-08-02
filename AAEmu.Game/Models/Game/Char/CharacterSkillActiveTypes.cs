using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Heirs;
using AAEmu.Game.Models.Game.Skills;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Per-character overrides for the client's content-created skill active-type pairs. Heir pairs
/// use <c>(heir_skills.id, heir_skill_details.skill_id)</c>; generic pairs use a zero Heir key.
/// </summary>
public sealed class CharacterSkillActiveTypes(Character owner)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly object _sync = new();
    private readonly Dictionary<(uint HeirSkillType, uint SkillType), SkillActiveType> _states = [];

    private Character Owner { get; } = owner;

    public SkillActiveType GetHeirState(uint heirSkillId, HeirSkillDetail detail)
    {
        lock (_sync)
        {
            return _states.GetValueOrDefault(
                (heirSkillId, detail.SkillId),
                detail.SkillActiveTypeId);
        }
    }

    /// <summary>Validates, persists in memory, and notifies the client about one state transition.</summary>
    public bool TrySet(uint heirSkillType, uint skillType, SkillActiveType activeType, bool notifyClient = true)
    {
        if (!Enum.IsDefined(activeType) || !IsValidPair(heirSkillType, skillType))
            return false;

        lock (_sync)
        {
            var key = (heirSkillType, skillType);
            if (!_states.ContainsKey(key) && _states.Count >= SCListSkillActiveTypePacket.MaxEntries)
                return false;

            if (!TryPersistState(heirSkillType, skillType, activeType))
                return false;

            _states[key] = activeType;

            if (!notifyClient)
                return true;

            var entry = ToPacketEntry(heirSkillType, skillType, activeType);
            if (activeType == SkillActiveType.Unlock)
            {
                // Live unlocks use this dedicated success reply. The persisted type-4 list entry
                // restores the same unlock set on reconnect.
                Owner.SendPacket(new SCUnlockLearnSkillPacket(0, checked((int)skillType)));
            }
            else
            {
                Owner.SendPacket(new SCUpdateSkillActiveTypePacket(entry));
            }

            return true;
        }
    }

    public IReadOnlyList<SkillActiveTypeEntry> BuildPacketEntries()
    {
        lock (_sync)
        {
            return _states
                .OrderBy(pair => pair.Key.HeirSkillType)
                .ThenBy(pair => pair.Key.SkillType)
                .Select(pair => ToPacketEntry(pair.Key.HeirSkillType, pair.Key.SkillType, pair.Value))
                .ToArray();
        }
    }

    public void Load(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT heir_skill_type, skill_type, active_type " +
            "FROM character_skill_active_types WHERE owner = @owner";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var heirSkillType = reader.GetUInt32("heir_skill_type");
            var skillType = reader.GetUInt32("skill_type");
            var activeTypeValue = reader.GetByte("active_type");
            var activeType = (SkillActiveType)activeTypeValue;
            if (!Enum.IsDefined(activeType) || !IsValidPair(heirSkillType, skillType))
            {
                Logger.Warn(
                    "Ignoring invalid skill active type for {0}: heir={1}, skill={2}, active={3}",
                    Owner.Name, heirSkillType, skillType, activeTypeValue);
                continue;
            }

            if (_states.Count >= SCListSkillActiveTypePacket.MaxEntries)
            {
                Logger.Warn(
                    "Ignoring skill active types above native limit {0} for {1}",
                    SCListSkillActiveTypePacket.MaxEntries, Owner.Name);
                break;
            }

            _states[(heirSkillType, skillType)] = activeType;
        }
    }

    private static SkillActiveTypeEntry ToPacketEntry(
        uint heirSkillType,
        uint skillType,
        SkillActiveType activeType) =>
        new(checked((int)heirSkillType), checked((int)skillType), (byte)activeType);

    private bool TryPersistState(uint heirSkillType, uint skillType, SkillActiveType activeType)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO character_skill_active_types(owner, heir_skill_type, skill_type, active_type) " +
                "VALUES (@owner, @heirSkillType, @skillType, @activeType) " +
                "ON DUPLICATE KEY UPDATE active_type = VALUES(active_type)";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Parameters.AddWithValue("@heirSkillType", heirSkillType);
            command.Parameters.AddWithValue("@skillType", skillType);
            command.Parameters.AddWithValue("@activeType", (byte)activeType);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                "Failed to persist skill active type for {0}: heir={1}, skill={2}, active={3}",
                Owner.Name,
                heirSkillType,
                skillType,
                (byte)activeType);
            return false;
        }
    }

    private static bool IsValidPair(uint heirSkillType, uint skillType)
    {
        if (skillType == 0)
            return false;

        if (heirSkillType == 0)
            return SkillManager.Instance.GetSkillTemplate(skillType) != null;

        return HeirGameData.Instance.TryGetHeirSkillForSuccessor(skillType, out var heirSkill, out _) &&
               heirSkill.Id == heirSkillType;
    }
}
