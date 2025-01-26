using System.Collections.Generic;
using AAEmu.Commons.Network;
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
    /// Server-side Exp multiplier (on top of buffs)
    /// </summary>
    public double ExpRate { get; set; } = 1.0;
    /// <summary>
    /// Server-side Honor Points multiplier (on top of buffs)
    /// </summary>
    public double HonorRate { get; set; } = 1.0;
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
