using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Hero;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Mobilization orders: a hero calling their nation to an assembly point.
/// </summary>
/// <remarks>
/// A hero interacts with the issuance doodad, the nation gets a popup naming them, and whoever accepts
/// is summoned to their nation's assembly point. Issuing counts twice - against a budget of five a day,
/// and against the fifty a term that the Mission Status tab tracks toward the hero bonus chest.
///
/// Almost all of the popup is client-side. The issuance window's conditions, assembly point and daily
/// cap come from the client's own data, and the summon popup takes its wording from there too; the
/// server supplies the counters, the broadcast, and who is being summoned where. That is why the button
/// was dead with nothing implemented - see SCHeroMobilizationOrderUpdated - rather than merely inert.
/// </remarks>
public class MobilizationOrderManager : Singleton<MobilizationOrderManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Orders one hero may issue per day.
    /// </summary>
    /// <remarks>
    /// Five, which is what the client shows as "Mobilization Orders Rem. 0/5" and enforces on the button
    /// before it will send anything. Held here as well because the client's copy is a courtesy, not a
    /// gate - a crafted request would otherwise be unlimited.
    /// </remarks>
    public const int DailyLimit = 5;

    /// <summary>
    /// How long an order stays answerable.
    /// </summary>
    /// <remarks>
    /// Sixty seconds, matching the popup's own countdown - mobilization_order.lua ticks openSecond down
    /// and sends TIME_OVER when it reaches zero, and handle_task.lua's variant hardcodes 60 * 1000. An
    /// answer arriving after that is a client that was slower than its own timer, and summoning on it
    /// would drop somebody at an assembly point long after the call.
    /// </remarks>
    private static readonly TimeSpan OrderLifetime = TimeSpan.FromSeconds(60);

    /// <summary>Where a nation's mobilization order gathers people.</summary>
    /// <param name="ZoneKey">The zone the point is in; its group is what the popup names.</param>
    private readonly record struct AssemblyPoint(float X, float Y, float Z, float Yaw, uint ZoneKey);

    /// <summary>
    /// The assembly point each nation is called to.
    /// </summary>
    /// <remarks>
    /// Fixed spots rather than the issuing doodad's own position. The doodad was the obvious source -
    /// the client fills the window's "Assembly Point" row from it - but a doodad's position is the
    /// middle of its collision, so summoning onto it left the arriving player stuck inside the thing
    /// they had been called to.
    ///
    /// These three were picked in-game, standing where an arriving raid should land: outside the
    /// building, on walkable ground, near the hero hall each nation musters at. They are all in
    /// main_world, so a summon is a within-level move.
    ///
    /// Keyed by NATION, which is the top-level faction - 114 has no FactionsEnum member because
    /// characters carry the member faction 161 (Pirate) and reach 114 through its mother id.
    /// </remarks>
    private static readonly Dictionary<uint, AssemblyPoint> AssemblyPoints = new()
    {
        [(uint)FactionsEnum.NuiaAlliance] = new(11118.1f, 12121.9f, 142.8f, 3f, 183),      // Marianople
        [(uint)FactionsEnum.HaranyaAlliance] = new(16798.5f, 9160.7f, 127.5f, -1f, 191),   // Solis Headlands
        [114] = new(15022.2f, 22927.2f, 101.6f, 4f, 284)                                   // Stormraw Sound, Outlaw
    };

    private readonly Lock _gate = new();

    /// <summary>characterId -> counters, loaded on demand and written through on every change.</summary>
    private readonly Dictionary<uint, Counters> _byCharacter = [];

    /// <summary>heroId -> the order they have in the air, while it is still answerable.</summary>
    private readonly Dictionary<uint, ActiveOrder> _active = [];

    /// <summary>An order that has gone out and can still be accepted.</summary>
    /// <param name="NationId">Who was called. An answer from outside it is refused.</param>
    /// <param name="Point">Where accepting puts you.</param>
    /// <param name="Expires">When the popup's own countdown runs out.</param>
    private readonly record struct ActiveOrder(uint NationId, AssemblyPoint Point, DateTime Expires);

    private sealed class Counters
    {
        public uint Season { get; set; }
        public DateTime Day { get; set; }
        public int Today { get; set; }
        public int Total { get; set; }
    }

    /// <summary>How many orders a hero has issued today and this term.</summary>
    public (int Today, int Total) CountsFor(uint characterId)
    {
        var counters = Load(characterId);
        return (counters.Today, counters.Total);
    }

    /// <summary>
    /// Handles a hero pressing Confirm on the issuance window.
    /// </summary>
    public void Issue(Character hero, uint doodadObjId)
    {
        if (hero == null)
            return;

        if (!HeroManager.Instance.IsHero(hero.Id))
        {
            Logger.Warn("MobilizationOrder: {0} is not a hero", hero.Name);
            return;
        }

        var counters = Load(hero.Id);
        if (counters.Today >= DailyLimit)
        {
            // The client greys its own button at this point, so arriving here means its counter and ours
            // disagree. Re-send ours rather than only refusing, so the two stop disagreeing.
            Logger.Info("MobilizationOrder: {0} has used all {1} of today's orders", hero.Name, DailyLimit);
            HeroManager.Instance.SendMobilizationOrders(hero);
            return;
        }

        var nationId = HeroManager.NationOf(hero);
        if (!AssemblyPoints.TryGetValue(nationId, out var point))
        {
            Logger.Warn("MobilizationOrder: nation {0} has no assembly point; {1}'s order not sent",
                nationId, hero.Name);
            return;
        }

        // The zone group is the one the assembly point sits in, not the one the doodad does, so the
        // popup names the place people will actually arrive at.
        var zoneGroupId = ZoneManager.Instance.GetZoneByKey(point.ZoneKey)?.GroupId ?? 0;
        var sent = Broadcast(hero, nationId, zoneGroupId);

        lock (_gate)
            _active[hero.Id] = new ActiveOrder(nationId, point, DateTime.UtcNow + OrderLifetime);

        counters.Today++;
        counters.Total++;
        Save(hero.Id, counters);

        hero.SendPacket(new SCFactionMobilizationOrderSuccessPacket());
        HeroManager.Instance.SendMobilizationOrders(hero);

        Logger.Info("MobilizationOrder: {0} summoned nation {1} to zone group {2}; {3} recipient(s), {4}/{5} today",
            hero.Name, nationId, zoneGroupId, sent, counters.Today, DailyLimit);
    }

    /// <summary>MOBILIZATION_ORDER_RESULT.ACCEPT, from mobilization_order.lua.</summary>
    private const uint ResultAccept = 1;

    /// <summary>
    /// Handles one recipient answering the summon popup, and summons them if they accepted.
    /// </summary>
    /// <remarks>
    /// An answer always arrives, including refusals: the popup reports through OnHide, so the No button
    /// and simply closing the window both send CANCEL, and the countdown sends TIME_OVER. Two answers for
    /// one popup are normal rather than a fault - the client has two presentations of this dialog and
    /// both report - so anything but the first accept is ignored quietly.
    /// </remarks>
    public void Answer(Character character, uint result, ulong heroId, short zoneGroupType)
    {
        if (character == null)
            return;

        Logger.Debug("MobilizationOrder: {0} answered {1} to hero {2} for zone group {3}",
            character.Name, result, heroId, zoneGroupType);

        if (result != ResultAccept)
            return;

        ActiveOrder order;
        lock (_gate)
        {
            if (!_active.TryGetValue((uint)heroId, out order))
            {
                Logger.Info("MobilizationOrder: {0} accepted an order from {1} that is no longer live",
                    character.Name, heroId);
                return;
            }
        }

        if (DateTime.UtcNow > order.Expires)
        {
            Logger.Info("MobilizationOrder: {0} accepted after the order expired", character.Name);
            return;
        }

        // The order called one nation. Accepting one addressed to another nation is not something the
        // client offers, so this is about a crafted packet rather than a mistake.
        if (HeroManager.NationOf(character) != order.NationId)
        {
            Logger.Warn("MobilizationOrder: {0} is not in nation {1}; not summoned",
                character.Name, order.NationId);
            return;
        }

        Summon(character, order.Point);
    }

    /// <summary>
    /// Puts an accepting player at the assembly point.
    /// </summary>
    /// <remarks>
    /// Moved the same way PortalManager moves someone within a level: set the position and finalize, so
    /// the region change updates the zone and hands the unit to the destination, then tell the client.
    /// The reason is Etc - there is no mobilization entry in TeleportReason and the client only uses it
    /// to pick a loading presentation.
    /// </remarks>
    private static void Summon(Character character, AssemblyPoint point)
    {
        character.SetPosition(point.X, point.Y, point.Z, 0f, 0f, point.Yaw);
        character.Transform.FinalizeTransform();

        character.SendPacket(new SCTeleportUnitPacket(
            TeleportReason.Etc, 0, point.X, point.Y, point.Z, point.Yaw));

        Logger.Info("MobilizationOrder: summoned {0} to {1:0.0} {2:0.0} {3:0.0} in zone {4}",
            character.Name, point.X, point.Y, point.Z, point.ZoneKey);
    }

    /// <summary>Sends the summon popup to everyone online in the nation except the hero.</summary>
    /// <returns>How many clients it reached.</returns>
    private static int Broadcast(Character hero, uint nationId, uint zoneGroupId)
    {
        var sent = 0;
        foreach (var player in WorldManager.Instance.GetAllCharacters())
        {
            if (player.Id == hero.Id || HeroManager.NationOf(player) != nationId)
                continue;

            player.SendPacket(new SCFactionMobilizationOrderPacket(
                (short)zoneGroupId, hero.Id, hero.Name));
            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Reads a hero's counters, rolling them over when the day or the season has moved on.
    /// </summary>
    /// <remarks>
    /// The daily count resets at midnight UTC and the term total resets when the season changes, because
    /// the fifty the bonus asks for is fifty within one term. Both are checked on read rather than by a
    /// scheduled job: a counter nobody is reading does not need to be correct, and this way a server that
    /// was down over the boundary still rolls over properly.
    /// </remarks>
    private Counters Load(uint characterId)
    {
        lock (_gate)
        {
            if (!_byCharacter.TryGetValue(characterId, out var counters))
            {
                counters = ReadRow(characterId);
                _byCharacter[characterId] = counters;
            }

            var today = DateTime.UtcNow.Date;
            var season = HeroSeason.CurrentId;
            var stale = counters.Day != today || counters.Season != season;
            if (!stale)
                return counters;

            if (counters.Season != season)
                counters.Total = 0;

            counters.Today = 0;
            counters.Day = today;
            counters.Season = season;
            return counters;
        }
    }

    private static Counters ReadRow(uint characterId)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT `season`, `day`, `today_count`, `total_count` " +
                "FROM `hero_mobilization_orders` WHERE `character_id` = @c";
            command.Parameters.AddWithValue("@c", characterId);
            command.Prepare();

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new Counters
                {
                    Season = reader.GetUInt32(0),
                    Day = reader.GetDateTime(1).Date,
                    Today = reader.GetInt32(2),
                    Total = reader.GetInt32(3)
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "MobilizationOrder: failed to read counters for character {0}", characterId);
        }

        return new Counters { Season = HeroSeason.CurrentId, Day = DateTime.UtcNow.Date };
    }

    private void Save(uint characterId, Counters counters)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "REPLACE INTO `hero_mobilization_orders` " +
                "(`character_id`, `season`, `day`, `today_count`, `total_count`) " +
                "VALUES (@c, @s, @d, @today, @total)";
            command.Parameters.AddWithValue("@c", characterId);
            command.Parameters.AddWithValue("@s", counters.Season);
            command.Parameters.AddWithValue("@d", counters.Day);
            command.Parameters.AddWithValue("@today", counters.Today);
            command.Parameters.AddWithValue("@total", counters.Total);
            command.Prepare();
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "MobilizationOrder: failed to save counters for character {0}", characterId);
        }
    }
}
