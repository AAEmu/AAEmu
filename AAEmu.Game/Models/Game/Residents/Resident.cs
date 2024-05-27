using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Residents;

public enum Option : byte
{
    Insert = 1,
    Delete = 2,
    Clear = 3
};

public class Resident : PacketMarshaler
{
    public uint Id { get; set; } // порядковый номер, в 3.0.3.0 - 29 зон
    public ushort ZoneGroupId { get; set; }
    public int Point { get; set; }
    public int ZonePoint { get; set; }
    public byte DevelopmentStage { get; set; } // этап развития
    public Option Option { get; set; }
    public DateTime Charge { get; set; }
    public List<ResidentMember> Members { get; set; }

    public Resident()
    {

        ZonePoint = 0;
        ZoneGroupId = 0;
        Option = Option.Clear;
        DevelopmentStage = 0;
        Charge = DateTime.MinValue;
        Members = [];
    }

    public int MembersCount()
    {
        return Members.Count(member => member?.Character != null);
    }

    public int MembersOnlineCount()
    {
        return Members.Count(member => member?.Character is { IsOnline: true });
    }

    public bool IsMember(uint id)
    {
        return Members.Any(member => member?.Character?.Id == id);
    }

    public void AddMember(ResidentMember member)
    {
        Members.Add(member);
    }

    public bool RemoveMember(ResidentMember member)
    {
        return Members.Remove(member);
    }

    public ResidentMember GetMember(uint id)
    {
        return Members.FirstOrDefault(member => member.Character.Id == id);
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ResidentGameData.Instance.GetZoneGroupId(Id));
        stream.Write(ZonePoint);
        stream.Write(Charge);
        stream.Write(ResidentGameData.Instance.GetDoodadPhase(Id, DevelopmentStage));
        return stream;
    }
}
