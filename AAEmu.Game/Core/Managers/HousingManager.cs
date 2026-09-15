using System.Drawing;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Dominions;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Taxations;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Housing;
using AAEmu.Game.Utils;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class HousingManager(
    IObjectIdManager objectIdManager,
    IFactionManager factionManager,
    ILocalizationManager localizationManager,
    IWorldManager worldManager,
    ITaskManager taskManager,
    ISkillManager skillManager,
    IHousingIdManager housingIdManager,
    IHousingTldManager housingTldManager,
    IItemManager itemManager,
    IMailManager mailManager,
    INameManager nameManager,
    IZoneManager zoneManager,
    IDoodadManager doodadManager,
    IUccManager uccManager,
    IButlerManager butlerManager,
    IDominionManager dominionManager,
    IGuildDominionManager guildDominionManager) : Singleton<HousingManager>, IHousingManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private const uint ForSaleMarkerDoodadId = 6760;
    private const int HoursForFailedTaxToReturnHouse = 22;

    /// <summary>The wreck stays standing this long after the removal debuff finishes the house.</summary>
    private const int SecondsForDemolitionWreck = 20;

    /// <summary>Wrecked houses (id -> when the wreck appeared). Drives the shell removal tick.</summary>
    private readonly Dictionary<uint, DateTime> _wreckedHouses = [];
    private const int MaxPrepaidWeeks = 5; // client MAX_PREPAID_WEEKS; prepay at or above this is refused
    private Dictionary<uint, House> _houses = [];
    private Dictionary<ushort, House> _housesTl = []; // TODO or so mb tlId is id in the active zone? or type of house
    private List<uint> _removedHousings = [];
    private bool _isCheckingTaxTiming;

    /// <summary>
    /// Gets all houses for a given Account
    /// </summary>
    /// <param name="values"></param>
    /// <param name="accountId"></param>
    /// <returns></returns>
    public int GetByAccountId(Dictionary<uint, House> values, uint accountId)
    {
        foreach (var (id, house) in _houses)
            if (house.AccountId == accountId)
                values.Add(id, house);
        return values.Count;
    }

    /// <summary>
    /// Gets all houses owned by Character
    /// </summary>
    /// <param name="values"></param>
    /// <param name="characterId"></param>
    /// <returns></returns>
    public int GetByCharacterId(Dictionary<uint, House> values, uint characterId)
    {
        foreach (var (id, house) in _houses)
            if (house.OwnerId == characterId)
                values.Add(id, house);
        return values.Count;
    }

    /// <summary>
    /// Creates House and set it's untouchable buff
    /// </summary>
    /// <param name="templateId"></param>
    /// <param name="factionId"></param>
    /// <param name="worldInstance"></param>
    /// <param name="objectId"></param>
    /// <param name="tlId"></param>
    /// <returns></returns>
    private House Create(uint templateId, FactionsEnum factionId, WorldInstance worldInstance, uint objectId = 0, ushort tlId = 0)
    {
        var template = HousingGameData.Instance.GetTemplate(templateId);
        if (template == null)
            return null;

        var house = new House
        {
            TlId = tlId > 0 ? tlId : (ushort)housingTldManager.GetNextId(),
            ObjId = objectId > 0 ? objectId : objectIdManager.GetNextId(),
            Template = template,
            TemplateId = template.Id, // duplicate Id
            Id = template.Id,
            Faction = factionManager.GetFaction(factionId),
            Name = localizationManager.Get("housings", "name", template.Id),
            Transform = { InstanceId = worldInstance.Id }
        };
        house.Hp = house.MaxHp;
        // Force public on always public properties on create
        if (template.AlwaysPublic)
            house.Permission = HousingPermission.Public;

        SetUntouchable(house, true);

        return house;
    }

    /// <summary>
    /// Creates the House for a Dominion claim on first declare - i.e. the very first time a zone group's Guard
    /// Tower is built, when only a native, always-present "정화의 수호탑 소환지점" doodad exists (baked into the
    /// CryEngine level, not tracked by AAEmu at all - no `doodad_spawners` table exists for it) and no House row
    /// has ever been created for that zone group yet. Mirrors <see cref="Build"/>'s house-creation tail, minus
    /// the design-item consumption and tax prepayment (DeclareDominion already consumes its own backpack item,
    /// and dominion tax is DominionManager's separate system, not personal housing tax).
    ///
    /// Known limitation, not solved here: <paramref name="declarer"/> becomes the House's OwnerId/CoOwnerId,
    /// same as any personal house - there is no guild/Expedition-owned House concept anywhere in this codebase.
    /// The user wants Dominion walls/gates owned by the guild, not the individual who placed them; that needs
    /// its own design pass (does OwnerId need a guild-id variant, or do wall/gate doodads need permission checks
    /// routed through Expedition membership instead of OwnerId directly - not decided yet).
    ///
    /// Also not verified: whether the generic HousingTaxTask (which iterates ALL houses in `_houses`) could try
    /// to apply personal-house tax/demolish-on-nonpayment logic to this Guard Tower, unaware DominionManager
    /// already handles its tax separately - worth checking before relying on this in a long-running server.
    /// </summary>
    public House CreateDominionHouse(uint templateId, Character declarer, WorldInstance world, float x, float y, float z)
    {
        var house = Create(templateId, declarer.Faction.Id, world);
        if (house == null)
            return null;

        house.Id = housingIdManager.GetNextId();
        house.Transform.Local.SetPosition(x, y, z);
        house.CurrentStep = house.Template.BuildSteps.Count > 0 ? 0 : -1;
        house.OwnerId = declarer.Id;
        house.CoOwnerId = declarer.Id;
        house.AccountId = declarer.AccountId;
        house.AllowRecover = true;
        house.PlaceDate = DateTime.UtcNow;
        house.ProtectionEndDate = DateTime.UtcNow.AddDays(AppConfiguration.Instance.World.DaysForTaxPayment);
        _houses.Add(house.Id, house);
        _housesTl.Add(house.TlId, house);
        declarer.SendPacket(new SCHouseDataPacket([house]));
        house.Spawn();
        if (WorldIntegration.ZoneAuthority)
            HousingZoneBridge.NotifyZoneHouseCreated(house);
        UpdateTaxInfo(house);

        return house;
    }

    /// <summary>
    /// Load housing definitions, player houses and starts tax check timer
    /// </summary>
    /// <exception cref="IOException"></exception>
    public void LoadPlayerHousing(WorldInstance worldInstance)
    {
        _houses = [];
        _housesTl = [];
        _removedHousings = [];

        worldInstance ??= worldManager.GetWorld(WorldManager.DefaultInstanceId);

        // var housingAreas = new Dictionary<uint, HousingAreas>();
        // var houseTaxes = new Dictionary<uint, HouseTax>();

        Logger.Info("Loading Player Buildings ...");
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.CommandText = "SELECT * FROM housings";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var templateId = reader.GetUInt32("template_id");
                        var factionId = (FactionsEnum)reader.GetUInt32("faction_id");
                        var house = Create(templateId, factionId, worldInstance);
                        house.ParentWorld = worldInstance;
                        house.Id = reader.GetUInt32("id");
                        house.AccountId = reader.GetUInt32("account_id");
                        house.OwnerId = reader.GetUInt32("owner");
                        house.CoOwnerId = reader.GetUInt32("co_owner");
                        house.Name = reader.GetString("name");
                        house.Transform = new Transform(house, null,
                            new Vector3(reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z")),
                            new Vector3(reader.GetFloat("roll"), reader.GetFloat("pitch"), reader.GetFloat("yaw"))
                        );
                        house.Transform.InstanceId = house.ParentWorld.Id; // Just to be sure
                        house.Transform.ZoneId = worldManager.GetZoneId(house.ParentWorld.Template, house.Transform.World.Position.X, house.Transform.World.Position.Y);
                        house.IsBeingLoadedFromDb = AppConfiguration.Instance.World.UsePersistentHouseDoodads;
                        try
                        {
                            house.CurrentStep = reader.GetInt32("current_step");
                        }
                        finally
                        {
                            house.IsBeingLoadedFromDb = false;
                        }
                        house.NumAction = reader.GetInt32("current_action");
                        house.Permission = (HousingPermission)reader.GetByte("permission");
                        house.PlaceDate = reader.GetDateTime("place_date");
                        house.ProtectionEndDate = reader.GetDateTime("protected_until");
                        house.SellToPlayerId = reader.GetUInt32("sell_to");
                        house.SellPrice = reader.GetUInt32("sell_price");
                        house.AllowRecover = reader.GetBoolean("allow_recover");
                        try { house.SellPublic = reader.GetBoolean(reader.GetOrdinal("sell_public")); } catch { house.SellPublic = true; }
                        _houses.Add(house.Id, house);
                        _housesTl.Add(house.TlId, house);

                        // Manually placed houses (or after upgrading MySQL), will get 2 weeks for free as to not immediately trigger them into demolition
                        if (house.PlaceDate == house.ProtectionEndDate)
                            house.ProtectionEndDate = house.PlaceDate.AddDays(14);

                        UpdateTaxInfo(house);
                        // Expired houses are demolished by CleanupExpiredHouses after the furniture and
                        // bound doodads have been spawned (see SpawnManager) - their contents cannot be
                        // returned before that.
                        house.IsDirty = false;
                    }
                }
            }
        }

        Logger.Info($"Loaded {_houses.Count} Player Buildings");
        ApplyAuthoredLodestonePlacements();

        var houseCheckTask = new HousingTaxTask();
        taskManager.Schedule(houseCheckTask, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));

        Logger.Info("Started Housing Tax Timer");
    }

    /// <summary>
    /// Runs the demolition lifecycle for houses whose protection expired while the server was down.
    /// This must run AFTER the housing furniture and bound doodads are spawned, otherwise the
    /// contents cannot be returned to the owner (the doodads do not exist yet during LoadPlayerHousing).
    /// </summary>
    public void CleanupExpiredHouses()
    {
        foreach (var house in _houses.Values.ToList())
        {
            if (house == null || house.OwnerId <= 0)
                continue;
            if (house.ProtectionEndDate > DateTime.UtcNow || _wreckedHouses.ContainsKey(house.Id))
                continue;

            // Normal lifecycle first - returns the contents, clears the tax mail and ownership,
            // sends the notices - then the wreck shell removes the house.
            Demolish(null, house, true, false);
            _wreckedHouses[house.Id] = DateTime.UtcNow;
            SetHouseHp(house, 0);
        }
    }

    /// <summary>
    /// Unowned lodestones take XYZ/yaw from <c>houses/{zoneKey}/house.g</c> (<c>removed false</c> only).
    /// </summary>
    private void ApplyAuthoredLodestonePlacements()
    {
        var live = HouseGPlacementCatalog.IndexLiveByDesign(
            HouseGPlacementCatalog.LoadFromRoots(EnumerateZoneGameDataRoots()));
        if (live.Count == 0)
            return;

        var snapped = 0;
        foreach (var house in _houses.Values)
        {
            if (house == null ||
                !SiegeGameData.Instance.IsLodestoneTemplate(house.TemplateId) ||
                !live.TryGetValue(house.TemplateId, out var place))
                continue;
            if (!HouseGPlacement.ShouldApplyToUnownedLodestone(
                    true, house.OwnerId, house.AccountId, place.Removed))
                continue;

            var pos = house.Transform.World.Position;
            var yaw = house.Transform.Local.Rotation.Z;
            if (Math.Abs(pos.X - place.X) < 0.05f &&
                Math.Abs(pos.Y - place.Y) < 0.05f &&
                Math.Abs(pos.Z - place.Z) < 0.05f &&
                Math.Abs(yaw - place.Yaw) < 0.01f)
                continue;

            house.Transform.Local.SetPosition(place.X, place.Y, place.Z);
            house.Transform.Local.SetRotation(0, 0, place.Yaw);
            house.Transform.ZoneId = worldManager.GetZoneId(
                house.ParentWorld.Template, place.X, place.Y);
            house.IsDirty = true;
            snapped++;
            Logger.Info(
                "Lodestone house {0} design {1} snapped to house.g ({2:0.###},{3:0.###},{4:0.###})",
                house.Id, house.TemplateId, place.X, place.Y, place.Z);
        }

        if (snapped > 0)
            Logger.Info("Applied {0} house.g lodestone placement(s)", snapped);
    }

    private static IEnumerable<string> EnumerateZoneGameDataRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Offer(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;
            try
            {
                var full = Path.GetFullPath(candidate.Trim());
                if (Directory.Exists(full))
                    seen.Add(full);
            }
            catch (Exception)
            {
                // bad path
            }
        }

        Offer(Environment.GetEnvironmentVariable("AAEMU_ZONE_GAME_DATA_ROOT"));
        Offer(AppConfiguration.Instance.ZoneGameDataRoot);
        return seen;
    }

    /// <summary>
    /// Saves player housing information
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="transaction"></param>
    /// <returns></returns>
    public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var deleteCount = 0;
        lock (_removedHousings)
        {
            if (_removedHousings.Count > 0)
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        $"DELETE FROM housings WHERE id IN({string.Join(",", _removedHousings)})";
                    command.Prepare();
                    command.ExecuteNonQuery();
                    deleteCount++;
                }

                _removedHousings.Clear();
            }
        }

        var updateCount = 0;
        foreach (var house in _houses.Values)
            if (house.Save(connection, transaction))
                updateCount++;

        return (updateCount, deleteCount);
    }

    /// <summary>
    /// Spawn all houses
    /// </summary>
    public void SpawnAll()
    {
        foreach (var house in _houses.Values)
        {
            // Override instanceId to always be "main_world" instance
            house.Transform.InstanceId = WorldManager.DefaultInstanceId;
            house.Spawn();
            if (WorldIntegration.ZoneAuthority)
                HousingZoneBridge.NotifyZoneHouseCreated(house);
        }
    }

    /// <summary>
    /// Replays housing state after a Zone joins or reconnects. Houses are already spawned in
    /// the Zone that just loaded.
    /// </summary>
    public void RelayAllToZone(uint zoneId)
    {
        if (!WorldIntegration.ZoneAuthority)
            return;

        var relayed = 0;
        foreach (var house in _houses.Values)
        {
            if (house.Transform?.ZoneId != zoneId)
                continue;
            HousingZoneBridge.NotifyZoneHouseCreated(house);
            relayed++;
        }

        Logger.Info("Replayed {0} houses to Zone zoneId={1}", relayed, zoneId);
    }

    /// <summary>
    /// After persistent housing doodads have been loaded from DB, reconcile bound doodads for each completed house.
    /// Spawns and saves any bound doodads missing from the DB (first-run migration), and removes duplicates.
    /// </summary>
    public void ReconcileBoundDoodads()
    {
        Logger.Info("Reconciling bound doodads for completed houses...");
        var addedCount = 0;
        var removedCount = 0;
        var realignedCount = 0;

        foreach (var house in _houses.Values)
        {
            if (house.CurrentStep != -1)
                continue;
            if (house.Template.HousingBindingDoodad == null || house.Template.HousingBindingDoodad.Length == 0)
                continue;

            foreach (var bindingDoodad in house.Template.HousingBindingDoodad)
            {
                var matches = house.AttachedDoodads
                    .Where(d => d.TemplateId == bindingDoodad.DoodadId
                             && d.AttachPoint == bindingDoodad.AttachPointId)
                    .ToList();

                if (matches.Count == 0)
                {
                    // An unresolved binding has no offset to spawn at. Creating one anyway would persist a
                    // position that was never defined, and every later pass would then treat it as correct.
                    if (!bindingDoodad.HasResolvedPosition)
                    {
                        Logger.Warn($"Reconcile: Not spawning bound doodad templateId={bindingDoodad.DoodadId} attachPoint={bindingDoodad.AttachPointId} for house {house.Id} - attach point unresolved");
                        continue;
                    }

                    // Missing from DB - spawn fresh and save (first-run migration or data loss recovery)
                    Logger.Debug($"Reconcile: Spawning missing bound doodad templateId={bindingDoodad.DoodadId} attachPoint={bindingDoodad.AttachPointId} for house {house.Id}");
                    var doodad = doodadManager.Create(house.ParentWorld, 0, bindingDoodad.DoodadId, house, true);
                    if (doodad == null)
                    {
                        Logger.Error($"Reconcile: Failed to create doodad templateId={bindingDoodad.DoodadId} for house {house.Id} — template not found, skipping.");
                        continue;
                    }
                    doodad.AttachPoint = bindingDoodad.AttachPointId;
                    doodad.ParentObj = house;
                    doodad.Transform = house.Transform.CloneDetached(doodad);
                    doodad.Transform.Parent = house.Transform;
                    doodad.Transform.Local.ApplyWorldSpawnPositionWithDeg(bindingDoodad.Position);
                    doodad.IsPersistent = true;
                    doodad.InitDoodad();
                    doodad.Spawn(); // register in world and make visible
                    doodad.Save();
                    house.AttachedDoodads.Add(doodad);
                    house.ParentWorld.SpawnManager.AddPlayerDoodad(doodad);
                    addedCount++;
                }
                else if (matches.Count > 1)
                {
                    // Duplicates — keep the first (earliest loaded = lowest DbId), delete extras
                    Logger.Warn($"Reconcile: Removing {matches.Count - 1} duplicate(s) for templateId={bindingDoodad.DoodadId} attachPoint={bindingDoodad.AttachPointId} on house {house.Id}");
                    for (var i = 1; i < matches.Count; i++)
                    {
                        var extra = matches[i];
                        house.AttachedDoodads.Remove(extra);
                        if (extra.ObjId > 0)
                            ObjectIdManager.Instance.ReleaseId(extra.ObjId);
                        extra.Delete();
                        removedCount++;
                    }
                }

                // The kept doodad carries whatever offset it was saved with, and a house built while its
                // attach point was still unresolved saved the house origin. The template is authoritative
                // for bound doodads, so pull a drifted one back onto it.
                if (matches.Count > 0 && RealignBoundDoodad(house, matches[0], bindingDoodad))
                    realignedCount++;
            }
        }

        Logger.Info($"Bound doodad reconciliation complete: {addedCount} added, {removedCount} duplicates removed, {realignedCount} realigned.");
    }

    /// <summary>
    /// Moves a bound doodad back onto the offset its house template gives it, and returns whether it moved.
    /// </summary>
    /// <remarks>
    /// Bound doodads are fixtures rather than player-placed furniture, so the template offset is
    /// authoritative and a saved one that disagrees is stale. This matters because the offset is
    /// persisted: a house built while its attach point could not be resolved keeps that transform
    /// indefinitely, leaving the doodad out of interaction range of where it is drawn.
    /// <para>
    /// Position and orientation are both compared, by <see cref="BoundDoodadAlignment"/>. A binding
    /// whose attach point is unresolved is skipped entirely - see
    /// <see cref="HousingBindingDoodad.HasResolvedPosition"/>.
    /// </para>
    /// </remarks>
    private static bool RealignBoundDoodad(House house, Doodad doodad, HousingBindingDoodad binding)
    {
        // Nothing to align to. The saved transform is left exactly as it is rather than being overwritten
        // with an offset the template does not actually define.
        if (!binding.HasResolvedPosition || binding.Position is null)
            return false;

        var target = binding.Position;
        if (!BoundDoodadAlignment.NeedsRealignment(doodad.Transform.Local.Position,
                doodad.Transform.Local.Rotation, target))
            return false;

        Logger.Debug($"Reconcile: Realigning bound doodad templateId={binding.DoodadId} attachPoint={binding.AttachPointId} on house {house.Id}");
        doodad.Transform.Local.ApplyWorldSpawnPositionWithDeg(target);
        if (doodad.IsPersistent)
            doodad.Save();

        return true;
    }

    /// <summary>
    /// Sets or removes the untouchable buff for the house
    /// </summary>
    /// <param name="house"></param>
    /// <param name="isUntouchable"></param>
    private void SetUntouchable(House house, bool isUntouchable)
    {
        if (isUntouchable)
        {
            if (house.Buffs.CheckBuff((uint)BuffConstants.Untouchable))
                return;

            // Permanent Untouchable buff, should only be removed when failed tax payment, or demolishing by hand
            var protectionBuffTemplate = skillManager.GetBuffTemplate((uint)BuffConstants.Untouchable);
            if (protectionBuffTemplate != null)
            {
                var casterObj = new SkillCasterUnit(house.ObjId);
                house.Buffs.AddBuff(new Buff(house, house, casterObj,
                    protectionBuffTemplate, null, DateTime.UtcNow));
            }
            else
            {
                Logger.Error("Unable to find Untouchable buff template");
            }
        }
        else
        {
            // Remove Untouchable if it's enabled
            if (house.Buffs.CheckBuff((uint)BuffConstants.Untouchable))
                house.Buffs.RemoveBuff((uint)BuffConstants.Untouchable);
        }
    }

    /// <summary>
    /// Sets or removes the removal debuff for demolishing houses
    /// </summary>
    /// <param name="house"></param>
    /// <param name="isDeteriorating"></param>
    private void SetRemovalDebuff(House house, bool isDeteriorating)
    {
        if (isDeteriorating)
        {
            if (!house.Buffs.CheckBuff((uint)BuffConstants.RemovalDebuff))
            {
                // Permanent Untouchable buff, should only be removed when failed tax payment, or demolishing by hand
                var protectionBuffTemplate = skillManager.GetBuffTemplate((uint)BuffConstants.RemovalDebuff);
                if (protectionBuffTemplate != null)
                {
                    var casterObj = new SkillCasterUnit(house.ObjId);
                    house.Buffs.AddBuff(new Buff(house, house, casterObj,
                        protectionBuffTemplate, null, DateTime.UtcNow));
                }
                else
                {
                    Logger.Error("Unable to find Removal Debuff template");
                }
            }
        }
        else
        {
            // Remove Untouchable if it's enabled
            if (house.Buffs.CheckBuff((uint)BuffConstants.RemovalDebuff))
                house.Buffs.RemoveBuff((uint)BuffConstants.RemovalDebuff);
        }
    }

    /// <summary>
    /// Sends tax information about a house
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="designId"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public void ConstructHouseTax(GameConnection connection, uint designId, float x, float y, float z)
    {

        var houseTemplate = HousingGameData.Instance.GetTemplate(designId);        if (!AccountPatron.IsPaid(connection.ActiveChar))
        {
            Logger.Debug("Build refused: design {0} needs patron", designId);
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotCreate);
            return;
        }
        if (houseTemplate == null)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotCreateConstructTaxAbnormal);
            return;
        }

        var placeZoneGroupId = (ushort)zoneManager.GetZoneByKey(connection.ActiveChar.Transform.ZoneId).GroupId;
        CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount, out var hostileTaxRate, out var weeklyTax, placeZoneGroupId, x, y);

        var baseTax = (int)(houseTemplate.Taxation?.Tax ?? 0);
        var depositTax = baseTax * 2;

        connection.SendPacket(
            new SCConstructHouseTaxPacket(designId,
                heavyTaxHouseCount,
                normalTaxHouseCount,
                houseTemplate.HeavyTax,
                (ulong)baseTax,
                (ulong)depositTax,
                (ulong)totalTaxAmountDue,
                (ulong)weeklyTax,
                (uint)hostileTaxRate
            )
        );
    }

    /// <summary>
    /// Request house tax information (using name plaque of a house)
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    public void HouseTaxInfo(GameConnection connection, ushort tlId)
    {
        if (!_housesTl.TryGetValue(tlId, out var house))
            return;

        SendHouseTaxInfo(connection.ActiveChar, house);
    }

    private void SendHouseTaxInfo(Character character, House house)
    {
        // zoneGroupId/position kept (upstream's refactor dropped them) - hostileTaxRate needs to know
        // which nation's territory the house sits in, not just the house's own template.
        var houseZoneGroupId = (ushort)zoneManager.GetZoneByKey(house.Transform.ZoneId).GroupId;
        CalculateBuildingTaxInfo(house.AccountId, house.Template, false, out var totalTaxAmountDue, out _, out _, out var hostileTaxRate, out _, houseZoneGroupId, house.Transform.World.Position.X, house.Transform.World.Position.Y);

        // Fix: deposit is two scaled weeklies (was 2x base, ignoring heavy scaling)
        var depositTax = totalTaxAmountDue * 2;

        // Fix: real overdue/prepay accounting (weeksPrepay was hardcoded 0)
        var requiresPayment = false;
        var weeksWithoutPay = 0; // paid-up mean 0 weeks missed (-1/0xFF reads as overdue client-side)
        if (house.TaxDueDate <= DateTime.UtcNow)
        {
            requiresPayment = true;
            weeksWithoutPay = 0;
        }
        if (house.ProtectionEndDate <= DateTime.UtcNow)
        {
            requiresPayment = true;
            var overdueDays = (DateTime.UtcNow - house.ProtectionEndDate).TotalDays;
            weeksWithoutPay = 1 + (overdueDays <= 0 ? 0 : (int)(overdueDays / 7));
        }
        var weeksPrepay = WeeksPrepaid(house);

        // Logger.Debug($"SCHouseTaxInfoPacket; tlId:{house.TlId}, domTaxRate: 0, deposit: {depositTax}, taxDue:{totalTaxAmountDue}, protectEnd:{house.ProtectionEndDate}, isPaid:{requiresPayment}, weeksWithoutPay:{weeksWithoutPay}, isHeavy:{house.Template.HeavyTax}");

        character.SendPacket(
            new SCHouseTaxInfoPacket(
                house.TlId,
                0u,  // dominionTaxRate — TODO: implement when castles are added
                (uint)hostileTaxRate,
                (ulong)depositTax, // shown in the (?) help text as this building's deposit tax
                (ulong)totalTaxAmountDue, // Amount Due
                house.ProtectionEndDate,
                !requiresPayment, // Fix: slot is isAlreadyPaid
                (sbyte)weeksWithoutPay,
                (byte)weeksPrepay,   // Fix: banked prepaid weeks (was hardcoded 0) — prepayment is not modelled
                house.Template.HeavyTax,
                (byte)(FeaturesManager.Fsets.TaxItem ? 1 : 0)   // Fix: 1=HOUSING_TAX_SEAL, was 0=contribution path — the binary leaves the enum unnamed
            )
        );
    }

    /// <summary>
    /// Townhall Region tab: residency + balance for a zone group. The client keeps a
    /// resident map keyed by zone group that only server packets fill; an empty map reads
    /// as Outsider everywhere, so these answers are the residency feed itself.
    /// </summary>
    public void ResidentInfo(GameConnection connection, short zoneGroup)
    {
        var character = connection.ActiveChar;
        if (character == null)
            return;
        SendTownhallState(connection, zoneGroup);
    }

    /// <summary>
    /// Townhall Residents tab: real member rows on SC 0x3C plus the shared state.
    /// </summary>
    public void ResidentMembers(GameConnection connection, short zoneGroup)
    {
        var character = connection.ActiveChar;
        if (character == null)
            return;
        SendTownhallState(connection, zoneGroup);
        var owners = new HashSet<uint>();
        foreach (var house in _houses.Values)
        {
            if (house.OwnerId == 0)
                continue;
            if (zoneManager.GetZoneByKey(house.Transform.ZoneId)?.GroupId == zoneGroup)
                owners.Add(house.OwnerId);
        }
        var rows = new List<ResidentMemberRow>();
        foreach (var ownerId in owners)
        {
            var owner = WorldManager.Instance.GetCharacterById(ownerId);
            rows.Add(new ResidentMemberRow(
                0, // TODO: service points are not modelled server-side yet
                DateTime.UtcNow,
                ownerId,
                owner?.Name ?? NameManager.Instance.GetCharacterName(ownerId) ?? string.Empty,
                owner?.Level ?? (byte)0,
                owner?.HeirLevel ?? (byte)0,
                owner?.Expedition != null ? 1u : 0u,
                owner is { IsOnline: true },
                false)); // TODO: party membership
        }
        character.SendPacket(new SCResidentMemberListPacket(zoneGroup, (uint)owners.Count, rows));
    }

    /// <summary>
    /// Townhall balance/charge queries.
    /// </summary>
    public void ResidentBalance(GameConnection connection, short zoneGroup, ulong type2)
    {
        var character = connection.ActiveChar;
        if (character == null)
            return;
        SendTownhallState(connection, zoneGroup);
    }

    /// <summary>
    /// One answer for every townhall trigger: the Region tab sends no request of its
    /// own (0x01D never arrives), so every other trigger replays the full set.
    /// </summary>
    /// <summary>
    /// Resident-map feed: one 0x37 per zone group the character owns houses in. The
    /// client's isResident is a map-contains-group lookup, so without these the
    /// townhall reads Outsider. Called on world entry and on every townhall trigger.
    /// </summary>
    public void SendResidentMap(GameConnection connection, uint characterId)
    {
        var character = connection.ActiveChar;
        if (character == null)
            return;
        var groups = new HashSet<uint>();
        foreach (var house in _houses.Values)
        {
            if (house.OwnerId != characterId)
                continue;
            var zone = zoneManager.GetZoneByKey(house.Transform.ZoneId);
            if (zone != null)
                groups.Add(zone.GroupId);
        }
        foreach (var groupId in groups)
            character.SendPacket(new SCResidentMapPacket((short)groupId));
        foreach (var groupId in groups)
            character.SendPacket(new SCResidentInfoOptionPacket((short)groupId, 1));
    }

    public void SendTownhallState(GameConnection connection, short zoneGroup)
    {
        var character = connection.ActiveChar;
        if (character == null)
            return;
        SendResidentMap(connection, character.Id);
        character.SendPacket(new SCResidentInfoPacket(zoneGroup, 0, 0));
        character.SendPacket(new SCResidentBalanceInfoPacket(zoneGroup, 0, GetResidentCount(zoneGroup), 0, 0, 0, 0));
    }

    private uint GetResidentCount(int zoneGroup)
    {
        var owners = new HashSet<uint>();
        foreach (var house in _houses.Values)
        {
            if (house.OwnerId == 0)
                continue;
            if (zoneManager.GetZoneByKey(house.Transform.ZoneId)?.GroupId == zoneGroup)
                owners.Add(house.OwnerId);
        }
        return (uint)owners.Count;
    }

    /// <summary>
    /// Townhall Sales tab: every public listing in the zone group, sent as
    /// SCHouseTradeListPacket (0x2F7).
    /// </summary>
    public void HousingTradeList(GameConnection connection, short zoneGroup)
    {
        var character = connection.ActiveChar;
        if (character == null)
            return;
        var rows = new List<House>();
        foreach (var house in _houses.Values)
        {
            if (house.SellPrice <= 0 || house.OwnerId == 0 || !house.SellPublic)
                continue;
            if (zoneManager.GetZoneByKey(house.Transform.ZoneId)?.GroupId == zoneGroup)
                rows.Add(house);
        }
        SendTownhallState(connection, zoneGroup);
        character.SendPacket(new SCHouseTradeListPacket(rows));
    }


    /// <summary>
    /// Proactively pushes the guild residence's real TlId to one character via the same
    /// SCHouseTaxInfoPacket the client's own on-demand request would get. The client's cached "which
    /// house is my guild residence" id starts at 0 with no client-side way to set it, and its own
    /// request for one asks using that same starting-at-0 value - so the loop never closes on its own.
    /// The server must push the correct id unprompted at least once: on placement, and again at login
    /// for members who weren't online when it was placed.
    /// Pushes the guild residence's TlId to one character with the same SCHouseTaxInfoPacket the
    /// client's own request would receive. The client's "do I have a guild residence" value
    /// (X2Faction:GetExpeditionHouseId) starts at 0 and is only populated by an incoming
    /// SCHouseTaxInfoPacket; its own CSRequestHouseTaxPacket asks about that cached value, so before
    /// the first push it asks about tl=0, which matches no house and the loop never closes. The server
    /// therefore pushes it unprompted: on placement, and again at login for members who were offline.
    /// </summary>
    public void SendExpeditionHouseInfo(Character character)
    {
        var houseId = character.Expedition?.ResidenceHouseId ?? 0;
        if (houseId == 0)
            return;

        var house = GetHouseById(houseId);
        if (house == null)
        {
            Logger.Warn("SendExpeditionHouseInfo: expedition {0}'s ResidenceHouseId {1} does not resolve to a loaded house", character.Expedition!.Name, houseId);
            return;
        }

        SendHouseTaxInfo(character, house);
    }

    /// <summary>
    /// Start building a house at target location using design
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="designId"></param>
    /// <param name="posX"></param>
    /// <param name="posY"></param>
    /// <param name="posZ"></param>
    /// <param name="zRot"></param>
    /// <param name="itemId"></param>
    /// <param name="autoUseAaPoint"></param>
    public void Build(GameConnection connection, uint designId, float posX, float posY, float posZ, float zRot,
        ulong itemId, bool autoUseAaPoint)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        // Free accounts are not grade 0 (premium_grades grants grade 1 at zero points), so the
        // gate must ask whether the account is actually paid.
        if (!AccountPatron.IsPaid(connection.ActiveChar))
        {
            Logger.Debug("Build refused: design {0} needs patron", designId);
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotCreate);
            return;
        }

        var sourceDesignItem = connection.ActiveChar.Inventory.GetItemById(itemId);
        if (sourceDesignItem == null || sourceDesignItem.OwnerId != connection.ActiveChar.Id)
        {
            // Invalid itemId supplied or the id is not owned by the user
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.BagInvalidItem);
            return;
        }

        var houseTemplate = HousingGameData.Instance.GetTemplate(designId);
        if (houseTemplate == null)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotCreate);
            return;
        }

        // The client picks the spot, so the zone it lands in is checked against housing_areas before
        // the house is persisted — without this a design could be planted anywhere on the map and,
        // once written, would reload there on every start regardless of whether the ground allows it.
        var zoneKey = worldManager.GetZoneId(connection.ActiveChar.ParentWorld.Template, posX, posY);
        var zone = zoneManager.GetZoneByKey(zoneKey);
        var zoneGroupName = zone != null ? zoneManager.GetZoneGroupById(zone.GroupId)?.Name : null;
        if (!HousingGameData.Instance.IsCategoryAllowedInZone(zone?.Name, houseTemplate.CategoryId, zoneGroupName))
        {
            Logger.Debug(
                "Build refused: design {0} (category {1}) is not permitted in zone {2} ({3})",
                designId, houseTemplate.CategoryId, zone?.Name ?? "<unknown>", zoneKey);
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotLocateInvalidArea);
            return;
        }

        // Unique dominion_housings and fortification drawings (41079 walls/gates/towers) use the
        // zone-group claim. The lodestone circle is tax/PvP, not the inop pads.
        var buildZoneGroupId = (ushort)(zone?.GroupId ?? 0);
        var claimedGuild = guildDominionManager.GetByZoneId(buildZoneGroupId);
        var claimedHero = claimedGuild == null ? dominionManager.GetByZoneId(buildZoneGroupId) : null;
        var inCircleGuild = guildDominionManager.GetDominionAtPosition(buildZoneGroupId, posX, posY);
        var inCircleHero = inCircleGuild == null ? dominionManager.GetDominionAtPosition(buildZoneGroupId, posX, posY) : null;
        var isTerritoryDesign = HousingGameData.Instance.IsDominionHousingTemplate(designId)
            || HousingGameData.Instance.IsTerritoryHousingCategory(zone?.Name, houseTemplate.CategoryId, zoneGroupName);

        if (isTerritoryDesign)
        {
            var isHeroOfClaim = claimedHero != null
                && (uint)DominionManager.ResolveOwningFaction(connection.ActiveChar) == claimedHero.OwningFactionId
                && HeroManager.Instance.IsCurrentHero(connection.ActiveChar);
            var isOwnerGuildMember = claimedGuild != null
                && connection.ActiveChar.Expedition != null
                && (uint)connection.ActiveChar.Expedition.Id == claimedGuild.ExpeditionId;
            if (!HousingTerritoryRules.MayPlaceTerritoryBuilding(
                    claimedHero != null, claimedGuild != null, isHeroOfClaim, isOwnerGuildMember))
            {
                Logger.Debug(
                    "Build refused: design {0} is a territory building, but {1} may not place it in zone group {2}",
                    designId, connection.ActiveChar.Name, buildZoneGroupId);
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.NoPerm);
                return;
            }
        }
        else if (HousingGameData.Instance.IsExpeditionResidenceTemplate(designId))
        {
            // Guild residence (housings.family hs_expedition_house*). Any member may place it; one per guild.
            var expedition = connection.ActiveChar.Expedition;
            if (expedition == null)
            {
                Logger.Debug("Build refused: design {0} is a Guild Residence, but {1} is not in a guild", designId, connection.ActiveChar.Name);
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.NoPerm);
                return;
            }
            if (expedition.ResidenceHouseId != 0)
            {
                Logger.Debug("Build refused: design {0} is a Guild Residence, but {1}'s guild already has one (House {2})", designId, connection.ActiveChar.Name, expedition.ResidenceHouseId);
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotCreate);
                return;
            }
        }
        else if (inCircleGuild != null || inCircleHero != null)
        {
            Logger.Debug("Build refused: design {0} is ordinary housing, but the target position is inside claimed Dominion territory", designId);
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotLocateInvalidArea);
            return;
        }

        if (HousingGameData.Instance.IsDominionHousingTemplate(designId)
            && !DominionClaimRules.MayPlaceUniqueDominionDesign(
                SiegeGameData.Instance.IsUniqueDominionHousingDesign(designId),
                DominionClaimRules.HasDesignInZone(
                    GetAllHouses(),
                    designId,
                    buildZoneGroupId,
                    house => house.TemplateId,
                    DominionManager.ZoneGroupOf)))
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotCreate);
            return;
        }

        if (HousingGameData.Instance.IsExpeditionResidenceTemplate(designId))
        {
            // Guild Residence: a per-guild clubhouse, unrelated to castle/dominion territory. Any guild
            // member may place it, but only one per guild regardless of which color design is chosen.
            var expedition = connection.ActiveChar.Expedition;
            if (expedition == null)
            {
                Logger.Debug("Build refused: design {0} is a Guild Residence, but {1} is not in a guild", designId, connection.ActiveChar.Name);
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.NoPerm);
                return;
            }
            if (expedition.ResidenceHouseId != 0)
            {
                Logger.Debug("Build refused: design {0} is a Guild Residence, but {1}'s guild already has one (House {2})", designId, connection.ActiveChar.Name, expedition.ResidenceHouseId);
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotCreate);
                return;
            }
        }

        CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out _, out _, out _, out _);

        if (FeaturesManager.Fsets.TaxItem)
        {
            // Pay in Tax Certificate

            var userTaxCount = connection.ActiveChar.Inventory.GetItemsCount(SlotType.Inventory, Item.TaxCertificate);
            var userBoundTaxCount = connection.ActiveChar.Inventory.GetItemsCount(SlotType.Inventory, Item.BoundTaxCertificate);
            var totalUserTaxCount = userTaxCount + userBoundTaxCount;
            var totalCertsCost = (int)Math.Ceiling(totalTaxAmountDue / 10000f);

            // Annoyingly complex item consumption, maybe we need a separate function in inventory to handle this kind of thing
            var consumedCerts = totalCertsCost;
            if (totalCertsCost > totalUserTaxCount)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
                return;
            }
            else
            {
                var c = consumedCerts;
                // Use Bound First
                if (userBoundTaxCount > 0 && c > 0)
                {
                    if (c > userBoundTaxCount)
                        c = userBoundTaxCount;
                    connection.ActiveChar.Inventory.Bag.ConsumeItem(ItemTaskType.HouseCreation, Item.BoundTaxCertificate, c, null);
                    consumedCerts -= c;
                }
                c = consumedCerts;
                if (userTaxCount > 0 && c > 0)
                {
                    if (c > userTaxCount)
                        c = userTaxCount;
                    connection.ActiveChar.Inventory.Bag.ConsumeItem(ItemTaskType.HouseCreation, Item.TaxCertificate, c, null);
                    consumedCerts -= c;
                }

                if (consumedCerts != 0)
                    Logger.Error($"Something went wrong when paying tax for new building for player {connection.ActiveChar.Name}");
            }
        }
        else
        {
            var paymentBalance = autoUseAaPoint
                ? connection.ActiveChar.AaPoint
                : connection.ActiveChar.Money;
            if (totalTaxAmountDue > paymentBalance)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
                return;
            }

            var paid = autoUseAaPoint
                ? connection.ActiveChar.SubtractAAPoint(SlotType.Inventory, totalTaxAmountDue, ItemTaskType.HouseCreation)
                : connection.ActiveChar.SubtractMoney(SlotType.Inventory, totalTaxAmountDue, ItemTaskType.HouseCreation);
            if (!paid)
                return;
        }

        if (connection.ActiveChar.Inventory.Bag.ConsumeItem(ItemTaskType.HouseBuilding, sourceDesignItem.TemplateId, 1, sourceDesignItem) <= 0)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.BagInvalidItem);
            return;
        }

        // Spawn the actual house
        var house = Create(designId, connection.ActiveChar.Faction.Id, connection.ActiveChar.ParentWorld);

        // Fallback for un-translated buildings (en_us)
        if (house.Name == string.Empty)
        {
            var fakeLocalizedName = localizationManager.Get("items", "name", sourceDesignItem.Template.Id, houseTemplate.Name);
            if (fakeLocalizedName.EndsWith(" Design"))
                fakeLocalizedName = fakeLocalizedName.Replace(" Design", "");
            house.Name = fakeLocalizedName;
        }

        house.Id = housingIdManager.GetNextId();
        house.Transform.Local.SetPosition(posX, posY, posZ);
        // In 1.2 the rotation in SCUnitStatePacket is sent as X, Y, Z using 1 byte each.
        // This limits us to 256 unique rotations around Z (up) that can be represented.
        // When placing the house with the preview and then finalizing it, this causes the actual rotation to be different from the preview.
        // 3.0 sends a full 32-bit float for the Z-rotation for BaseUnitType.Housing, so this seems to have been fixed in later versions.
        // The fact the server has a more accurate view of the rotation than the client means positions of objects (doodads) placed in the house
        // can be offset.
        // To make the server and client agree on the rotation, we convert the float zRot to a sbyte, then back to a float.
        // The server then knows the rotation as one of the 256 unique rotations that the client can be sent.
        var (_, _, yaw) = PositionAndRotation.ToRollPitchYawSBytes(new Vector3(0, 0, zRot));
        zRot = PositionAndRotation.FromRollPitchYawSBytes(0, 0, yaw).Z;
        house.Transform.Local.SetRotation(0, 0, zRot);

        if (house.Template.BuildSteps.Count > 0)
            house.CurrentStep = 0;
        else
            house.CurrentStep = -1;
        house.OwnerId = connection.ActiveChar.Id;
        house.CoOwnerId = connection.ActiveChar.Id;
        house.AccountId = connection.AccountId;
        house.Permission = HousingPermission.Private;
        house.AllowRecover = true;
        house.PlaceDate = DateTime.UtcNow;
        house.ProtectionEndDate = DateTime.UtcNow.AddDays(AppConfiguration.Instance.World.DaysForTaxPayment);
        _houses.Add(house.Id, house);
        _housesTl.Add(house.TlId, house);
        connection.ActiveChar.SendPacket(new SCHouseDataPacket([house]));
        house.Spawn();
        if (WorldIntegration.ZoneAuthority)
            HousingZoneBridge.NotifyZoneHouseCreated(house);
        UpdateTaxInfo(house);

        if (HousingGameData.Instance.IsExpeditionResidenceTemplate(designId) && connection.ActiveChar.Expedition != null)
        {
            var expedition = connection.ActiveChar.Expedition;
            if (!ExpeditionManager.Instance.TrySetResidenceHouseId(expedition, 0, house.Id))
                return;
            Logger.Info("Guild Residence: {0}'s guild ({1}) placed House {2} (design {3})", connection.ActiveChar.Name, expedition.Name, house.Id, designId);

            // See SendExpeditionHouseInfo's own doc comment - the client can't learn its guild's
            // residence exists on its own, so every currently-online member needs this pushed now.
            foreach (var member in expedition.Members)
            {
                if (WorldManager.Instance.GetCharacterById(member.CharacterId) is { } onlineMember)
                    SendExpeditionHouseInfo(onlineMember);
            }
        }
    }

    /// <summary>
    /// Update house permission settings
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="permission"></param>
    public void ChangeHousePermission(GameConnection connection, ushort tlId, HousingPermission permission)
    {
        if (!_housesTl.TryGetValue(tlId, out var house))
            return; // invalid house

        if (house.OwnerId != connection.ActiveChar.Id)
            return; // not the owner

        house.Permission = permission;
        house.BroadcastPacket(new SCHousePermissionChangedPacket(tlId, (byte)permission), false);
    }

    /// <summary>
    /// Rename house
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="name"></param>
    public void ChangeHouseName(GameConnection connection, ushort tlId, string name)
    {
        if (!_housesTl.TryGetValue(tlId, out var house))
            return;

        if (house.OwnerId != connection.ActiveChar.Id)
            return;

        house.Name = string.Concat(name.Substring(0, 1).ToUpper(), name.AsSpan(1));
        house.IsDirty = true; // Manually set the IsDirty on House level
        connection.SendPacket(new SCUnitNameChangedPacket(house.ObjId, house.Name));
    }

    /// <summary>
    /// Start demolishing of a house
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="house"></param>
    /// <param name="failedToPayTax"></param>
    /// <param name="forceRestoreAllDecor"></param>
    public void Demolish(GameConnection connection, House house, bool failedToPayTax, bool forceRestoreAllDecor)
    {
        if (house == null)
            return;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        using var persist = mailManager.DeferPersist();
        lock (house.LifecycleSyncRoot)
            DemolishLocked(connection, house, failedToPayTax, forceRestoreAllDecor);
    }

    private void DemolishLocked(GameConnection connection, House house, bool failedToPayTax,
        bool forceRestoreAllDecor)
    {
        if (!_houses.ContainsKey(house.Id))
        {
            connection?.ActiveChar?.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return;
        }
        // Unique dominion_housings on a Hero claim are demolished by the current Hero. Ordinary houses
        // and guild residences keep their own owner/leader checks.
        var character = connection?.ActiveChar;
        var isAuthorized = connection is null;
        if (!isAuthorized && character != null)
        {
            var houseZone = zoneManager.GetZoneByKey(house.Transform.ZoneId);
            var zoneGroupId = (ushort)(houseZone?.GroupId ?? 0);
            var zoneGroupName = houseZone != null ? zoneManager.GetZoneGroupById(houseZone.GroupId)?.Name : null;
            var claimedGuild = guildDominionManager.GetByZoneId(zoneGroupId);
            var claimedHero = claimedGuild == null ? dominionManager.GetByZoneId(zoneGroupId) : null;
            var isTerritoryHouse = HousingGameData.Instance.IsDominionHousingTemplate(house.TemplateId)
                || HousingGameData.Instance.IsTerritoryHousingCategory(
                    houseZone?.Name, house.Template?.CategoryId ?? 0, zoneGroupName);

            if (isTerritoryHouse && (claimedHero != null || claimedGuild != null))
            {
                var isHeroOfClaim = claimedHero != null
                    && (uint)DominionManager.ResolveOwningFaction(character) == claimedHero.OwningFactionId
                    && HeroManager.Instance.IsCurrentHero(character);
                var isOwnerGuildMember = claimedGuild != null
                    && character.Expedition != null
                    && (uint)character.Expedition.Id == claimedGuild.ExpeditionId;
                isAuthorized = HousingTerritoryRules.MayPlaceTerritoryBuilding(
                    claimedHero != null, claimedGuild != null, isHeroOfClaim, isOwnerGuildMember);
            }
            else if (HousingGameData.Instance.IsExpeditionResidenceTemplate(house.TemplateId))
            {
                // Guild Residence is demolishable only by the owning guild's leader, not any member.
                isAuthorized = character.Expedition != null && character.Expedition.ResidenceHouseId == house.Id
                    && character.Id == character.Expedition.OwnerId;
            }
            else
            {
                isAuthorized = house.OwnerId == character.Id;
            }
        }

        if (isAuthorized)
        {
            // VERIFY: check if tax paid, cannot manually demolish or sell a house with unpaid taxes ?
            // Note - ZeromusXYZ: I'm disabling this "feature", as it would prevent you from demolishing freshly placed buildings that you want to move 
            /*
            if (house.TaxDueDate <= DateTime.UtcNow)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotDemolishUnpaidTax);
                return;
            }
            */
            if (!butlerManager.UnbindHouse(house.Id))
            {
                connection?.ActiveChar?.SendErrorMessage(ErrorMessageType.InternalError);
                return;
            }

            var ownerChar = worldManager.GetCharacterById(house.OwnerId);

            // Mark it as expired protection
            house.ProtectionEndDate = DateTime.UtcNow.AddSeconds(-1);
            // The removal debuff (buff 2250) now deals the damage; UpdateTaxInfo applies it below,
            // and ApplyDemolitionTick starts the wreck shell once the last tick lands.
            // Make sure to call UpdateTaxInfo first to remove tax-rated mails of this house
            UpdateTaxInfo(house);
            // Return items to player by mail
            ReturnHouseItemsToOwner(house, failedToPayTax, forceRestoreAllDecor, null);

            // Remove owner
            house.OwnerId = 0;
            house.CoOwnerId = 0;
            house.AccountId = 0;
            house.SellPrice = 0;
            house.SellToPlayerId = 0;
            house.Permission = HousingPermission.Public;
            house.BroadcastPacket(new SCHouseDemolishedPacket(house.TlId), false);

            ownerChar?.SendPacket(new SCHouseRemovedPacket(house.TlId));
            // Make killable
            UpdateHouseFaction(house, FactionsEnum.Monstrosity);

            SetForSaleMarkers(house, false);

            house.IsDirty = true;

            // Guild Residence: clear the owning expedition's ResidenceHouseId on EVERY demolition path,
            // not just the connection-driven one - resolved from the house/expedition relationship
            // itself rather than the acting character, since the tax-expiry auto-demolish path
            // (Demolish(null, house, true, false)) has no connection/character at all. Without this,
            // an offline owner's tax-expired residence left the expedition's ResidenceHouseId stuck
            // pointing at a house that no longer exists - blocking both a replacement placement (Build
            // rejects any nonzero ResidenceHouseId) and correctly gating housing-required buff grades.
            if (HousingGameData.Instance.IsExpeditionResidenceTemplate(house.TemplateId))
            {
                var owningExpedition = character?.Expedition?.ResidenceHouseId == house.Id
                    ? character.Expedition
                    : ExpeditionManager.Instance.Expeditions.FirstOrDefault(e => e.ResidenceHouseId == house.Id);

                if (owningExpedition != null)
                {
                    // 80% of the design's shop price (Contribution Shop pack 304), paid back as guild
                    // Contribution Points. Currently 0 for all 3 residence designs in the shipped data.
                    // Only refunds when the demolishing character is themselves a member of the owning
                    // expedition - preserves existing refund semantics, independent of the id-clearing below.
                    if (character?.Expedition == owningExpedition)
                    {
                        var residenceItemId = HousingGameData.Instance.GetItemIdByDesign(house.TemplateId);
                        var shopPrice = NpcManager.Instance.GetGoods(304)?.GetItem(residenceItemId, 0)?.Cost ?? 0;
                        var refund = (int)(shopPrice * 0.8);
                        if (refund > 0)
                            ExpeditionManager.Instance.TryChangeContributionPoints(character, refund, false);
                    }

                    ExpeditionManager.Instance.TrySetResidenceHouseId(owningExpedition, house.Id, 0);
                }
            }

            // TODO: better house killing handling
            _removedHousings.Add(house.Id);
        }
        else
        {
            // Non-owner should not be able to press demolish
            connection.ActiveChar?.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
        }
    }

    /// <summary>
    /// Fully removes a house from the world
    /// </summary>
    /// <param name="house"></param>
    public void RemoveDeadHouse(House house)
    {
        if (house != null && !TryRemoveDeadHouse(house))
            _wreckedHouses[house.Id] = DateTime.UtcNow.AddSeconds(-SecondsForDemolitionWreck);
    }

    internal bool TryRemoveDeadHouse(House house)
    {
        if (house == null)
            return false;
        return WithPersistenceOperation(() =>
        {
            lock (house.LifecycleSyncRoot)
                return RemoveDeadHouseLocked(house);
        });
    }

    private bool RemoveDeadHouseLocked(House house)
    {
        var zoneId = house.Transform?.ZoneId ?? 0;
        var houseObjId = house.ObjId;

        if (!butlerManager.UnbindHouse(house.Id))
        {
            Logger.Error("RemoveDeadHouse: failed to clear farmhand binding for house {0}", house.Id);
            return false;
        }

        house.IsRemovedFromWorld = true;

        // Same guild-residence lifecycle fix as Demolish: this path has no requesting character at
        // all (a house dying from combat/siege damage, not a player-initiated demolish), so the owning
        // expedition must be resolved from the residence relationship itself, not skipped entirely.
        if (HousingGameData.Instance.IsExpeditionResidenceTemplate(house.TemplateId))
        {
            var owningExpedition = ExpeditionManager.Instance.Expeditions.FirstOrDefault(e => e.ResidenceHouseId == house.Id);
            if (owningExpedition != null)
            {
                ExpeditionManager.Instance.TrySetResidenceHouseId(owningExpedition, house.Id, 0);
            }
        }

        // Remove house from housing tables
        _removedHousings.Add(house.Id);
        _houses.Remove(house.Id);
        _housesTl.Remove(house.TlId);
        housingTldManager.ReleaseId(house.TlId);
        housingIdManager.ReleaseId(house.Id);
        house.Delete();

        // House.Delete tears down attached doodads first, each of which emits WZRemoveDoodad.
        HousingZoneBridge.NotifyZoneHouseRemoved(zoneId, houseObjId);

        if (houseObjId > 0)
            objectIdManager.ReleaseId(houseObjId);
        return true;
    }

    /// <summary>
    /// Helper function to calculate due tax
    /// </summary>
    /// <param name="accountId"></param>
    /// <param name="newHouseTemplate"></param>
    /// <param name="buildingNewHouse"></param>
    /// <param name="totalTaxToPay"></param>
    /// <param name="heavyHouseCount"></param>
    /// <param name="normalHouseCount"></param>
    /// <param name="hostileTaxRate"></param>
    /// <param name="oneWeekTaxCount"></param>
    /// <returns></returns>
    public bool CalculateBuildingTaxInfo(uint accountId, HousingTemplate newHouseTemplate, bool buildingNewHouse, out int totalTaxToPay, out int heavyHouseCount, out int normalHouseCount, out int hostileTaxRate, out int oneWeekTaxCount, ushort? zoneId = null, float x = 0, float y = 0)
    {
        totalTaxToPay = 0;
        heavyHouseCount = 0;
        normalHouseCount = 0;
        // Castles are in now - display-only for the moment (nothing currently deducts this from totalTaxToPay,
        // matching the pre-existing behavior of this whole out-param; only the "what would my tax be" UI
        // packets read it). Whether/how a hostile-tax surcharge should actually be charged and credited to the
        // dominion's CurHouseTaxMoney pool is real follow-up work, not done here - see DominionManager's tax
        // payout tick doc comment.
        hostileTaxRate = zoneId is { } z
            ? (guildDominionManager.GetDominionAtPosition(z, x, y) ?? dominionManager.GetDominionAtPosition(z, x, y))?.TaxRate ?? 0
            : 0;
        oneWeekTaxCount = 0;

        var userHouses = new Dictionary<uint, House>();
        if (GetByAccountId(userHouses, accountId) <= 0)
            return false;

        // Count the houses on this account
        foreach (var h in userHouses)
        {
            if (h.Value.Template.HeavyTax)
                heavyHouseCount++;
            else
                normalHouseCount++;
        }

        // If this is for a new building, add 1 to count
        if (buildingNewHouse)
        {
            if (newHouseTemplate.HeavyTax)
                heavyHouseCount++;
            else
                normalHouseCount++;
        }

        // Fix: 10.x heavy tax from the heavy_taxes table (largest count <= owned).
        // Only heavy-tax properties scale, and only from the 3rd heavy property up.
        var baseTax = (int)(newHouseTemplate.Taxation?.Tax ?? 0);
        var weeklyTax = baseTax;
        if (newHouseTemplate.HeavyTax && heavyHouseCount >= 3)
            weeklyTax = HeavyTaxRules.WeeklyTax((uint)baseTax, heavyHouseCount);

        totalTaxToPay = oneWeekTaxCount = weeklyTax;

        // Deposit is two weeks of the (possibly scaled) weekly tax
        if (buildingNewHouse)
            totalTaxToPay += weeklyTax * 2;

        return true;
    }


    /// <summary>
    /// Damage dealt each time the house removal debuff (buff 2250) ticks (15s in client data).
    /// That debuff is self-cast (caster == owner == the house), which <see cref="BaseUnit.CanAttack"/>
    /// rejects for a self-target, so DamageEffect routes the tick here instead of through CanAttack.
    /// Damage is a percentage of the house's own MaxHp, so small and large houses wreck in the same time.
    /// </summary>
    public void ApplyDemolitionTick(House house, BaseUnit caster, int authoredDamage)
    {
        if (house?.Template == null || house.Hp <= 0)
            return;

        var world = AppConfiguration.Instance.World;
        var damage = world.DemolitionTickDamageMode == DemolitionTickDamageMode.Percent
            ? Math.Max(1, (int)Math.Ceiling(house.MaxHp * world.DemolitionTickDamagePercent / 100.0))
            : Math.Max(1, authoredDamage);

        if (house.Hp - damage > 0)
        {
            house.ReduceCurrentHp(caster, damage);
            return;
        }

        // Final tick: structurally dead. Keep the shell standing for the wreck window instead of
        // letting the death event remove the house the moment HP reaches zero.
        _wreckedHouses[house.Id] = DateTime.UtcNow;
        SetHouseHp(house, 0);
        Logger.Info($"Demolition: house {house.Id} ({house.Name}) wrecked, removing in {SecondsForDemolitionWreck}s");
    }

    /// <summary>Removes houses whose wreck shell has stood for <see cref="SecondsForDemolitionWreck"/>.</summary>
    private void TickDemolitionShell()
    {
        if (_wreckedHouses.Count == 0)
            return;

        foreach (var (id, wreckedUtc) in _wreckedHouses.ToList())
        {
            if (!_houses.TryGetValue(id, out var house))
            {
                _wreckedHouses.Remove(id);
                continue;
            }

            if ((DateTime.UtcNow - wreckedUtc).TotalSeconds < SecondsForDemolitionWreck)
                continue;

            Logger.Info($"Demolition: removing wrecked house {house.Id} ({house.Name})");
            if (TryRemoveDeadHouse(house))
                _wreckedHouses.Remove(id);
        }
    }

    private static void SetHouseHp(House house, int hp)
    {
        if (house.Hp == hp)
            return;

        house.Hp = hp;
        house.BroadcastPacket(new SCUnitPointsPacket(house.ObjId, house.Hp, house.Mp), true);
    }

    /// <summary>
    /// This function updates related tax mails of a house (if needed)
    /// </summary>
    /// <param name="house"></param>
    public void UpdateTaxInfo(House house)
    {
        var isDemolished = house.ProtectionEndDate <= DateTime.UtcNow;
        var isTaxDue = house.TaxDueDate <= DateTime.UtcNow;

        // Update Buffs (if needed)
        SetUntouchable(house, !isDemolished);
        SetRemovalDebuff(house, isDemolished);

        if (house.OwnerId <= 0)
            return;

        // If expired, start demolition debuffs
        if (isDemolished)
        {
            mailManager.DeleteHouseMails(house.Id);
        }
        else
        if (isTaxDue)
        {
            // TODO: update corresponding mails if needed (like update weeks unpaid etc)
            var allMails = mailManager.GetMyHouseMails(house.Id);

            if (allMails.Count <= 0)
            {
                // Create new tax mail
                var newMail = new MailForTax(house);
                newMail.FinalizeMail();
                newMail.Send();
                Logger.Trace($"New Tax Mail sent for {house.Name} owned by {house.OwnerId}");
            }
            else
            {
                foreach (var mail in allMails)
                {
                    MailForTax.UpdateTaxInfo(mail, house);
                    Logger.Trace($"Tax Mail {mail.Id} updated for {house.Name} ({house.Id}) owned by {house.OwnerId}");
                }
            }
        }
    }

    /// <summary>
    /// Adds a week to the protection end date (pay 1 week's tax)
    /// </summary>
    /// <param name="house"></param>
    /// <returns></returns>
    public bool PayWeeklyTax(House house)
    {
        house.ProtectionEndDate = house.ProtectionEndDate.AddDays(AppConfiguration.Instance.World.DaysForTaxPayment);
        return true;
    }

    /// <summary>
    /// Whole prepaid weeks banked beyond the current tax period (client weeksPrepay).
    /// A freshly placed house reports 0.
    /// </summary>
    private static int WeeksPrepaid(House house)
    {
        var extra = (house.ProtectionEndDate - DateTime.UtcNow).TotalDays - AppConfiguration.Instance.World.DaysForTaxPayment;
        if (extra <= 0)
            return 0;
        return (int)(extra / 7);
    }

    /// <summary>
    /// Pays one configured tax period before it is due. The request is keyed by the housing
    /// timeline ID, never by a client-supplied house database or object ID.
    /// </summary>
    public bool PrepayHouseTax(GameConnection connection, ushort tlId, bool useAaPoint)
    {
        var character = connection.ActiveChar;
        if (character == null || !_housesTl.TryGetValue(tlId, out var house) || house.OwnerId != character.Id)
            return false;
        if (!AccountPatron.IsPaid(character))
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }

        // Fix: mirror the client prepay gates (unpaid, for sale, under construction, prepaid cap)
        if (house.TaxDueDate <= DateTime.UtcNow)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }
        if (house.SellPrice > 0)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }
        if (house.CurrentStep != -1)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }
        if (WeeksPrepaid(house) >= MaxPrepaidWeeks)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }

        if (!CalculateBuildingTaxInfo(
                house.AccountId, house.Template, false,
                out _, out _, out _, out _, out var weeklyTax))
            return false;

        if (FeaturesManager.Fsets.TaxItem)
        {
            var boundCertificates = character.Inventory.GetItemsCount(SlotType.Inventory, Item.BoundTaxCertificate);
            var certificates = character.Inventory.GetItemsCount(SlotType.Inventory, Item.TaxCertificate);
            var requiredCertificates = (int)Math.Ceiling(weeklyTax / 10000f);
            if (boundCertificates + certificates < requiredCertificates)
            {
                character.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
                return false;
            }

            var remaining = requiredCertificates;
            if (boundCertificates > 0)
            {
                var consume = Math.Min(remaining, boundCertificates);
                character.Inventory.Bag.ConsumeItem(ItemTaskType.HouseDeposit, Item.BoundTaxCertificate, consume, null);
                remaining -= consume;
            }

            if (remaining > 0)
                character.Inventory.Bag.ConsumeItem(ItemTaskType.HouseDeposit, Item.TaxCertificate, remaining, null);
        }
        else
        {
            var balance = useAaPoint ? character.AaPoint : character.Money;
            if (weeklyTax > balance)
            {
                character.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
                return false;
            }

            var paid = useAaPoint
                ? character.SubtractAAPoint(SlotType.Inventory, weeklyTax, ItemTaskType.HouseDeposit)
                : character.SubtractMoney(SlotType.Inventory, weeklyTax, ItemTaskType.HouseDeposit);
            if (!paid)
                return false;
        }

        if (!PayWeeklyTax(house))
            return false;

        UpdateTaxInfo(house);
        HouseTaxInfo(connection, tlId);
        return true;
    }

    /// <summary>
    /// Get house by DB Id
    /// </summary>
    /// <param name="houseId"></param>
    /// <returns></returns>
    /// <summary>Every loaded building. Used by the recall skills to find one the caster may enter.</summary>
    public IEnumerable<House> GetAllHouses() => _houses.Values;

    public House GetHouseById(uint houseId)
    {
        return _houses.GetValueOrDefault(houseId);
    }

    /// <summary>
    /// Get house by TlId
    /// </summary>
    /// <param name="houseTlId"></param>
    /// <returns></returns>
    private House GetHouseByTlId(ushort houseTlId)
    {
        return _housesTl.GetValueOrDefault(houseTlId);
    }

    /// <summary>
    /// Changes the faction of the house
    /// </summary>
    /// <param name="house"></param>
    /// <param name="factionId"></param>
    private void UpdateHouseFaction(House house, FactionsEnum factionId)
    {
        var oldFaction = house.Faction?.Id ?? 0;
        house.BroadcastPacket(new SCUnitFactionChangedPacket(house.ObjId, house.Name, oldFaction, factionId, false), true);
        house.Faction = factionManager.GetFaction(factionId);
        foreach (var doodad in house.AttachedDoodads)
            doodadManager.RefreshFaction(doodad, house, house);
        foreach (var doodad in house.ParentWorld?.SpawnManager?.GetAllPlayerDoodads()
                     .Where(d => d.OwnerDbId == house.Id) ?? [])
            doodadManager.RefreshFaction(doodad, house, house);
        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayUnitFactionChangedToZone?.Invoke(
                house.ObjId, (int)oldFaction, (int)factionId, false);
    }

    /// <summary>
    /// Helper function for when the owning character changes faction
    /// </summary>
    /// <param name="characterId"></param>
    /// <param name="factionId"></param>
    public void UpdateOwnedHousingFaction(uint characterId, FactionsEnum factionId)
    {
        // TODO: Does this also need to be done when temporary changing factions? (like arena)
        var myHouses = new Dictionary<uint, House>();
        GetByCharacterId(myHouses, characterId);
        foreach (var h in myHouses)
            if (h.Value.Faction == null || h.Value.Faction.Id != factionId)
                UpdateHouseFaction(h.Value, factionId);
    }

    /// <summary>
    /// Returns furniture of a house that's being demolished or sold
    /// </summary>
    /// <param name="house"></param>
    /// <param name="failedToPayTax">Set true if demolishing due to failed tax, this adds a delay to the mail</param>
    /// <param name="forceRestoreAllDecor">For GM commands or server merges. Will try to send ALL placed furniture if set to true, even those that normally don't get returned.</param>
    /// <param name="newOwner">New owner Character if buying, otherwise leave null</param>
    private void ReturnHouseItemsToOwner(House house, bool failedToPayTax, bool forceRestoreAllDecor, Character newOwner)
    {
        if (house.OwnerId <= 0)
            return;

        var returnedItems = new List<Item>();
        var returnedMoney = 0;

        // If returning items because of a new House Owner, then don't include the design
        if (newOwner == null)
        {
            // TODO: proper grades for design
            // TODO: for future versions: Support Full-Kit demolition
            var designItemId = HousingGameData.Instance.GetItemIdByDesign(house.Template.Id);
            var designItem = itemManager.Create(designItemId, 1, 0);
            var designTemplate = itemManager.GetTemplate(designItemId);
            if (designTemplate != null && designItem != null)
            {
                designItem.Grade = designTemplate.FixedGrade >= 0 ? (byte)designTemplate.FixedGrade : (byte)0;
                designItem.OwnerId = house.OwnerId;
                designItem.SlotType = SlotType.Mail;
                returnedItems.Add(designItem);
            }
            else
            {
                Logger.Error($"Was unable to find design items for demolishing {house.Name} ({house.Id}). HouseTemplateId: {house.Template.Id}, DesignItemId: {designItemId}");
            }

            // Return taxes
            if (!failedToPayTax)
            {
                if (FeaturesManager.Fsets.TaxItem)
                {
                    var taxItem = itemManager.Create(Item.BoundTaxCertificate, (int)(house.Template.Taxation.Tax / 5000), 0);
                    taxItem.OwnerId = house.OwnerId;
                    taxItem.SlotType = SlotType.Mail;
                    returnedItems.Add(taxItem);
                }
                else
                {
                    returnedMoney = (int)(house.Template.Taxation.Tax * 2);
                }
            }
        }

        var furniture = house.ParentWorld.GetDoodadByHouseDbId(house.Id);
        foreach (var f in furniture)
        {
            // Ignore attached objects (those are doors/windows etc)
            if (f.AttachPoint != AttachPointKind.None)
                continue;

            // Ignore for sale signs
            if (f.TemplateId == ForSaleMarkerDoodadId)
                continue;

            var decoDesign = HousingGameData.Instance.GetDecorationDesignFromDoodadId(f.TemplateId);
            if (decoDesign == null)
            {
                // Is not furniture, probably plants or backpacks
                f.Transform.DetachAll();
                f.ParentObjId = 0;
                f.ParentObj = null;
                f.OwnerDbId = 0;
                doodadManager.RefreshFaction(f);
                // TODO: probably needs to send a packet as well here
                continue;
            }

            var decoInfo = HousingGameData.Instance.GetItemHousingDecorations(decoDesign.Id);
            if (decoInfo == null)
            {
                // No design info for this item ? Just detach it for now
                f.Transform.DetachAll();
                f.ParentObjId = 0;
                f.ParentObj = null;
                f.OwnerDbId = 0;
                doodadManager.RefreshFaction(f);
                Logger.Warn($"ReturnHouseItemsToOwner - Furniture has a design, but couldn't find a item for it, DoodadObjId:{f.ObjId} Template:{f.TemplateId}, DesignId: {decoDesign.Id}");
                continue;
            }

            var thisDoodadsItem = itemManager.GetItemByItemId(f.ItemId);
            var returnedThisItem = false;

            var wantReturned = (newOwner == null && decoInfo.Restore) || forceRestoreAllDecor;

            // If item is bound, always return it owner
            if (f.ItemId > 0)
            {
                var item = itemManager.GetItemByItemId(f.ItemId);
                if (item.ItemFlags.HasFlag(ItemFlag.SoulBound))
                    wantReturned = true;
            }

            // If this doodad is a Coffer and has a ItemContainer attached, also return all item of that container
            if (f is DoodadCoffer coffer && f.GetItemContainerId() > 0)
            {
                // TODO: Check if items should stay in the coffer when house is sold.
                // Move it to new owner's SystemContainer first so they don't get destroyed
                var ownerSystemContainer = itemManager.GetItemContainerForCharacter(house.OwnerId, SlotType.System, null, 0);
                for (var i = coffer.ItemContainer.Items.Count - 1; i >= 0; i--)
                {
                    var cofferItem = coffer.ItemContainer.Items[i];
                    //if (cofferItem.HasFlag(ItemFlag.SoulBound) || forceRestoreAllDecor)
                    {
                        ownerSystemContainer?.AddOrMoveExistingItem(ItemTaskType.Invalid, cofferItem);
                        returnedItems.Add(cofferItem);
                    }
                }
            }

            // If the decoration item isn't marked as Restore, then just delete it (and it's possibly attached item)
            if (!wantReturned)
            {
                // Non-restore-able item
                if (newOwner == null)
                {
                    // Just delete the doodad and attached item if no new owner
                    // Delete the attached item
                    if (f.ItemId != 0)
                        thisDoodadsItem._holdingContainer?.ConsumeItem(ItemTaskType.Invalid,
                            thisDoodadsItem.TemplateId, thisDoodadsItem.Count, thisDoodadsItem);

                    // Is furniture, but doesn't restore, destroy it
                    f.Transform.DetachAll();
                    f.ItemId = 0;
                    f.Delete();
                }
                else
                {
                    // Move the doodad and item to the new owner
                    if (f.ItemId != 0)
                    {
                        // If a single item is attached, change it's owner and location
                        var item = itemManager.GetItemByItemId(f.ItemId);
                        newOwner.Inventory.SystemContainer.AddOrMoveExistingItem(ItemTaskType.Invalid, item);
                    }
                    // Change doodad owner
                    f.OwnerId = newOwner.Id;
                }

                continue;
            }

            // Item needs to be actually returned, so let's do that
            if (f.ItemId > 0)
            {
                // Ignore if it's not in a System container for whatever reason
                if (thisDoodadsItem is { SlotType: SlotType.System })
                {
                    returnedItems.Add(thisDoodadsItem);
                    returnedThisItem = true;
                    f.ItemId = 0; // don't auto-delete
                }
            }
            else
            if (f.ItemTemplateId > 0)
            {
                // try to stack stackable items
                var oldItem = returnedItems.FirstOrDefault(x =>
                    x.TemplateId == f.ItemTemplateId && x.HasDefaultDetail && x.MadeUnitId == 0 &&
                    x.Count < x.Template.MaxCount);

                if (oldItem != null)
                {
                    oldItem.Count++;
                }
                else
                {
                    // It's a new one, add an item slot
                    var furnitureItem = itemManager.Create(f.ItemTemplateId, 1, 0);
                    var furnitureTemplate = itemManager.GetTemplate(f.ItemTemplateId);
                    furnitureItem.Grade = furnitureTemplate.FixedGrade >= 0 ? (byte)furnitureTemplate.FixedGrade : (byte)0;
                    furnitureItem.OwnerId = house.OwnerId;
                    furnitureItem.SlotType = SlotType.Mail;
                    returnedItems.Add(furnitureItem);
                }
                returnedThisItem = true;
            }
            else
            {
                // Not sure what happened here, just ignore it
                continue;
            }

            // Set new doodad owner if needed
            if (newOwner != null)
                f.OwnerId = newOwner.Id;

            if (newOwner == null || returnedThisItem)
            {
                f.Transform.DetachAll();
                f.Delete();
            }
        }

        // TODO: Grab a list of items in chests

        // TODO: Proper Mail handler
        BaseMail newMail = null;
        for (var i = 0; i < returnedItems.Count; i++)
        {
            // Split items into mails of maximum 10 attachments
            if (i % 10 == 0)
            {
                // TODO: proper mail handler
                newMail = new BaseMail
                {
                    MailType = MailType.Demolish,
                    ReceiverName = nameManager.GetCharacterName(house.OwnerId), // Doesn't seem like this needs to be set
                    Header =
                    {
                        ReceiverId = house.OwnerId,
                        SenderId = 0,
                        SenderName = ".houseDemolish",
                        Extra = house.Id
                    },
                    Title = "title",
                    Body = {
                        Text = "body", // Yes, that's indeed what it needs to be set to
                        SendDate = DateTime.UtcNow,
                        RecvDate = DateTime.UtcNow.AddHours(failedToPayTax ? HoursForFailedTaxToReturnHouse : 0)
                    }
                };
            }
            // Only attach money to first mail
            if (returnedMoney > 0 && i == 0)
                newMail.AttachMoney(returnedMoney);

            // If player is loaded in at the moment (which he/she should be anyway), directly manipulate the inventory
            // If not, only change the container
            var onlineOwner = worldManager.GetCharacterById((uint)returnedItems[i].OwnerId);
            if (onlineOwner != null)
                onlineOwner.Inventory.MailAttachments.AddOrMoveExistingItem(ItemTaskType.Invalid, returnedItems[i]);
            else
                returnedItems[i].SlotType = SlotType.Mail;

            // Attach item
            newMail.Body.Attachments.Add(returnedItems[i]);

            // Send on last or 10th item of the mail
            if (i % 10 == 9 || i == returnedItems.Count - 1)
                newMail.Send();
        }

        if (newMail != null)
        {
            Logger.Trace($"Demolition mail sent to {newMail.ReceiverName}");
        }
    }

    /* Unused
    /// <summary>
    /// Get house design by item template
    /// </summary>
    /// <param name="itemId"></param>
    /// <returns></returns>
    private uint GetDesignByItemId(uint itemId)
    {
        var design = _housingItemHousings.FirstOrDefault(h => h.Item_Id == itemId);
        return design?.Design_Id ?? 0;
    }
    */

    /// <summary>
    /// Helper function to calculate how many Appraisal Certificates are needed to sell a house at a given price
    /// </summary>
    /// <param name="house">Not used in early versions</param>
    /// <param name="salePrice"></param>
    /// <returns></returns>
    private static int CalculateSaleCertifcates(House house, uint salePrice)
    {
        // Fix: 10.x appraisal seals scale with price from the template seal_count
        // (taxations.seal_count); the client shows the same need via GetHouseSaleItemInfo.
        var baseTax = house?.Template?.Taxation?.Tax ?? 0;
        var sealCount = house?.Template?.Taxation?.SealCount ?? 1;
        if (baseTax <= 0 || salePrice <= 0)
            return Math.Max(1, (int)sealCount);
        var certAmount = (int)sealCount;
        if (certAmount < 1)
            certAmount = 1;
        return certAmount;
    }

    /// <summary>
    /// Sets or removes For Sale Signs on the property
    /// </summary>
    /// <param name="house"></param>
    /// <param name="isForSale"></param>
    /// <summary>
    /// Writes the house row immediately. Sale transitions must not wait for the periodic
    /// tick or a graceful shutdown (same reason as DominionManager.SaveLodestoneNow).
    /// </summary>
    private static void SaveHouseNow(House house)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        house.Save(connection, transaction);
        transaction.Commit();
    }

    private void SetForSaleMarkers(House house, bool isForSale)
    {
        var world = house.ParentWorld;
        if (world == null)
        {
            Logger.Warn($"Trying to set a house for sale of a house that doesn't have a world attached {house.Id}");
            return;
        }
        if (isForSale)
        {
            var radius = house.Template?.GardenRadius ?? 0f;
            for (var postId = 0; postId < 4; postId++)
            {
                var xMultiplier = postId % 2 == 0 ? -1 : 1f;
                var yMultiplier = postId / 2 == 0 ? -1 : 1f;
                var zRot = (135f + 90f * postId % 360).DegToRad();

                var doodad = doodadManager.Create(house.ParentWorld,  0, ForSaleMarkerDoodadId, null, true);
                // location
                doodad.Transform.Local.SetPosition(
                    // 10.x: plot corner from housing_sizes.garden_radius (axis-aligned; TODO rotate by yaw)
                    radius * xMultiplier + house.Transform.World.Position.X,
                    radius * yMultiplier + house.Transform.World.Position.Y,
                    +house.Transform.World.Position.Z);
                // adjust height to the floor
                doodad.Transform.Local.SetHeight(doodad.ParentWorld.Template.GeoData.GetHeight(doodad.Transform.World.Position));// worldManager.GetHeight(doodad.Transform)));
                doodad.Transform.Local.SetZRotation(zRot);
                //doodad.Transform.WorldId = world.Template.Id;
                doodad.Transform.InstanceId = world.Id;
                doodad.Transform.ZoneId = house.Transform?.ZoneId ?? 0; // Fix: zone-stamp markers (was 0, zone binary dropped them)
                doodad.ItemTemplateId = 0; // designId;
                doodad.ItemId = 0;
                doodad.OwnerId = house.OwnerId; // Fix: attribute markers so load resolves creator faction
                doodad.ParentObjId = 0;
                doodad.ParentObj = null;
                doodad.UccId = 0;
                doodad.AttachPoint = AttachPointKind.None;
                doodad.OwnerType = DoodadOwnerType.Housing;
                doodad.OwnerDbId = house.Id;
                doodadManager.RefreshFaction(doodad, null, house);
                doodad.IsPersistent = true;
                doodad.InitDoodad();

                doodad.Spawn();
                doodad.Save(); // Fix: persist sale markers across restarts like bound doodads
            }
        }
        else
        {
            // Get all doodads related to this house
            var thisHouseSalePosts = world.GetDoodadByHouseDbId(house.Id);
            for (var c = thisHouseSalePosts.Count - 1; c >= 0; c--)
            {
                var doodad = thisHouseSalePosts[c];
                if (doodad.TemplateId == ForSaleMarkerDoodadId)
                {
                    house.AttachedDoodads.Remove(doodad);
                    doodad.Delete();
                }
            }
        }
    }

    /// <summary>
    /// Returns management titles to a seller when a listing could not be persisted.
    /// </summary>
    private static void RefundSaleTitles(Character seller, int amount)
    {
        if (seller == null || amount <= 0)
            return;

        if (!seller.Inventory.Bag.AcquireDefaultItemEx(ItemTaskType.BuyHouse, Item.BuildingManagementTitle, amount, -1, out _, out _, 0))
            Logger.Warn("SetForSale: failed to return {0} management title(s) to {1}", amount, seller.Name);
    }

    /// <summary>
    /// Puts up a house for sale
    /// </summary>
    /// <param name="house"></param>
    /// <param name="price"></param>
    /// <param name="buyerId">Use CharacterId for selling to a specific person</param>
    /// <param name="seller">Current owner of the property (needed to manipulate inventory)</param>
    /// <returns></returns>
    public bool SetForSale(House house, uint price, uint buyerId, Character seller, bool isPublic = true)
    {
        if (house == null)
            return false;

        if (seller != null && house.OwnerId != seller.Id)
        {
            seller.SendErrorMessage(ErrorMessageType.HouseCannotSellAsNotOwner);
            return false;
        }
        if (seller != null && !AccountPatron.IsPaid(seller))
        {
            seller.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }
        if (!house.Template.IsSellable)
        {
            seller?.SendErrorMessage(ErrorMessageType.HouseCannotSellAsType);
            return false;
        }
        if (house.CurrentStep != -1)
        {
            seller?.SendErrorMessage(ErrorMessageType.HouseCannotSellAsUnderConstruction);
            return false;
        }
        if (house.TaxDueDate <= DateTime.UtcNow)
        {
            seller?.SendErrorMessage(ErrorMessageType.HouseCannotSellAsDelayedTax);
            return false;
        }
        if (house.SellPrice > 0)
        {
            seller?.SendErrorMessage(ErrorMessageType.HouseCannotSellAsAlreadyForSale);
            return false;
        }

        // Check if buyer exists (we just check if the name exists)
        var buyerName = nameManager.GetCharacterName(buyerId);
        if (buyerId != 0 && buyerName == null)
            return false;

        buyerName ??= "";

        if (buyerId != 0 && buyerId == house.OwnerId)
        {
            seller?.SendErrorMessage(ErrorMessageType.HouseCannotSellToOneself);
            return false;
        }

        if (!SalePriceRules.IsListablePrice(price))
        {
            // Mirrors the auction escrow cap; no too-high enum exists on this wire
            seller?.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }

        // Using the GM command does not send the seller (uses null), and thus will not require certificates.
        // Pre-check the cost so an unaffordable seller never reaches the durable writes below.
        var certAmount = 0;
        if (seller != null)
        {
            certAmount = CalculateSaleCertifcates(house, price);
            if (seller.Inventory.GetItemsCount(SlotType.Inventory, Item.BuildingManagementTitle) < certAmount)
            {
                seller.SendErrorMessage(ErrorMessageType.HouseCannotSellAsNotEnoughSeal);
                return false;
            }
        }

        // One World snapshot writes the house row and the deducted titles in the same transaction
        // (SaveManager saves houses and items together), so the listing and the charge cannot diverge.
        var flushed = false;
        using (WorldSnapshotCommit.Begin(bypassCharges: false))
        {
            house.SellPrice = price;
            house.SellToPlayerId = buyerId;
            house.SellPublic = isPublic;
            SetForSaleMarkers(house, true);

            var consumedTitles = 0;
            if (certAmount > 0)
                consumedTitles = seller.Inventory.Bag.ConsumeItem(ItemTaskType.BuyHouse, Item.BuildingManagementTitle, certAmount, null);

            if (certAmount > 0 && consumedTitles != certAmount)
            {
                // Under-billed: give back whatever was taken and bail out before the flush.
                RefundSaleTitles(seller, consumedTitles);
                house.SellPrice = 0;
                house.SellToPlayerId = 0;
                house.SellPublic = true;
                SetForSaleMarkers(house, false);
                seller.SendErrorMessage(ErrorMessageType.HouseCannotSellAsNotEnoughSeal);
                return false;
            }

            // Run the compensation through the flush callback so it happens while the save lock is
            // still held - a failed snapshot must not leave the titles spent and the listing gone.
            // RequestFlush first: FlushNow is a no-op unless a flush was asked for, and the
            // callback above could then never run.
            WorldSnapshotCommit.RequestFlush(bypassCharges: false);
            flushed = WorldSnapshotCommit.FlushNow(bypassCharges: false, onFailed: () =>
            {
                RefundSaleTitles(seller, certAmount);
                house.SellPrice = 0;
                house.SellToPlayerId = 0;
                house.SellPublic = true;
                SetForSaleMarkers(house, false);
            });
        }

        // Take FlushNow's own result: disposing the scope above overwrites the last status with
        // Saved, so AcceptedLast on its own still reports success after a failed flush.
        if (!flushed || !WorldSnapshotCommit.AcceptedLast(bypassCharges: false, failNextPersist: false))
        {
            // The snapshot did not commit and the callback already restored the titles.
            Logger.Warn("SetForSale: snapshot rejected for house {0}, listing rolled back", house.Id);
            house.BroadcastPacket(new SCHouseResetForSalePacket(house.TlId, house.Name), false);
            seller?.SendErrorMessage(ErrorMessageType.HouseCannotSellAsNotEnoughSeal);
            return false;
        }

        house.BroadcastPacket(new SCHouseSetForSalePacket(house.TlId, price, house.SellToPlayerId, buyerName, house.Name), false);
        return true;
    }

    public bool SetForSale(ushort houseTlId, uint price, uint buyerId, Character seller, bool isPublic = true) => SetForSale(GetHouseByTlId(houseTlId), price, buyerId, seller, isPublic);

    /// <summary>
    /// Rotate Building confirm (CS 0x1A0): bc u24 house objId + zRot + height.
    /// Mirrors the client gates (owner, not on sale, tax current, in range, rotate cost).
    /// </summary>
    public bool RotateHouse(GameConnection connection, uint bc, float zRot, float height)
    {
        var character = connection.ActiveChar;
        if (character == null)
            return false;
        // Client-supplied floats: reject non-finite input outright, and normalise the yaw.
        if (!float.IsFinite(zRot) || !float.IsFinite(height))
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }
        zRot %= 360f;
        if (zRot < 0f)
            zRot += 360f;
        House rotateHouse = null;
        foreach (var h in _houses.Values)
        {
            if (h.ObjId == bc)
            {
                rotateHouse = h;
                break;
            }
        }
        if (rotateHouse == null || rotateHouse.OwnerId != character.Id)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }
        if (rotateHouse.SellPrice > 0)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }
        if (rotateHouse.ProtectionEndDate <= DateTime.UtcNow)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }
        var hp = rotateHouse.Transform.World.Position;
        var cp = character.Transform.World.Position;
        var dx = cp.X - hp.X;
        var dy = cp.Y - hp.Y;
        var dz = cp.Z - hp.Z;
        var maxDist = (rotateHouse.Template?.GardenRadius ?? 0f) + 30f;
        if (dx * dx + dy * dy + dz * dz > maxDist * maxDist)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }
        var rotateItemId = rotateHouse.Template?.RotateItemId ?? 0;
        var rotateItemCount = rotateHouse.Template?.RotateItemCount ?? 0;

        // The charge and the rotation commit together: one World snapshot writes the house row and
        // the consumed item, and a failure puts both back while the save lock is still held.
        var originalRotation = rotateHouse.Transform.Local.Rotation;
        var originalPosition = rotateHouse.Transform.Local.Position;
        var flushed = false;
        using (WorldSnapshotCommit.Begin(bypassCharges: false))
        {
            if (rotateItemId > 0 && rotateItemCount > 0)
            {
                // Check the count first: ConsumeItem releases whatever the bag has even when it is
                // short, and returning here without a refund would eat the partial charge.
                if (character.Inventory.GetItemsCount(SlotType.Inventory, rotateItemId) < (int)rotateItemCount)
                {
                    character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
                    return false;
                }

                var taken = character.Inventory.Bag.ConsumeItem(ItemTaskType.HouseBuilding, rotateItemId, (int)rotateItemCount, null);
                if (taken != (int)rotateItemCount)
                {
                    if (taken > 0)
                        character.Inventory.Bag.AcquireDefaultItemEx(ItemTaskType.HouseBuilding, rotateItemId, taken, -1, out _, out _, 0);
                    character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
                    return false;
                }
            }

            // Keep the plot's own elevation - the client's height is only a confirmation of it,
            // never an authoritative placement (unbounded values would corrupt the saved transform).
            rotateHouse.Transform.Local.SetPosition(hp.X, hp.Y, hp.Z);
            rotateHouse.Transform.Local.SetRotation(0, 0, zRot);
            rotateHouse.IsDirty = true;

            // RequestFlush first, or FlushNow reports success without writing (see SetForSale).
            WorldSnapshotCommit.RequestFlush(bypassCharges: false);
            flushed = WorldSnapshotCommit.FlushNow(bypassCharges: false, onFailed: () =>
            {
                rotateHouse.Transform.Local.SetPosition(originalPosition.X, originalPosition.Y, originalPosition.Z);
                rotateHouse.Transform.Local.SetRotation(originalRotation.X, originalRotation.Y, originalRotation.Z);
                rotateHouse.IsDirty = true;
                if (rotateItemId > 0 && rotateItemCount > 0 &&
                    !character.Inventory.Bag.AcquireDefaultItemEx(ItemTaskType.HouseBuilding, rotateItemId, (int)rotateItemCount, -1, out _, out _, 0))
                    Logger.Warn("RotateHouse: failed to return {0} x{1} to {2}", rotateItemId, rotateItemCount, character.Name);
            });
        }

        // FlushNow's own result, because the scope's dispose overwrites the last status with Saved.
        if (!flushed || !WorldSnapshotCommit.AcceptedLast(bypassCharges: false, failNextPersist: false))
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }

        rotateHouse.BroadcastPacket(new SCHouseRotatedPacket(rotateHouse.ObjId, zRot), false);
        return true;
    }

    /// <summary>
    /// Cancels a sale
    /// </summary>
    /// <param name="house"></param>
    /// <param name="returnCertificates"></param>
    /// <returns></returns>
    public bool CancelForSale(House house, bool returnCertificates = true)
    {
        if (house.SellPrice <= 0)
        {
            // Fix: idempotent recovery, client sale cache can stick, always emit clear-signal
            house.BroadcastPacket(new SCHouseResetForSalePacket(house.TlId, house.Name), false);
            SetForSaleMarkers(house, false);
        SaveHouseNow(house);
            return true;
        }
        var certAmount = CalculateSaleCertifcates(house, house.SellPrice);
        var owner = worldManager.GetCharacterById(house.OwnerId);

        house.SellPrice = 0;
        house.SellToPlayerId = 0;
        house.SellPublic = true;
        // Can only return certificates if owner is online and is the one resetting the sale
        if (certAmount > 0 && returnCertificates && owner != null)
        {
            if (owner.Inventory.MailAttachments.AcquireDefaultItemEx(ItemTaskType.Invalid,
                Item.BuildingManagementTitle, certAmount, -1, out var addedItems, out _, 0))
            {
                // Mail container is set up to never update existing items, so we can discard that result
                var mail = new BaseMail
                {
                    MailType = MailType.HousingSale,
                    Header =
                    {
                        ReceiverId = house.OwnerId,
                        SenderName = ".houseSellCancel"
                    },
                    ReceiverName = nameManager.GetCharacterName(house.OwnerId),
                    Title = "title(" + zoneManager.GetZoneByKey(house.Transform.ZoneId)?.GroupId.ToString() + ",'" + house.Name + "')",
                    Body =
                    {
                        Text = "body('" + house.Name + "', " + Item.BuildingManagementTitle.ToString() + ", " + certAmount.ToString() + ")"
                    }
                };
                mail.Body.Attachments.AddRange(addedItems);
                mail.Body.SendDate = DateTime.UtcNow;
                mail.Body.RecvDate = DateTime.UtcNow.AddMilliseconds(1);
                mail.Send();
            }
            else
            {
                // Failed to create Appraisal certificate ?
                Logger.Warn("CancelForSale - Failed to create Appraisal Certificates for mail");
                return false;
            }
        }

        house.BroadcastPacket(new SCHouseResetForSalePacket(house.TlId, house.Name), false);
        SetForSaleMarkers(house, false);
        SaveHouseNow(house);

        return true;
    }

    public bool CancelForSale(ushort houseTlId, bool returnCertificates = true) => CancelForSale(GetHouseByTlId(houseTlId), returnCertificates);

    /// <summary>
    /// Updates all furniture on the house to a new owner and broadcasts packets for it
    /// </summary>
    /// <param name="house"></param>
    /// <param name="characterId"></param>
    /// <returns>The number of items that have their owner information updated</returns>
    private static uint UpdateFurnitureOwner(House house, uint characterId, FactionsEnum newFaction)
    {
        uint res = 0;
        var furnitureList = house.ParentWorld.GetDoodadByHouseDbId(house.Id);
        foreach (var furniture in furnitureList)
        {
            furniture.OwnerId = characterId;
            furniture.BroadcastPacket(new SCDoodadOriginatorPacket(furniture.ObjId, characterId, newFaction), true);
            if (furniture.IsPersistent)
                furniture.Save();
            res++;
        }
        return res;
    }

    /// <summary>
    /// Buys the house using money amount
    /// </summary>
    /// <param name="houseTlId"></param>
    /// <param name="money"></param>
    /// <param name="character"></param>
    /// <returns>Returns true if successful</returns>
    public bool BuyHouse(ushort houseTlId, uint money, Character character)
    {
        var house = GetHouseByTlId(houseTlId);

        if (house == null)
        {
            // Invalid house
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }

        using var persist = mailManager.DeferPersist();
        lock (house.LifecycleSyncRoot)
            return BuyHouseLocked(house, money, character);
    }

    private bool BuyHouseLocked(House house, uint money, Character character)
    {
        if (house.SellPrice <= 0)
        {
            // House wasn't for sale
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotForSale);
            return false;
        }

        if (house.SellPrice != money)
        {
            // House price changed
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsSaleInfoChanged);
            return false;
        }

        if (house.SellToPlayerId != 0 && house.SellToPlayerId != character.Id)
        {
            // Not a valid buyer
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotDesignatedBuyer);
            return false;
        }

        if (house.OwnerId == character.Id)
        {
            // Cannot buy own building
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsOwner);
            return false;
        }

        // NOTE: check tax due maybe ?

        if (character.Money < house.SellPrice)
        {
            // Not enough money
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotEnoughMoney);
            return false;
        }

        var purchasePreparation = PrepareOwnershipTransfer(
            () => character.SubtractMoney(SlotType.Inventory, (int)house.SellPrice, ItemTaskType.BuyHouse),
            () => butlerManager.UnbindHouse(house.Id),
            () =>
            {
                if (!character.AddMoney(SlotType.Inventory, house.SellPrice, ItemTaskType.BuyHouse))
                    Logger.Error("BuyHouse: failed to refund {0} copper to character {1} after farmhand unbind failed",
                        house.SellPrice, character.Id);
            });
        if (purchasePreparation == HousePurchasePreparation.PaymentFailed)
        {
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotEnoughMoney);
            return false;
        }

        if (purchasePreparation == HousePurchasePreparation.ButlerUnbindFailed)
        {
            character.SendErrorMessage(ErrorMessageType.InternalError);
            return false;
        }

        var previousOwner = house.OwnerId;
        var previousOwnerName = nameManager.GetCharacterName(previousOwner);

        // Mail confirmation mail to new owner
        var newOwnerMail = new BaseMail
        {
            MailType = MailType.HousingSale,
            Header =
            {
                ReceiverId = character.Id,
                SenderName = ".houseBought"
            },
            ReceiverName = character.Name,
            Title = "title(" + zoneManager.GetZoneByKey(house.Transform.ZoneId)?.GroupId.ToString() + ",'" + house.Name + "')",
            Body =
            {
                Text = "body('" + previousOwnerName + "', '" + house.Name + "', " + house.SellPrice.ToString() + ")",
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow.AddMilliseconds(1)
            }
        };
        newOwnerMail.Send();

        // Send sales money to previous owner
        var profitMail = new BaseMail
        {
            MailType = MailType.HousingSale,
            Header =
            {
                ReceiverId = previousOwner,
                SenderName = ".houseSold"
            },
            ReceiverName = previousOwnerName,
            Title = "title('" + character.Name + "','" + house.Name + "')",
            Body =
            {
                Text = "body('" + character.Name + "', '" + house.Name + "', " + house.SellPrice.ToString() + ")",
                CopperCoins = (int)house.SellPrice, // add the money
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow.AddMilliseconds(1)
            }
        };
        profitMail.Send();

        ReturnHouseItemsToOwner(house, false, false, character);

        // Set new owner info
        house.SellPrice = 0;
        house.SellToPlayerId = 0;
        house.AccountId = character.AccountId;
        house.SellPublic = true;
        house.OwnerId = character.Id;
        house.CoOwnerId = character.Id; // not entirely sure if this actually needs to change
        house.Permission = house.Template.AlwaysPublic ? HousingPermission.Public : HousingPermission.Private;
        UpdateHouseFaction(house, character.Faction.Id);
        UpdateTaxInfo(house); // send tax due mails etc. if needed ...

        // TODO: broadcast changes
        house.BroadcastPacket(
            new SCHouseSoldPacket(
                house.TlId,
                previousOwner,
                character.Id,
                character.AccountId,
                character.Name,
                house.Name), false);

        SetForSaleMarkers(house, false);
        SaveHouseNow(house);

        character.SendPacket(new SCHouseDataPacket([house]));
        var oldOwner = worldManager.GetCharacterById(previousOwner);
        if (oldOwner is { IsOnline: true })
            oldOwner.SendPacket(new SCHouseRemovedPacket(house.TlId));

        UpdateFurnitureOwner(house, character.Id, character.Faction.Id);

        house.IsDirty = true;

        return true;
    }

    internal static HousePurchasePreparation PrepareOwnershipTransfer(Func<bool> chargeBuyer,
        Func<bool> unbindSellerButler, Action refundBuyer)
    {
        if (!chargeBuyer())
            return HousePurchasePreparation.PaymentFailed;
        if (unbindSellerButler())
            return HousePurchasePreparation.Success;
        refundBuyer();
        return HousePurchasePreparation.ButlerUnbindFailed;
    }

    internal enum HousePurchasePreparation
    {
        Success,
        PaymentFailed,
        ButlerUnbindFailed
    }

    private static T WithPersistenceOperation<T>(Func<T> operation)
    {
        var entered = !PersistenceGate.IsOperationHeld;
        if (entered)
            PersistenceGate.EnterOperation();
        try
        {
            return operation();
        }
        finally
        {
            if (entered)
                PersistenceGate.ExitOperation();
        }
    }

    /// <summary>
    /// Ticker function for checking all houses if they need tax mails sent
    /// </summary>
    public void CheckHousingTaxes()
    {
        if (_isCheckingTaxTiming)
            return;
        _isCheckingTaxTiming = true;
        try
        {
            // Logger.Trace("CheckHousingTaxes");
            var expiredHouseList = new List<House>();
            foreach (var house in _houses)
            {
                if (house.Value?.ProtectionEndDate <= DateTime.UtcNow && house.Value?.OwnerId > 0
                    && !_wreckedHouses.ContainsKey(house.Key))
                    expiredHouseList.Add(house.Value);
                UpdateTaxInfo(house.Value);
            }
            foreach (var house in expiredHouseList)
            {
                Demolish(null, house, true, false);
            }

            TickDemolitionShell();
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }

        _isCheckingTaxTiming = false;
    }

    /// <summary>
    /// Places a piece of furniture at a given location, using item and design
    /// </summary>
    /// <param name="player"></param>
    /// <param name="houseTlId"></param>
    /// <param name="designId"></param>
    /// <param name="pos"></param>
    /// <param name="quat"></param>
    /// <param name="parentObjId"></param>
    /// <param name="itemId"></param>
    /// <returns></returns>
    public bool DecorateHouse(Character player, ushort houseTlId, uint designId, Vector3 pos, Quaternion quat, uint parentObjId, ulong itemId)
    {
        // Check Player
        if (player == null)
            return false;

        // Check Item
        var item = itemManager.GetItemByItemId(itemId);
        if (item == null || item.OwnerId != player.Id || !AuctionHouseRules.IsPlayerHeldItem(item))
        {
            // Invalid Item
            return false;
        }

        // Check House
        var house = GetHouseByTlId(houseTlId);
        if (house == null || house.TlId != houseTlId)
        {
            // Invalid House
            player.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }

        var itemUcc = uccManager.GetUccFromItem(item);

        // Create decoration doodad
        var decorationDesign = HousingGameData.Instance.GetDecorationDesignFromId(designId);

        /*
        if (item.TemplateId != decorationDesign.ItemTemplateId)
        {
            player.SendErrorMessage(ErrorMessageType.FailedToUseItem);
            return false;
        }
        */

        var doodad = doodadManager.Create(house.ParentWorld, 0, decorationDesign.DoodadId, house, true);
        doodad.Transform.Parent = house.Transform;
        doodad.Transform.Local.SetPosition(pos.X, pos.Y, pos.Z);
        doodad.Transform.Local.ApplyFromQuaternion(quat);
        doodad.ItemTemplateId = item.TemplateId; // designId;
        doodad.ItemId = item.Template.MaxCount <= 1 ? itemId : 0;
        doodad.OwnerDbId = house.Id;

        if (house.Id > 0 && item is BigFish fish)
        {
            var weight = (short)fish.Weight;
            var length = (short)fish.Length;
            doodad.Data = (length << 16) + weight;
        }

        doodad.OwnerId = player.Id;
        doodad.ParentObjId = house.ObjId;
        doodad.ParentObj = house;
        doodad.AttachPoint = AttachPointKind.None;
        doodad.OwnerType = DoodadOwnerType.Housing;
        doodad.UccId = itemUcc?.Id ?? 0;
        doodad.IsPersistent = true;

        if (doodad is DoodadCoffer coffer)
        {
            coffer.InitializeCoffer(player.Id);
        }

        doodad.InitDoodad();
        doodad.Spawn();
        doodad.Save();

        bool res;
        if (item.Template.MaxCount > 1)
        {
            // Stackable items are simply consumed
            res = player.Inventory.Bag.ConsumeItem(ItemTaskType.DoodadCreate, item.TemplateId, 1, item) == 1;
        }
        else
        {
            // Non-stackable items are stored in the owner's system container as to retain crafter information and such 
            res = player.Inventory.SystemContainer.AddOrMoveExistingItem(ItemTaskType.DoodadCreate, item);
        }

        // Logger.Debug($"DecorateHouse => DoodadTemplate: {doodad.TemplateId} , DoodadId {doodad.ObjId}, Pos: {doodad.Transform}");
        return res;
    }

    /// <summary>
    /// Toggles the allow furniture recovery flag
    /// </summary>
    /// <param name="character"></param>
    /// <param name="houseTl"></param>
    public void HousingToggleAllowRecover(Character character, ushort houseTl)
    {
        var house = GetHouseByTlId(houseTl);
        if (house == null)
            return;
        if (character.Id != house.OwnerId)
            return;
        house.AllowRecover = !house.AllowRecover;
        house.BroadcastPacket(new SCHousingRecoverTogglePacket(house.TlId, house.AllowRecover), false);
    }

    /// <summary>
    /// Returns a house where the given position falls within boundaries of the house 
    /// </summary>
    /// <param name="world">World instance containing the position.</param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns>Target House or Null</returns>
    public House GetHouseAtLocation(WorldInstance world, float x, float y)
    {
        if (world == null)
            return null;

        // TODO: Check if all houses actually use a square shape aligned to grid
        foreach (var h in _houses)
        {
            var house = h.Value;
            if (house.ParentWorld != world)
                continue;
            // 10.x: plot bounds from housing_sizes.garden_radius
            var r = house.Template?.GardenRadius ?? 0f;
            var bounds = new RectangleF(house.Transform.World.Position.X - r, house.Transform.World.Position.Y - r,
                r * 2f, r * 2f);
            if (bounds.Contains(x, y))
                return house;
        }
        return null;
    }
}
