using System.Collections.Concurrent;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Core.Managers;

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatManager : Singleton<ChatManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// nullChannel is used as a fallback channel, do not use directly
    /// </summary>
    private ChatChannel NullChannel { get; }= new() { ChatType = ChatType.White, Faction = 0, InternalName = "Null" };
    private ConcurrentDictionary<FactionsEnum, ChatChannel> FactionChannels { get; } = new();
    private ConcurrentDictionary<long, ChatChannel> NationChannels { get; } = new();
    private ConcurrentDictionary<long, ChatChannel> ZoneChannels { get; } = new();
    private ConcurrentDictionary<long, ChatChannel> PartyChannels { get; } = new();
    private ConcurrentDictionary<long, ChatChannel> RaidChannels { get; }= new();
    private ConcurrentDictionary<FactionsEnum, ChatChannel> GuildChannels { get; }= new();
    private ConcurrentDictionary<long, ChatChannel> FamilyChannels { get; } = new();

    /// <summary>
    /// Creates default channels
    /// </summary>
    public void Initialize()
    {
        Logger.Info("Initializing Chat Manager...");

        // Create Faction Channels
        _ = AddFactionChannel(FactionsEnum.NuiaAlliance, "Nuia");
        _ = AddFactionChannel(FactionsEnum.HaranyaAlliance, "Haranya");
        _ = AddFactionChannel(FactionsEnum.Pirate, "Pirate");
        // TODO: Player Factions ?

        // Create Nation Channels
        _ = AddNationChannel(Race.Nuian, FactionsEnum.NuiaAlliance, "Nuian-Elf-Dwarf");
        _ = AddNationChannel(Race.Hariharan, FactionsEnum.HaranyaAlliance, "Harani-Firran-Warborn");

        // Zone, Party/Raid, Guild, Family channels are created on the fly
    }

    /// <summary>
    /// Used in GM command /testchatchannel list
    /// </summary>
    /// <returns>List of all chat channels currently loaded</returns>
    public List<ChatChannel> ListAllChannels()
    {
        var res = new List<ChatChannel>
        {
            NullChannel
        };
        res.AddRange(FactionChannels.Values);
        res.AddRange(NationChannels.Values);
        res.AddRange(ZoneChannels.Values);
        res.AddRange(PartyChannels.Values);
        res.AddRange(RaidChannels.Values);
        res.AddRange(GuildChannels.Values);
        res.AddRange(FamilyChannels.Values);
        return res;
    }

    /// <summary>
    /// Removes a player from all chat channels
    /// </summary>
    /// <param name="character"></param>
    public void LeaveAllChannels(Character character)
    {
        foreach (var c in FactionChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in NationChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in ZoneChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in PartyChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in RaidChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in GuildChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in FamilyChannels)
            c.Value?.LeaveChannel(character);
    }

    /// <summary>
    /// Removes zone, party, guild, etc. channels that have zero members in them to free up space (and Id's)
    /// </summary>
    public int CleanUpChannels()
    {
        var res = 0;
        foreach (var c in ZoneChannels)
            if (c.Value.Members.Count <= 0)
            {
                ZoneChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in PartyChannels)
            if (c.Value.Members.Count <= 0)
            {
                PartyChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in RaidChannels)
            if (c.Value.Members.Count <= 0)
            {
                RaidChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in GuildChannels)
            if (c.Value.Members.Count <= 0)
            {
                GuildChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in FamilyChannels)
            if (c.Value.Members.Count <= 0)
            {
                FamilyChannels.TryRemove(c.Key, out _);
                res++;
            }
        return res;
    }

    /// <summary>
    /// Creates a faction chat channel
    /// </summary>
    /// <param name="factionId"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    private bool AddFactionChannel(FactionsEnum factionId, string name)
    {
        var channel = new ChatChannel() { ChatType = ChatType.Ally, Faction = factionId, InternalId = (uint)factionId, InternalName = name };
        return FactionChannels.TryAdd(factionId, channel);
    }

    /// <summary>
    /// Gets a faction chat channel by FactionId
    /// </summary>
    /// <param name="factionMotherId"></param>
    /// <returns></returns>
    public ChatChannel GetFactionChat(FactionsEnum factionMotherId)
    {
        return FactionChannels.GetValueOrDefault(factionMotherId, NullChannel);
    }

    /// <summary>
    /// Get a character's faction chat channel
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public ChatChannel GetFactionChat(Character character)
    {
        return GetFactionChat(character.Faction.MotherId);
    }

    /// <summary>
    /// Adds a nation chat channel
    /// </summary>
    /// <param name="race"></param>
    /// <param name="factionDisplayId"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    private bool AddNationChannel(Race race, FactionsEnum factionDisplayId, string name)
    {
        var mRace = (((byte)race - 1) & 0xFC);
        var channel = new ChatChannel() { ChatType = ChatType.Region, Faction = factionDisplayId, InternalId = mRace, InternalName = name };
        return NationChannels.TryAdd(mRace, channel);
    }

    /// <summary>
    /// Gets nation chat channel by race
    /// </summary>
    /// <param name="race"></param>
    /// <returns></returns>
    public ChatChannel GetNationChat(Race race)
    {
        // some bit magic that makes raceId into some kind of birth continent id
        // If Fairy (for Nuia) and Returned (for Haranya) are ever added as a different faction, we'll need to go and write some proper code for this
        var mRace = (((byte)race - 1) & 0xFC);
        return NationChannels.GetValueOrDefault(mRace, NullChannel);
    }

    /// <summary>
    /// Gets nation chat channel for a character
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public ChatChannel GetNationChat(Character character)
    {
        return GetNationChat(character.Race);
    }

    /// <summary>
    /// Adds a zone group chat channel
    /// </summary>
    /// <param name="zoneGroupId"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    private bool AddZoneChannel(uint zoneGroupId, string name)
    {
        var channel = new ChatChannel { ChatType = ChatType.Shout, SubType = (short)zoneGroupId, InternalId = zoneGroupId, InternalName = name };
        return ZoneChannels.TryAdd(zoneGroupId, channel);
    }

    /// <summary>
    /// Gets or creates a channel by zone key
    /// </summary>
    /// <param name="zoneKey"></param>
    /// <returns></returns>
    public ChatChannel GetZoneChat(uint zoneKey)
    {
        var zone = ZoneManager.Instance.GetZoneByKey(zoneKey);
        var zoneGroupId = zone?.GroupId ?? 0;

        // create it if it's not there
        if (!ZoneChannels.ContainsKey(zoneGroupId))
        {
            var zoneGroupName = ZoneManager.Instance.GetZoneGroupById(zoneGroupId)?.Name ?? "ZoneGroup(" + zoneGroupId.ToString() + ")";
            if (!AddZoneChannel(zoneGroupId, zoneGroupName))
                Logger.Error("Failed to create zone chat channel !");
        }

        if (ZoneChannels.TryGetValue(zoneGroupId, out var channel))
        {
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetZoneChat !");
            return NullChannel;
        }
    }

    /// <summary>
    /// Adds a guild specific chat channel
    /// </summary>
    /// <param name="guild"></param>
    /// <returns></returns>
    private bool AddGuildChannel(Expedition guild)
    {
        var channel = new ChatChannel() { ChatType = ChatType.Clan, SubType = (short)guild.Id, InternalId = (uint)guild.Id, InternalName = guild.Name };
        return GuildChannels.TryAdd(guild.Id, channel);
    }

    /// <summary>
    /// Get or create a guild channel 
    /// </summary>
    /// <param name="guild"></param>
    /// <returns></returns>
    public ChatChannel GetGuildChat(Expedition guild)
    {
        // create it if it's not there
        if (!GuildChannels.ContainsKey(guild.Id))
        {
            if (!AddGuildChannel(guild))
                Logger.Error("Failed to create guild chat channel !");
        }

        if (GuildChannels.TryGetValue(guild.Id, out var channel))
        {
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetGuildChat !");
            return NullChannel;
        }
    }

    /// <summary>
    /// Adds a family chat channel
    /// </summary>
    /// <param name="familyId"></param>
    /// <returns></returns>
    private bool AddFamilyChannel(uint familyId)
    {
        var channel = new ChatChannel() { ChatType = ChatType.Family, SubType = (short)familyId, InternalId = familyId, InternalName = $"Family {familyId}" };
        return FamilyChannels.TryAdd(familyId, channel);
    }

    /// <summary>
    /// Gets a family chat channel by Id
    /// </summary>
    /// <param name="familyId"></param>
    /// <returns></returns>
    public ChatChannel GetFamilyChat(uint familyId)
    {
        // create it if it's not there
        if (!FamilyChannels.ContainsKey(familyId))
        {
            if (!AddFamilyChannel(familyId))
                Logger.Error("Failed to create family chat channel !");
        }

        if (FamilyChannels.TryGetValue(familyId, out var channel))
        {
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetFamilyChat !");
            return NullChannel;
        }
    }

    /// <summary>
    /// Creates a party chat channel
    /// </summary>
    /// <param name="partyId"></param>
    /// <returns></returns>
    private bool AddPartyChannel(uint partyId)
    {
        var channel = new ChatChannel() { ChatType = ChatType.Party, SubType = (short)partyId, InternalId = partyId, InternalName = $"Party({partyId})" };
        return PartyChannels.TryAdd(partyId, channel);
    }

    /// <summary>
    /// Get or Creates a party chat channel for Character myChar
    /// </summary>
    /// <param name="party">Team(raid) you belong</param>
    /// <param name="myChar">You</param>
    /// <returns>ChatChannel based on your position inside a Raid</returns>
    public ChatChannel GetPartyChat(Team party, Character myChar)
    {
        var partyId = party.Id << 6;
        // Find my position inside the raid
        uint partyNumber = 0;
        for (uint i = 0; i < party.Members.Length; i++)
        {
            if ((party.Members[i] == null) || (party.Members[i].Character == null))
                continue;
            if (party.Members[i].Character.Id == myChar.Id)
            {
                partyNumber = (i / 5);
                break;
            }
        }
        partyId += partyNumber;

        // create it if it's not there
        if (!PartyChannels.ContainsKey(partyId))
        {
            if (!AddPartyChannel(partyId))
                Logger.Error("Failed to create party chat channel !");
        }

        if (PartyChannels.TryGetValue(partyId, out var channel))
        {
            channel.InternalName = $"Party {partyNumber + 1} of {WorldManager.Instance.GetCharacterById(party.OwnerId)?.Name ?? " ???"}";
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetPartyChat !");
            return NullChannel;
        }
    }

    /// <summary>
    /// Creates a raid chat channel
    /// </summary>
    /// <param name="partyId"></param>
    /// <returns></returns>
    private bool AddRaidChannel(uint partyId)
    {
        var channel = new ChatChannel() { ChatType = ChatType.Raid, SubType = (short)partyId, InternalId = partyId, InternalName = $"Raid({partyId})" };
        return RaidChannels.TryAdd(partyId, channel);
    }

    /// <summary>
    /// Get Raid channel for your Team
    /// </summary>
    /// <param name="party"></param>
    /// <returns></returns>
    public ChatChannel GetRaidChat(Team party)
    {
        // create it if it's not there
        if (!RaidChannels.ContainsKey(party.Id))
        {
            if (!AddRaidChannel(party.Id))
                Logger.Error("Failed to create party chat channel !");
        }

        if (RaidChannels.TryGetValue(party.Id, out var channel))
        {
            channel.InternalName = $"Raid of {WorldManager.Instance.GetCharacterById(party.OwnerId)?.Name ?? " ???"}";
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetRaidChat !");
            return NullChannel;
        }
    }
}
