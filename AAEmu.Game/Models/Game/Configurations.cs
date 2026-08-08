using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Team;
// ReSharper disable ClassNeverInstantiated.Global

namespace AAEmu.Game.Models.Game;

public class Configurations : PacketMarshaler
{
    public string Key { get; set; }
    public string Value { get; set; }
}

public class WorldConfig
{
    /// <summary>
    /// Message of the Day that gets displayed in player's chat upon login
    /// </summary>
    public string MOTD { get; set; } = "";

    /// <summary>
    /// Message shown to the player when they exit the game
    /// </summary>
    public string LogoutMessage { get; set; } = "";

    /// <summary>
    /// Time in minutes between user data Save events
    /// </summary>
    public double AutoSaveInterval { get; set; } = 5.0;

    /// <summary>
    /// Interval in milliseconds between batched team-member status and map-position updates.
    /// </summary>
    public int TeamRemoteMemberUpdateIntervalMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Loot allocation method assigned when a new team is created. LootMaster is not valid here
    /// because a specific member must be selected after creation.
    /// </summary>
    public LootingRuleMethod DefaultTeamLootMethod { get; set; } = LootingRuleMethod.RotateWinner;

    /// <summary>
    /// Minimum item grade that requires a roll for newly created teams.
    /// </summary>
    public sbyte DefaultTeamLootMinimumGrade { get; set; } = 2;

    /// <summary>
    /// Whether bind-on-pickup items require a roll for newly created teams.
    /// </summary>
    public bool DefaultTeamRollForBindOnPickup { get; set; } = true;

    /// <summary>
    /// Server-side Exp multiplier (on top of buffs)
    /// </summary>
    public double ExpRate { get; set; } = 1.0;

    /// <summary>
    /// Highest player level exposed from the content database.
    /// </summary>
    public byte PlayerLevelCap { get; set; } = 55;

    /// <summary>
    /// Highest mate (mount or pet) level exposed from the content database.
    /// </summary>
    public byte MateLevelCap { get; set; } = 50;

    /// <summary>
    /// Server-side Honor Points multiplier (on top of buffs)
    /// </summary>
    public double HonorRate { get; set; } = 1.0;

    /// <summary>
    /// Separate multiplier for PvP Honor Points (kills in Conflict/War zones). Independent of HonorRate.
    /// </summary>
    public double PvpHonorRate { get; set; } = 1.0;

    /// <summary>
    /// Server-side Vocation Badge multiplier (on top of buffs)
    /// </summary>
    public double VocationRate { get; set; } = 1.0;

    /// <summary>
    /// Multiplier for the loot dice (some loot types are not affected by this)
    /// </summary>
    public double LootRate { get; set; } = 1.0;

    /// <summary>
    /// Multiplier for gold that is obtained through loot drops
    /// </summary>
    public double GoldLootMultiplier { get; set; } = 1.0;

    /// <summary>
    /// Multiplier for growth rate of doodads, note that this only affects steps marked as growth and not those with a simple timer.
    /// </summary>
    public double GrowthRate { get; set; } = 1.0;

    /// <summary>
    /// Number of days 1 week worth of tax pays for, set this to 3640 would make 1 tax payment last for about 10 years.
    /// </summary>
    public uint DaysForTaxPayment { get; set; } = 7u;

    /// <summary>
    /// Set a minimum access-level that a character must have to ignore falling damage (for devs)
    /// </summary>
    public int IgnoreFallDamageAccessLevel { get; set; } = 100;

    /// <summary>
    /// When enabled, players take no damage at all
    /// </summary>
    public bool GodMode { get; set; }

    /// <summary>
    /// Enables the loading of NavMesh data for dungeons
    /// </summary>
    public bool GeoDataMode { get; set; }

    /// <summary>
    /// When false, heightmaps get loaded on-demand only. Should increase boot times and lower memory use
    /// </summary>
    // TODO: Also apply this to missionX.bai files
    public bool PreLoadTerrain { get; set; }

    /// <summary>
    /// When false, a world's zone navmesh (<c>*.bai</c>) is loaded the first time something asks
    /// for it instead of for every world template at boot. Boot otherwise walks all world
    /// templates — including <c>backup_</c>, <c>test_</c>, <c>machinima_</c> and every instance
    /// world — and holds all of it resident for the process lifetime. Default: false (lazy).
    /// Only consulted when <see cref="GeoDataMode"/> is enabled.
    /// Configure in <c>AAEmu.Game/Configurations/World.json</c> under <c>World.PreLoadNavmesh</c>.
    /// </summary>
    public bool PreLoadNavmesh { get; set; }

    /// <summary>
    /// Maximum number of instances that can be created (includes system instances)
    /// </summary>
    public uint MaxInstances { get; set; } = 32;

    /// <summary>
    /// Server-side Actability Points multiplier (on top of buffs)
    /// </summary>
    public double ActabilityRate { get; set; } = 1.0;

    /// <summary>
    /// When true, housing bound doodads (doors, windows, planters, drills, animals) are saved to the
    /// database and their state (open/closed, fill level, growth phase) is restored on server restart.
    /// When false (default), bound doodads are re-created fresh from template data on every restart,
    /// matching the original behaviour.
    /// Configure in <c>AAEmu.Game/Configurations/World.json</c> under <c>World.UsePersistentHouseDoodads</c>.
    /// </summary>
    public bool UsePersistentHouseDoodads { get; set; } = false;

    /// <summary>
    /// When false, world doodad spawners and persistent doodads (including player-placed doodads)
    /// are not spawned at world load. Diagnostic toggle for isolating world-entry behaviour from
    /// doodad spawn data. Default: true.
    /// Configure in <c>AAEmu.Game/Configurations/World.json</c> under <c>World.SpawnDoodads</c>.
    /// </summary>
    public bool SpawnDoodads { get; set; } = true;

    /// <summary>
    /// When false, transfers (carriages, airships) are not spawned at world load. Diagnostic
    /// toggle for isolating world-entry behaviour from transfer spawn data. Default: true.
    /// Configure in <c>AAEmu.Game/Configurations/World.json</c> under <c>World.SpawnTransfers</c>.
    /// </summary>
    public bool SpawnTransfers { get; set; } = true;

    /// <summary>
    /// When false, gimmicks are not spawned at world load. Diagnostic toggle for isolating
    /// world-entry behaviour from gimmick spawn data. Default: true.
    /// Configure in <c>AAEmu.Game/Configurations/World.json</c> under <c>World.SpawnGimmicks</c>.
    /// </summary>
    public bool SpawnGimmicks { get; set; } = true;

    /// <summary>
    /// When false, slaves (player boats, vehicles) are not spawned at world load. Diagnostic
    /// toggle for isolating world-entry behaviour from slave spawn data. Default: true.
    /// Configure in <c>AAEmu.Game/Configurations/World.json</c> under <c>World.SpawnSlaves</c>.
    /// </summary>
    public bool SpawnSlaves { get; set; } = true;
}

public class DungeonLoadConfig
{
    public string Name { get; set; } = string.Empty;
    public uint Channel { get; set; } = 0;
    public uint Id { get; set; } = 0;
}

public class DungeonsConfig
{
    /// <summary>
    /// If people are kicked from a dungeon and there are no people left,
    /// should the system automatically remove the dungeon instance (default=yes, retail=no) 
    /// </summary>
    public bool AutoCleanupAfterKick { get; set; } = true;

    /// <summary>
    /// Time in seconds after being removed from a party in a dungeon before you get kicked out
    /// </summary>
    public int AutoTeamDisbandKickTime { get; set; } = 30;

    /// <summary>
    /// List of dungeon instances that should be created by default
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<DungeonLoadConfig> AutoCreate { get; set; } = [];
}

public class AccountDeleteDelayTiming
{
    /// <summary>
    /// Minimum Level this timing applies to
    /// </summary>
    public int Level { get; set; }
    /// <summary>
    /// Delay in minutes that needs to be used if this character is at least this level
    /// </summary>
    public int Delay { get; set; }
}

public class AccountConfig
{
    /// <summary>
    /// Allowed Regex for account names
    /// </summary>
    public string NameRegex { get; set; } = "^[a-zA-Z0-9]{1,18}$";
    /// <summary>
    /// Marks if a deleted character's name can be re-used for a new character
    /// </summary>
    public bool DeleteReleaseName { get; set; } = false;
    // ReSharper disable once CollectionNeverUpdated.Global
    // Populated by JSON reader
    /// <summary>
    /// Delete character settings
    /// </summary>
    public List<AccountDeleteDelayTiming> DeleteTimings { get; set; } = [];
    /// <summary>
    /// Default access-level for new accounts
    /// </summary>
    public int AccessLevelDefault { get; set; } = 0;
    /// <summary>
    /// Access-Level that should be used for the first created account on the server regardless of other settings
    /// </summary>
    public int AccessLevelFirstAccount { get; set; } = 100;
    /// <summary>
    /// Access-Level that should be used for the first created character on the server regardless of other settings
    /// </summary>
    public int AccessLevelFirstCharacter { get; set; } = 100;

    /// <summary>
    /// Grants every character the highest grade in premium_grades instead of deriving it from
    /// characters.point. Off by default, so the point thresholds (1, 50, 125, 225, 400) keep deciding.
    /// </summary>
    /// <remarks>
    /// Turning this on is what "everyone is a max Patron" means: it moves the whole account off the
    /// free tier (grade 1, which premium_grades gives max_labor = 0, so the account/"Offline" pool
    /// does not exist for it) onto the top grade and its 6000/5000 caps and 15/10 regeneration.
    /// The grade also travels in UnitState, so the client's own labor cap display follows it.
    /// </remarks>
    public bool ForceMaxPremiumGrade { get; set; } = false;
}

public class CurrencyValuesConfig
{
    public int Default { get; set; } = 0;
    public int DailyLogin { get; set; } = 0;
    public int TickMinutes { get; set; } = 5;
    public int TickAmount { get; set; } = 0;
    public int TickAmountPremium { get; set; } = 0;

    public int GetTickAmount(bool isPremium)
    {
        return isPremium ? TickAmountPremium : TickAmount;
    }
}

public class SpecialtyConfig
{
    /// <summary>
    /// Maximum distance in metres at which a character may use a specialty outlet.
    /// </summary>
    public float InteractionRange { get; set; } = 3f;

    /// <summary>
    /// Base labor charged when a specialty pack is delivered, before Commerce proficiency reduction.
    /// </summary>
    public int SellLaborCost { get; set; } = 60;

    /// <summary>
    /// Delayed-delivery interest added to specialty proceeds, as a percentage.
    /// </summary>
    public int InterestRate { get; set; } = 5;

    /// <summary>
    /// Seller share when a different character crafted the pack.
    /// </summary>
    public float SellerShare { get; set; } = 0.8f;

    /// <summary>
    /// Maximum rate for speciality packs
    /// </summary>
    public int MaxSpecialtyRatio { get; set; } = 130;
    /// <summary>
    /// Minimum rate for speciality packs
    /// </summary>
    public int MinSpecialtyRatio { get; set; } = 70;
    /// <summary>
    /// Amount the trade in rate lowers for each traded pack
    /// </summary>
    public double RatioDecreasePerPack { get; set; } = 0.5f;
    /// <summary>
    /// Number of % a trade recovers every X time
    /// </summary>
    public double RatioIncreasePerTick { get; set; } = 5.0;
    /// <summary>
    /// Number of minutes between trade rate updates when selling packs
    /// </summary>
    public double RatioDecreaseTickMinutes { get; set; } = 1f;
    /// <summary>
    /// Time in minutes before a traded pack is no longer counted towards the trade rate calculation
    /// </summary>
    public double RatioRegenTickMinutes { get; set; } = 60f;

    /// <summary>
    /// Time in minutes to delay trade pack reward mail delivery. Default is 8 hours.
    /// </summary>
    /// <remarks>
    /// The default value is 8 hours. This setting controls how long after delivery 
    /// a player must wait before receiving their trade pack reward via mail.
    /// </remarks>
    public double TradePackMailDelayInMinutes { get; set; } = 480f;
}

public class UccConfig
{
    /// <summary>
    /// Revision of the client's LevelDB UCC cache. Client 10.0.2.13 substitutes revision 5 when
    /// SCInitialConfig carries zero, so 5 is the explicit native revision for this protocol target.
    /// </summary>
    public byte CacheVersion { get; set; } = 5;

    /// <summary>
    /// Gold charged when a completed emblem upload is converted into Crest Ink. The 10.0 client
    /// transmits no price and game content has no Crest Ink price row, so this remains server policy.
    /// </summary>
    public int CrestInkCreationCost { get; set; } = 50000;
}

public class FeaturesConfig
{
    /// <summary>
    /// fset feature bits applied at boot, keyed by <c>Feature</c> enum name (case-insensitive).
    /// A key the enum does not define, or one that lands in a scalar byte, is reported as an error at
    /// startup instead of being applied.
    /// Configure in <c>AAEmu.Game/Configurations/Features.json</c> under <c>Features.Flags</c>.
    /// </summary>
    public Dictionary<string, bool> Flags { get; set; } = [];

    /// <summary>
    /// Pay house tax with tax certificates instead of gold. Was 1.2 fset bit 59 (taxItem); 10.0.2.13
    /// dropped the bit from the blob, so this is a plain server switch read by HousingManager and
    /// MailManager.
    /// Configure in <c>AAEmu.Game/Configurations/Features.json</c> under <c>Features.TaxItem</c>.
    /// </summary>
    public bool TaxItem { get; set; } = true;

    /// <summary>
    /// Split specialty pack profit with the crafter. Was 1.2 fset bit 56 (backpackProfitShare);
    /// 10.0.2.13 dropped the bit from the blob, so this is a plain server switch read by SpecialtyManager.
    /// Configure in <c>AAEmu.Game/Configurations/Features.json</c> under <c>Features.BackpackProfitShare</c>.
    /// </summary>
    public bool BackpackProfitShare { get; set; } = true;
}

/// <summary>
/// Server policy carried by SCInitialConfigPacket (opcode 0x007). Only values the server decides live
/// here; everything the server already knows — the feature blob, starting labor, the account's premium
/// window, the War-zone honor rate — is read from its owning source at send time.
/// Configure in <c>AAEmu.Game/Configurations/InitialConfig.json</c> under <c>InitialConfig</c>.
/// </summary>
public class InitialConfig
{
    /// <summary>
    /// CryEngine level loaded behind the character-selection lobby. This is a zone level name, not the
    /// server's logical <c>main_world</c> template name.
    /// </summary>
    public string LobbyLevel { get; set; } = "w_hanuimaru_1";

    /// <summary>
    /// Optional u64 content checksum carried by the SetGameType proxy message. Zero disables the legacy
    /// checksum comparison, matching the development client/session protocol.
    /// </summary>
    public ulong LobbyLevelChecksum { get; set; }

    /// <summary>Whether the SetGameType proxy message requests CryEngine immersive mode.</summary>
    public bool LobbyImmersiveMode { get; set; } = true;

    /// <summary>
    /// Host the client resolves web content against. Backs the useUrlLink, eventWebLink and use_web_*
    /// features; those open nothing while this points at a host that does not serve them.
    /// </summary>
    public string Host { get; set; } = "aaemu.local";

    /// <summary>Host serving the in-game cash shop. Backs the ingamecashshop feature.</summary>
    public string CashHost { get; set; } = "";

    /// <summary>Host serving the second-password / security portal. Backs the secondpass feature.</summary>
    public string SecurityHost { get; set; } = "";

    /// <summary>
    /// Characters the client fetches per page of the character list, read in candidatelist.lua through
    /// <c>X2:GetCandidateOnceRetrieveCount()</c>. Only consulted while the useCharacterListPage feature
    /// is enabled; 0 fetches the list in one go.
    /// </summary>
    public int CandidateRetrieveCount { get; set; }

    /// <summary>Allow placing houses and farms.</summary>
    public bool CanPlaceHouse { get; set; } = true;

    /// <summary>Allow paying house and farm tax.</summary>
    public bool CanPayTax { get; set; } = true;

    /// <summary>Allow using the auction house.</summary>
    public bool CanUseAuction { get; set; } = true;

    /// <summary>Allow player-to-player trade.</summary>
    public bool CanTrade { get; set; } = true;

    /// <summary>Allow sending mail.</summary>
    public bool CanSendMail { get; set; } = true;

    /// <summary>Allow using the bank / warehouse.</summary>
    public bool CanUseBank { get; set; } = true;

    /// <summary>Allow spending copper.</summary>
    public bool CanUseCopper { get; set; } = true;

    /// <summary>
    /// Second-password attempts before the client stops asking. Only consulted while the secondpass
    /// feature is enabled.
    /// </summary>
    public byte SecondPasswordMaxFailCount { get; set; }

    /// <summary>Milliseconds of inactivity before the client disconnects itself. Default: 10 minutes.</summary>
    public int IdleKickTime { get; set; } = 600000;

    /// <summary>Character slots the client offers a premium account.</summary>
    public byte PremiumMaxCharacterSlots { get; set; } = 4;

    /// <summary>
    /// Premium-service membership domain used by the client when accepting service URLs. Native
    /// ClientPlayer initializes this to 1; it is independent of the account payment method.
    /// </summary>
    public byte MemberType { get; set; } = 1;

    /// <summary>
    /// UnitDistance <c>over_distance</c> threshold in metres, copied to ClientPlayer+0x3DFC. target.lua
    /// shows "???" for anything further away, so 0 blanks every target. Retail sniff
    /// archeage_20260702_001757 SCInitialConfig = 256.0.
    /// </summary>
    public float BigModelDistance { get; set; } = 256.0f;

    /// <summary>Character slots the client offers this account.</summary>
    public byte MaxCharacterSlots { get; set; } = 4;

    /// <summary>
    /// Marks the session as a development build, which unlocks the client's debug UI and relaxes its
    /// content checks. Enabled by default for the 10.0.2.13 migration work.
    /// </summary>
    public bool IsDev { get; set; } = true;
}

/// <summary>
/// normal and Ancestral/Heir levels before comparing these values.
/// </summary>
public class LevelRestrictionConfig
{
    public byte AuctionSearchLevel { get; set; } = 10;
    public byte AuctionBidLevel { get; set; } = 10;
    public byte AuctionPostLevel { get; set; } = 10;
    public byte TradeLevel { get; set; } = 10;
    public byte MailLevel { get; set; } = 10;
    public byte PermissionLevel { get; set; }
    public byte OtherLevel { get; set; }
    public ChatLevelRestrictionConfig Chat { get; set; } = new();

    public byte GetChatLevel(ChatType type) => type switch
    {
        ChatType.White => Chat.White,
        ChatType.Shout => Chat.Shout,
        ChatType.Trade => Chat.Trade,
        ChatType.GroupFind => Chat.GroupFind,
        ChatType.Party => Chat.Party,
        ChatType.Raid => Chat.Raid,
        ChatType.Region => Chat.Region,
        ChatType.Clan => Chat.Clan,
        ChatType.System2 => Chat.System,
        ChatType.Family => Chat.Family,
        ChatType.RaidLeader => Chat.RaidLeader,
        ChatType.Judge => Chat.Judge,
        ChatType.Ally => Chat.Ally,
        ChatType.User => Chat.User,
        _ => 0
    };
}

/// <summary>
/// The twenty u8 <c>limitLevels</c> entries indexed by the native chat channel number. Reserved indices are
/// named explicitly so configuration always maps one-to-one to the wire instead of relying on array padding.
/// </summary>
public class ChatLevelRestrictionConfig
{
    public byte White { get; set; }
    public byte Shout { get; set; } = 15;
    public byte Trade { get; set; } = 15;
    public byte GroupFind { get; set; } = 15;
    public byte Party { get; set; }
    public byte Raid { get; set; }
    public byte Region { get; set; } = 15;
    public byte Clan { get; set; }
    public byte System { get; set; }
    public byte Family { get; set; }
    public byte RaidLeader { get; set; }
    public byte Judge { get; set; }
    public byte Reserved12 { get; set; }
    public byte Reserved13 { get; set; }
    public byte Ally { get; set; } = 15;
    public byte User { get; set; }
    public byte Reserved16 { get; set; }
    public byte Reserved17 { get; set; }
    public byte Reserved18 { get; set; }
    public byte Reserved19 { get; set; }
}

public class ScriptsConfig
{
    public LoadStrategyType LoadStrategy { get; set; } = LoadStrategyType.Reflection;

    public enum LoadStrategyType
    {
        Compilation,
        Reflection
    }
}
