using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The Exile System. Each route (faction_change_npc_spawners) has an exile broker in the origin's lands. A
/// route opens while its destination is an inferior faction and its origin is not, until this week's slots
/// (faction_change_limit_nums, by the activity gap) are taken. The brokers' quests end in
/// QuestActSupplyFactionChange, which moves the character, starts the faction_change_quest_cooldown and gives
/// the destination exile activity points. GMs can switch the system off, force routes open or shut, set
/// limits and take factions out of it.
/// </summary>
public class FactionExileManager : Singleton<FactionExileManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const string EnabledSetting = "enabled";
    private static int CooldownSeconds => ContentConfigGameData.Instance.GetInt("faction_change_quest_cooldown", 5184000);

    private readonly object _sync = new();
    private readonly List<ExileRoute> _routes = [];
    private readonly HashSet<uint> _suspended = [];
    private readonly Dictionary<uint, (uint Original, DateTime At)> _lastExile = [];
    private bool _enabled = true;
    private uint _week;
    private int _started;

    public bool IsStarted => Volatile.Read(ref _started) != 0;

    public bool Enabled { get { lock (_sync) return _enabled; } }

    public IReadOnlyCollection<uint> Suspended { get { lock (_sync) return _suspended.ToList(); } }

    /// <summary>
    /// Whether a root faction takes part in exile at all. Only Nuia, Haranya and the Outlaws do. This is the
    /// one gate to widen for other factions.
    /// </summary>
    public static bool IsExileFaction(uint factionId) => FactionPowerManager.PowerFactions().Contains(factionId);

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        try
        {
            lock (_sync)
            {
                _week = FactionPowerManager.Instance.Week;
                foreach (var template in FactionPowerGameData.Instance.ExileRoutes)
                {
                    _routes.Add(new ExileRoute
                    {
                        FromFactionId = template.FromFactionId,
                        ToFactionId = template.ToFactionId,
                        NpcSpawnerId = template.NpcSpawnerId
                    });
                }

                LoadState();
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Exile: could not load the exile state, starting with defaults");
        }

        FactionPowerManager.Instance.StandingsChanged += Refresh;
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMinutes(1));
        Refresh();
        Logger.Info("Exile: {0} route(s), {1}", _routes.Count, _enabled ? "enabled" : "disabled");
    }

    #region state

    private ExileRoute FindRouteNoLock(uint from, uint to) =>
        _routes.FirstOrDefault(r => r.FromFactionId == from && r.ToFactionId == to);

    private int MaxNoLock(ExileRoute route)
    {
        var totals = FactionPowerManager.Instance.PreviousScores().ToDictionary(s => s.FactionId, FactionPowerRules.Total);
        return FactionExileRules.MaxFor(route, FactionPowerGameData.Instance.ChangeLimits,
            totals.GetValueOrDefault(route.FromFactionId), totals.GetValueOrDefault(route.ToFactionId));
    }

    private bool IsActiveNoLock(ExileRoute route) =>
        FactionExileRules.IsActive(route, MaxNoLock(route), _enabled, _suspended, FactionPowerManager.Instance.IsInferior);

    public sealed record RouteInfo(uint From, uint To, uint SpawnerId, ExileRouteMode Mode, int? MaxOverride, int Max, uint Used, bool Active);

    public List<RouteInfo> Routes()
    {
        lock (_sync)
        {
            return _routes.Select(r =>
            {
                var max = MaxNoLock(r);
                return new RouteInfo(r.FromFactionId, r.ToFactionId, r.NpcSpawnerId, r.Mode, r.MaxOverride, max, r.Used, IsActiveNoLock(r));
            }).ToList();
        }
    }

    /// <summary>Re-evaluates every route: brokers of shut routes are kept away and clients get the slots.</summary>
    public void Refresh()
    {
        if (!IsStarted)
            return;

        List<uint> closedSpawners;
        lock (_sync)
            closedSpawners = _routes.Where(r => !IsActiveNoLock(r)).Select(r => r.NpcSpawnerId).Distinct().ToList();

        WorldIntegration.ApplyClosedGameSpawners?.Invoke(closedSpawners);
        WorldManager.Instance.BroadcastPacketToServer(BuildListPacket());
    }

    public SCFactionImmigrateLimitListPacket BuildListPacket()
    {
        lock (_sync)
        {
            var limits = _routes.Select(r =>
            {
                // A shut route shows no slots left, which the client draws as "not accepting".
                var max = IsActiveNoLock(r) ? (uint)MaxNoLock(r) : r.Used;
                return new FactionImmigrateLimit(r.ToFactionId, r.FromFactionId, max, r.Used, 0);
            }).ToList();
            return new SCFactionImmigrateLimitListPacket(_week, limits);
        }
    }

    public void OnLogin(Character character)
    {
        if (IsStarted && character != null)
            character.SendPacket(BuildListPacket());
    }

    private void Tick(TimeSpan delta)
    {
        var week = FactionPowerManager.Instance.Week;
        lock (_sync)
        {
            if (week == _week)
                return;
            _week = week;
            foreach (var route in _routes)
                route.Used = 0;
        }

        Logger.Info("Exile: week {0} started, slots reset", week);
        Refresh();
    }

    #endregion

    #region requirement checks

    /// <summary>Unit requirement 123: value2 1 = the faction is inferior, 0 = superior, 2 = neither.</summary>
    public static bool CheckFactionPower(uint factionId, uint standing)
    {
        var power = FactionPowerManager.Instance;
        return standing switch
        {
            0 => power.IsSuperior(factionId),
            1 => power.IsInferior(factionId),
            _ => !power.IsSuperior(factionId) && !power.IsInferior(factionId)
        };
    }

    /// <summary>Unit requirement 124: the route has slots and is open.</summary>
    public bool CheckRouteOpen(uint from, uint to)
    {
        lock (_sync)
        {
            var route = FindRouteNoLock(from, to);
            return route != null && IsActiveNoLock(route);
        }
    }

    public DateTime? LastExile(uint characterId)
    {
        lock (_sync)
            return _lastExile.TryGetValue(characterId, out var last) ? last.At : null;
    }

    /// <summary>Unit requirement 125: the faction change cooldown has run out.</summary>
    public bool CheckCooldown(Character character) =>
        character != null && FactionExileRules.CooldownOver(LastExile(character.Id), CooldownSeconds, DateTime.UtcNow);

    public DateTime? CooldownEnds(uint characterId) => LastExile(characterId)?.AddSeconds(CooldownSeconds);

    /// <summary>Why a character may not exile on their own yet (guild or family membership), or null.</summary>
    public static string LeaveRuleBlock(Character character)
    {
        if (character?.Expedition != null)
            return "leave your guild before you exile";
        if (character?.Family > 0)
            return "leave your family before you exile";
        return null;
    }

    /// <summary>
    /// What keeps a character from starting an exile quest of their own (cooldown, guild or family), told
    /// as a chat line because the client has no message for these requirements; null if nothing does.
    /// </summary>
    public string ExileQuestBlock(Character character)
    {
        if (character == null)
            return null;
        if (!CheckCooldown(character) && CooldownEnds(character.Id) is { } ends)
            return $"You may change faction again after {ends:yyyy-MM-dd HH:mm} UTC.";
        return LeaveRuleBlock(character) is { } leave ? $"You must {leave}." : null;
    }

    /// <summary>A quest whose faction change honours the cooldown and the guild/family rule.</summary>
    public static bool IsLimitedExileQuest(QuestTemplate template) =>
        template?.Components.Values.Any(component =>
            component.ActTemplates.Any(act => act is QuestActSupplyFactionChange { IgnoreLimit: false })) == true;

    #endregion

    #region exile

    /// <summary>
    /// Moves a character into a faction (a system faction such as 161, 199, 200 or 148; 0 = back to the faction it
    /// had before its first exile). Quest rewards respect the route, its slots and the cooldown unless
    /// <paramref name="ignoreLimit"/>; <paramref name="inferiorEscape"/> lets a member of an inferior faction
    /// leave on a full route. <paramref name="force"/> is the GM path: no route, slot or cooldown checks and
    /// no slot used.
    /// </summary>
    public string Exile(Character character, uint targetFactionId, bool ignoreLimit, bool inferiorEscape, bool force)
    {
        if (character == null)
            return "no character";
        if (!IsStarted)
            return "the exile system is not running";
        if (character.IsTempFaction)
            return $"{character.Name} is in a temporary faction, which is not part of the exile system";

        uint target;
        lock (_sync)
        {
            target = targetFactionId != 0
                ? targetFactionId
                : _lastExile.TryGetValue(character.Id, out var last) ? last.Original : 0;
        }

        var targetFaction = FactionManager.Instance.GetFaction((FactionsEnum)target);
        if (targetFaction == null)
            return targetFactionId == 0 ? $"{character.Name} has no faction to return to" : $"unknown faction {target}";

        var from = FactionPowerManager.RootFactionOf(character);
        var to = RootOf(targetFaction);
        if (from == to && character.Faction?.Id == targetFaction.Id)
            return $"{character.Name} is already in {targetFaction.Name}";
        if (!force && (!IsExileFaction(from) || !IsExileFaction(to)))
            return "both factions must take part in the exile system";

        if (!force && !ignoreLimit && !CheckCooldown(character))
            return $"faction change cooldown runs until {CooldownEnds(character.Id):yyyy-MM-dd HH:mm} UTC";
        if (!force && LeaveRuleBlock(character) is { } blocked)
            return blocked;
        if (character.Expedition != null && character.Expedition.OwnerId == character.Id)
            return "a guild leader must hand over the guild first";

        ExileRoute route = null;
        var now = DateTime.UtcNow;
        var originalFaction = (uint)(character.Faction?.Id ?? 0);
        uint original;
        DateTime at;
        lock (_sync)
        {
            if (!force)
            {
                route = FindRouteNoLock(from, to);
                if (route == null && from != to)
                    return $"there is no exile route from {from} to {to}";
                var escaping = inferiorEscape && FactionPowerManager.Instance.IsInferior(from);
                if (route != null && !ignoreLimit && !escaping && !IsActiveNoLock(route))
                    return "this exile route is closed";
                if (_suspended.Contains(from) || _suspended.Contains(to))
                    return "one of the factions is suspended from exile";
                // The slot is taken before the lock is released so two exiles cannot share the last one.
                if (route != null)
                    route.Used++;
            }

            var known = _lastExile.TryGetValue(character.Id, out var previous);
            original = known ? previous.Original : originalFaction;
            // A GM move does not start the character's own faction change cooldown.
            at = !force ? now : known ? previous.At : now.AddSeconds(-CooldownSeconds);
        }

        // Held from the commit until the character carries the new faction, so a World save in between cannot
        // write the old faction back over it.
        using (PersistenceOperationScope.Enter())
        {
            if (!SaveExile(character.Id, (uint)targetFaction.Id, original, from, at, route))
            {
                lock (_sync)
                {
                    if (route is { Used: > 0 })
                        route.Used--;
                }

                return "the faction change could not be saved, nothing was changed";
            }

            character.SetFaction(targetFaction.Id);
        }

        lock (_sync)
            _lastExile[character.Id] = (original, at);

        if (character.Expedition != null)
            ExpeditionManager.Instance.Leave(character);
        if (character.Family > 0)
            FamilyManager.Instance.LeaveFamily(character);

        RaidRecruitmentManager.Instance.OnFactionChanged(character);
        ChatManager.Instance.SyncFactionChannel(character);
        HousingManager.Instance.UpdateOwnedHousingFaction(character.Id, targetFaction.Id);
        foreach (var doodad in character.ParentWorld?.SpawnManager?.GetPlayerDoodads(character.Id) ?? [])
            DoodadManager.Instance.RefreshFaction(doodad, character, doodad.ParentObj as Models.Game.Housing.House);
        FactionPowerManager.Instance.ApplyInferiorBuff(character);

        if (!force)
            FactionPowerManager.Instance.AddExile(to);
        Refresh();
        Logger.Info("Exile: {0} moved from {1} to {2} ({3}){4}", character.Name, originalFaction, targetFaction.Id,
            to, force ? " by a GM" : string.Empty);
        return $"{character.Name} is now in faction {(uint)targetFaction.Id}{(string.IsNullOrEmpty(targetFaction.Name) ? "" : $" ({targetFaction.Name})")}";
    }

    private static uint RootOf(SystemFaction faction)
    {
        var root = faction;
        for (var depth = 0; depth < 8 && root.MotherId != 0; depth++)
        {
            var mother = FactionManager.Instance.GetFaction(root.MotherId);
            if (mother == null)
                break;
            root = mother;
        }

        return (uint)root.Id;
    }

    #endregion

    #region GM

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            _enabled = enabled;
            SaveSetting(EnabledSetting, enabled ? 1 : 0);
        }

        Refresh();
    }

    public bool SetMode(uint from, uint to, ExileRouteMode mode)
    {
        lock (_sync)
        {
            var route = FindRouteNoLock(from, to);
            if (route == null)
                return false;
            route.Mode = mode;
            SaveRoute(route);
        }

        Refresh();
        return true;
    }

    public bool SetMax(uint from, uint to, int? max)
    {
        lock (_sync)
        {
            var route = FindRouteNoLock(from, to);
            if (route == null)
                return false;
            route.MaxOverride = max;
            SaveRoute(route);
        }

        Refresh();
        return true;
    }

    public bool SetSuspended(uint factionId, bool suspended)
    {
        if (!IsExileFaction(factionId))
            return false;
        lock (_sync)
        {
            if (suspended)
                _suspended.Add(factionId);
            else
                _suspended.Remove(factionId);
            SaveSuspensions();
        }

        Refresh();
        return true;
    }

    /// <summary>Gives back this week's slots: one route, or every route when both ids are 0.</summary>
    public void ResetUsed(uint from, uint to)
    {
        lock (_sync)
        {
            foreach (var route in _routes.Where(r => (from == 0 && to == 0) || (r.FromFactionId == from && r.ToFactionId == to)))
            {
                route.Used = 0;
                SaveCount(route);
            }
        }

        Refresh();
    }

    public bool ClearCooldown(uint characterId)
    {
        lock (_sync)
        {
            if (!_lastExile.TryGetValue(characterId, out var last))
                return false;
            _lastExile[characterId] = (last.Original, DateTime.MinValue);
            SaveCharacterTime(characterId, DateTime.UnixEpoch);
        }

        return true;
    }

    #endregion

    #region persistence

    private void LoadState()
    {
        using var connection = MySQL.CreateConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name, value FROM faction_exile_settings";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString("name") == EnabledSetting)
                    _enabled = reader.GetInt32("value") != 0;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM faction_exile_routes";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var route = FindRouteNoLock(reader.GetUInt32("from_faction"), reader.GetUInt32("to_faction"));
                if (route == null)
                    continue;
                route.Mode = (ExileRouteMode)reader.GetByte("mode");
                var maxOrdinal = reader.GetOrdinal("max_override");
                route.MaxOverride = reader.IsDBNull(maxOrdinal) ? null : reader.GetInt32(maxOrdinal);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM faction_exile_counts WHERE week_num = @week";
            command.Parameters.AddWithValue("@week", _week);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var route = FindRouteNoLock(reader.GetUInt32("from_faction"), reader.GetUInt32("to_faction"));
                if (route != null)
                    route.Used = reader.GetUInt32("used");
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT faction_id FROM faction_exile_suspensions";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                _suspended.Add(reader.GetUInt32("faction_id"));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT character_id, original_faction_id, exiled_at FROM character_exiles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                _lastExile[reader.GetUInt32("character_id")] =
                    (reader.GetUInt32("original_faction_id"), DateTime.SpecifyKind(reader.GetDateTime("exiled_at"), DateTimeKind.Utc));
            }
        }
    }

    private static void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Exile: saving failed ({0})", sql.Split(' ').Take(3).Aggregate((a, b) => a + " " + b));
        }
    }

    private static void SaveSetting(string name, int value) =>
        Execute("REPLACE INTO faction_exile_settings (name, value) VALUES (@n, @v)", ("@n", name), ("@v", value));

    private static void SaveRoute(ExileRoute route) =>
        Execute("REPLACE INTO faction_exile_routes (from_faction, to_faction, mode, max_override) VALUES (@f, @t, @m, @x)",
            ("@f", route.FromFactionId), ("@t", route.ToFactionId), ("@m", (byte)route.Mode), ("@x", route.MaxOverride));

    private void SaveCount(ExileRoute route) =>
        Execute("REPLACE INTO faction_exile_counts (week_num, from_faction, to_faction, used) VALUES (@w, @f, @t, @u)",
            ("@w", _week), ("@f", route.FromFactionId), ("@t", route.ToFactionId), ("@u", route.Used));

    private void SaveSuspensions()
    {
        Execute("DELETE FROM faction_exile_suspensions");
        foreach (var factionId in _suspended)
            Execute("INSERT INTO faction_exile_suspensions (faction_id) VALUES (@f)", ("@f", factionId));
    }

    /// <summary>The faction, the exile record and the route's used slots in one transaction.</summary>
    private bool SaveExile(uint characterId, uint factionId, uint original, uint from, DateTime at, ExileRoute route)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var transaction = connection.BeginTransaction();
            void Run(string sql, params (string Name, object Value)[] parameters)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in parameters)
                    command.Parameters.AddWithValue(name, value);
                command.ExecuteNonQuery();
            }

            Run("UPDATE characters SET faction_id = @f WHERE id = @c", ("@f", factionId), ("@c", characterId));
            Run("""
                REPLACE INTO character_exiles (character_id, original_faction_id, from_faction_id, to_faction_id, exiled_at)
                VALUES (@c, @o, @f, @t, @a)
                """, ("@c", characterId), ("@o", original), ("@f", from), ("@t", factionId), ("@a", at));
            if (route != null)
            {
                uint week;
                lock (_sync)
                    week = _week;
                // One committed increment per exile; the in-memory count also holds reservations still in flight.
                Run("""
                    INSERT INTO faction_exile_counts (week_num, from_faction, to_faction, used) VALUES (@w, @f, @t, 1)
                    ON DUPLICATE KEY UPDATE used = used + 1
                    """, ("@w", week), ("@f", route.FromFactionId), ("@t", route.ToFactionId));
            }

            transaction.Commit();
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Exile: saving the faction change of character {0} failed", characterId);
            return false;
        }
    }

    private static void SaveCharacterTime(uint characterId, DateTime at) =>
        Execute("UPDATE character_exiles SET exiled_at = @a WHERE character_id = @c", ("@c", characterId), ("@a", at));

    #endregion
}
