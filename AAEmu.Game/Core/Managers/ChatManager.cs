using System.Collections.Concurrent;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Team;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class ChatManager : Singleton<ChatManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// nullChannel is used as a fallback channel, do not use directly
    /// </summary>
    private ChatChannel nullChannel;
    private ConcurrentDictionary<long, ChatChannel> _factionChannels;
    private ConcurrentDictionary<long, ChatChannel> _nationChannels;
    private ConcurrentDictionary<long, ChatChannel> _zoneChannels;
    private ConcurrentDictionary<long, ChatChannel> _partyChannels;
    private ConcurrentDictionary<long, ChatChannel> _raidChannels;
    private ConcurrentDictionary<long, ChatChannel> _guildChannels;
    private ConcurrentDictionary<long, ChatChannel> _familyChannels;

    public ChatManager()
    {
        nullChannel = new ChatChannel() { chatType = ChatType.White, faction = 0, internalName = "Null" };
        _factionChannels = new ConcurrentDictionary<long, ChatChannel>();
        _nationChannels = new ConcurrentDictionary<long, ChatChannel>();
        _zoneChannels = new ConcurrentDictionary<long, ChatChannel>();
        _partyChannels = new ConcurrentDictionary<long, ChatChannel>();
        _raidChannels = new ConcurrentDictionary<long, ChatChannel>();
        _guildChannels = new ConcurrentDictionary<long, ChatChannel>();
        _familyChannels = new ConcurrentDictionary<long, ChatChannel>();
    }

    public void Initialize()
    {
        Logger.Info("Initializing Chat Manager...");

        // Create Faction Channels
        AddFactionChannel(148, "Nuia");
        AddFactionChannel(149, "Haranya");
        AddFactionChannel(114, "Pirate");
        // TODO: Player Factions ?

        // Create Nation Channels
        AddNationChannel(Race.Nuian, 148, "Nuian-Elf-Dwarf");
        AddNationChannel(Race.Hariharan, 149, "Harani-Firran-Warborn");

        // Zone, Party/Raid, Guild, Family channels are created on the fly
    }

    /// <summary>
    /// Used in GM command /testchatchannel list
    /// </summary>
    /// <returns>List of all chat channels currently loaded</returns>
    public List<ChatChannel> ListAllChannels()
    {
        var res = new List<ChatChannel>();
        res.Add(nullChannel);
        res.AddRange(_factionChannels.Values);
        res.AddRange(_nationChannels.Values);
        res.AddRange(_zoneChannels.Values);
        res.AddRange(_partyChannels.Values);
        res.AddRange(_raidChannels.Values);
        res.AddRange(_guildChannels.Values);
        res.AddRange(_familyChannels.Values);
        return res;
    }

    public void LeaveAllChannels(Character character)
    {
        foreach (var c in _factionChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in _nationChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in _zoneChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in _partyChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in _raidChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in _guildChannels)
            c.Value?.LeaveChannel(character);
        foreach (var c in _familyChannels)
            c.Value?.LeaveChannel(character);
    }

    /// <summary>
    /// Removes zone, party, guild, etc. channels that have zero members in them to free up space (and Id's)
    /// </summary>
    public int CleanUpChannels()
    {
        var res = 0;
        foreach (var c in _zoneChannels)
            if (c.Value.members.Count <= 0)
            {
                _zoneChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in _partyChannels)
            if (c.Value.members.Count <= 0)
            {
                _partyChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in _raidChannels)
            if (c.Value.members.Count <= 0)
            {
                _raidChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in _guildChannels)
            if (c.Value.members.Count <= 0)
            {
                _guildChannels.TryRemove(c.Key, out _);
                res++;
            }
        foreach (var c in _familyChannels)
            if (c.Value.members.Count <= 0)
            {
                _familyChannels.TryRemove(c.Key, out _);
                res++;
            }
        return res;
    }

    private bool AddFactionChannel(uint factionId, string name)
    {
        var channel = new ChatChannel() { chatType = ChatType.Ally, faction = factionId, internalId = factionId, internalName = name };
        return _factionChannels.TryAdd(factionId, channel);
    }

    public ChatChannel GetFactionChat(uint factionMotherId)
    {
        if (_factionChannels.TryGetValue(factionMotherId, out var c))
            return c;
        else
            return nullChannel;
    }

    public ChatChannel GetFactionChat(Character character)
    {
        return GetFactionChat(character.Faction.MotherId);
    }

    private bool AddNationChannel(Race race, uint factionDisplayId, string name)
    {
        var mRace = (((byte)race - 1) & 0xFC);
        var channel = new ChatChannel() { chatType = ChatType.Region, faction = factionDisplayId, internalId = mRace, internalName = name };
        return _nationChannels.TryAdd(mRace, channel);
    }

    public ChatChannel GetNationChat(Race race)
    {
        // some bit magic that makes raceId into some kind of birth continent id
        // If Fairy (for Nuia) and Returned (for Haranya) are ever added as a diffferent faction, we'll need to go and write some proper code for this
        var mRace = (((byte)race - 1) & 0xFC);
        if (_nationChannels.TryGetValue(mRace, out var channel))
            return channel;
        else
            return nullChannel;
    }

    public ChatChannel GetNationChat(Character character)
    {
        return GetNationChat(character.Race);
    }

    private bool AddZoneChannel(uint zoneGroupId, string name)
    {
        var channel = new ChatChannel() { chatType = ChatType.Shout, subType = (short)zoneGroupId, internalId = zoneGroupId, internalName = name };
        return _zoneChannels.TryAdd(zoneGroupId, channel);
    }

    public ChatChannel GetZoneChat(uint zoneKey)
    {
        var zone = ZoneManager.Instance.GetZoneByKey(zoneKey);
        var zoneGroupId = zone?.GroupId ?? 0;

        // create it if it's not there
        if (!_zoneChannels.ContainsKey(zoneGroupId))
        {
            var zoneGroupName = ZoneManager.Instance.GetZoneGroupById(zoneGroupId)?.Name ?? "ZoneGroup(" + zoneGroupId.ToString() + ")";
            if (!AddZoneChannel(zoneGroupId, zoneGroupName))
                Logger.Error("Failed to create zone chat channel !");
        }

        if (_zoneChannels.TryGetValue(zoneGroupId, out var channel))
        {
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetZoneChat !");
            return nullChannel;
        }
    }

    private bool AddGuildChannel(Expedition guild)
    {
        var channel = new ChatChannel() { chatType = ChatType.Clan, subType = (short)guild.Id, internalId = guild.Id, internalName = guild.Name };
        return _guildChannels.TryAdd(guild.Id, channel);
    }

    public ChatChannel GetGuildChat(Expedition guild)
    {
        // create it if it's not there
        if (!_guildChannels.ContainsKey(guild.Id))
        {
            if (!AddGuildChannel(guild))
                Logger.Error("Failed to create guild chat channel !");
        }

        if (_guildChannels.TryGetValue(guild.Id, out var channel))
        {
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetGuildChat !");
            return nullChannel;
        }
    }

    private bool AddFamilyChannel(uint familyId)
    {
        var channel = new ChatChannel() { chatType = ChatType.Family, subType = (short)familyId, internalId = familyId, internalName = $"Family {familyId}" };
        return _familyChannels.TryAdd(familyId, channel);
    }
    
    public ChatChannel GetFamilyChat(uint familyId)
    {
        // create it if it's not there
        if (!_familyChannels.ContainsKey(familyId))
        {
            if (!AddFamilyChannel(familyId))
                Logger.Error("Failed to create family chat channel !");
        }

        if (_familyChannels.TryGetValue(familyId, out var channel))
        {
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetFamilyChat !");
            return nullChannel;
        }
    }

    private bool AddPartyChannel(uint partyId)
    {
        var channel = new ChatChannel() { chatType = ChatType.Party, subType = (short)partyId, internalId = partyId, internalName = "Party(" + partyId.ToString() + ")" };
        return _partyChannels.TryAdd(partyId, channel);
    }

    /// <summary>
    /// Get or Creates a party chat channel for Character myChar
    /// </summary>
    /// <param name="party">Team(raid) you belong</param>
    /// <param name="myChar">You</param>
    /// <returns>ChatChannel based on your position inside a Raid</returns>
    public ChatChannel GetPartyChat(Team party, Character myChar)
    {
        uint partyId = party.Id << 6;
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
        if (!_partyChannels.ContainsKey(partyId))
        {
            if (!AddPartyChannel(partyId))
                Logger.Error("Failed to create party chat channel !");
        }

        if (_partyChannels.TryGetValue(partyId, out var channel))
        {
            channel.internalName = "Party " + (partyNumber + 1).ToString() + " of " + WorldManager.Instance.GetCharacterById(party.OwnerId)?.Name ?? " ???";
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetPartyChat !");
            return nullChannel;
        }
    }

    private bool AddRaidChannel(uint partyId)
    {
        var channel = new ChatChannel() { chatType = ChatType.Raid, subType = (short)partyId, internalId = partyId, internalName = "Party(" + partyId.ToString() + ")" };
        return _raidChannels.TryAdd(partyId, channel);
    }

    /// <summary>
    /// Get Raid channel for your Team
    /// </summary>
    /// <param name="party"></param>
    /// <returns></returns>
    public ChatChannel GetRaidChat(Team party)
    {
        // create it if it's not there
        if (!_raidChannels.ContainsKey(party.Id))
        {
            if (!AddRaidChannel(party.Id))
                Logger.Error("Failed to create party chat channel !");
        }

        if (_raidChannels.TryGetValue(party.Id, out var channel))
        {
            channel.internalName = "Raid of " + WorldManager.Instance.GetCharacterById(party.OwnerId)?.Name ?? " ???";
            return channel;
        }
        else
        {
            Logger.Error("Should not be able to get a null channel from GetRaidChat !");
            return nullChannel;
        }
    }
}
