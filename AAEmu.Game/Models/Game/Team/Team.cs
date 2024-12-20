using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Team;

public class Team : PacketMarshaler
{
    public uint Id { get; set; }
    public uint OwnerId { get; set; }
    public bool IsParty { get; set; }
    public TeamMember[] Members { get; set; }
    public LootingRule LootingRule { get; set; }
    public (byte, uint)[] MarksList { get; set; }
    public WorldSpawnPosition PingPosition { get; set; }

    public Team()
    {
        Members = new TeamMember[50];
        ResetMarks();
        PingPosition = new WorldSpawnPosition();
        LootingRule = new LootingRule();
    }

    public void ResetMarks()
    {
        MarksList = new (byte, uint)[12];
        for (var i = 0; i < 12; i++)
            MarksList[i] = (0, 0);
    }

    public bool IsMarked(uint id)
    {
        foreach (var (_, obj) in MarksList)
            if (obj == id)
                return true;
        return false;
    }

    public int MembersCount()
    {
        var count = 0;
        foreach (var member in Members)
            if (member?.Character != null)
                count++;
        return count;
    }

    public int MembersOnlineCount()
    {
        var count = 0;
        foreach (var member in Members)
            if ((member?.Character != null) && (member.Character.IsOnline))
                count++;
        return count;
    }

    public bool IsMember(uint id)
    {
        foreach (var member in Members)
            if (member?.Character != null && member.Character.Id == id)
                return true;
        return false;
    }

    public bool IsObjMember(uint objId)
    {
        var mate = MateManager.Instance.GetActiveMateByMateObjId(objId);
        foreach (var member in Members)
            if (member?.Character != null && (member.Character.ObjId == objId || mate?.OwnerObjId == member.Character.ObjId))
                return true;
        return false;
    }

    public uint GetNewOwner()
    {
        foreach (var member in Members)
            if (member?.Character != null && member.Character.IsOnline && member.Character.Id != OwnerId)
                return member.Character.Id;
        return 0;
    }

    public bool ChangeRole(uint id, MemberRole role)
    {
        foreach (var member in Members)
        {
            if (member == null || member.Character?.Id != id)
                continue;

            if (member.Role == role)
                return false;

            member.Role = role;
            return true;
        }

        return false;
    }

    public (TeamMember member, int partyIndex) AddMember(Character unit)
    {
        for (var i = 0; i < Members.Length; i++)
        {
            if (Members[i]?.Character != null)
                continue;

            Members[i] = new TeamMember(unit);
            return (Members[i], GetParty(i));
        }

        return (null, 0);
    }

    public bool RemoveMember(uint id)
    {
        var i = GetIndex(id);
        if (i < 0)
            return false;

        Members[i] = null;
        return true;
    }

    public bool MoveMember(uint id, uint id2, byte from, byte to)
    {
        // TODO validate idFrom, idTo
        try
        {
            var tempMember = Members[from];
            var tempMember2 = Members[to];
            Members[from] = tempMember2;
            Members[to] = tempMember;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public TeamMember ChangeStatus(Character unit)
    {
        var i = GetIndex(unit.Id);
        if (i < 0)
            return null;

        // TODO ...
        Members[i].Character = unit;

        // Members[i] = new TeamMember(unit);
        return Members[i];
    }

    public void BroadcastPacket(GamePacket packet, uint id = 0)
    {
        foreach (var member in Members)
        {
            if (member?.Character == null || !member.Character.IsOnline || member.Character.Id == id)
                continue;
            member.Character.SendPacket(packet);
        }
    }

    public int GetIndex(uint id)
    {
        for (var i = 0; i < Members.Length; i++)
            if (Members[i]?.Character != null && Members[i].Character.Id == id)
                return i;
        return -1;
    }

    public static int GetParty(int index)
    {
        if (index < 5)
            return 0;
        return index / 5;
    }

    public byte[] GetPartyCounts()
    {
        var result = new byte[10];
        for (var i = 0; i < Members.Length; i++)
        {
            if (Members[i]?.Character == null)
                continue;
            var partyIndex = GetParty(i);
            result[partyIndex]++;
        }

        return result;
    }

    /// <summary>
    /// Gets the next "winner" for loot
    /// </summary>
    /// <param name="eligiblePlayer">Only Characters in this list allowed to be returned</param>
    /// <param name="referenceLootOwner">BaseUnit used for location reference to check range</param>
    /// <param name="maxRange">If greater than 0, this range will be used to check range</param>
    /// <returns></returns>
    public Character GetNextLootWinner(HashSet<Character> eligiblePlayer, IBaseUnit referenceLootOwner, float maxRange = 200f)
    {
        // Match the eligible player list with the team members list
        var eligibleMembers = Members
            .Where(member => member is { Character: not null } &&
                             eligiblePlayer.Contains(member.Character))
            .ToList();

        // If every eligible player has got a winning roll, reset all of their win flags
        if (eligibleMembers.All(x => x.HasGoneRoundRobin))
        {
            foreach (var teamMember in eligibleMembers)
            {
                teamMember.HasGoneRoundRobin = false;
            }
        }
        
        // Grab a list of all remaining characters using only those provided from the members list
        var tempPlayerList = eligibleMembers.Where(member => member is { HasGoneRoundRobin: false }).ToList();

        // Somehow no results?
        if (tempPlayerList.Count <= 0)
        {
            // If somehow no results even after re-filling the list, return null
            Logger.Warn($"Was unable to generate a new random looting order list for team {Id} with leader {OwnerId}, ReferenceLootOwner: {referenceLootOwner}, MaxRange: {maxRange}");
            return null;
        }

        // Pick a random
        var rngPos = Random.Shared.Next(tempPlayerList.Count);
        tempPlayerList[rngPos].HasGoneRoundRobin = true;
        return tempPlayerList[rngPos].Character;
    }
    

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write(OwnerId);
        stream.Write(IsParty);

        foreach (var count in GetPartyCounts())
            stream.Write(count);

        foreach (var member in Members)
        {
            stream.Write(member?.Character?.Id ?? 0u);
            stream.Write(member?.Character?.IsOnline ?? false);
        }

        for (var i = 0; i < 12; i++)
        {
            var type = MarksList[i].Item1;
            var obj = MarksList[i].Item2;
            stream.Write(type);
            if (type == 1)
                stream.Write(obj);
            else if (type == 2)
                stream.WriteBc(obj);
        }

        stream.Write(LootingRule);
        return stream;
    }
}
