using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Residents;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class ResidentManager : Singleton<ResidentManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public List<Resident> Residents { get; set; }
    public const int MaxCountResident = 29; // Suggested Maximum Size

    public ResidentManager()
    {
    }

    public void Initialize()
    {
        Residents = new List<Resident>();
        for (uint i = 0; i < MaxCountResident; i++)
        {
            var resident = new Resident();
            resident.Id = i + 1;
            resident.ZoneGroupId = ResidentGameData.Instance.GetZoneGroupId(i + 1);
            Residents.Add(resident);
        }
    }

    public List<Resident> GetInfo()
    {
        return Residents;
    }

    public (ushort, byte) GetZoneInfo(Character character)
    {
        ushort zoneGroupId = 0;
        byte developmentStage = 0;
        var id = ZoneManager.Instance.GetZoneIdByKey(character.Transform.ZoneId);
        foreach (var resident in Residents)
        {
            if (resident.ZoneGroupId == id)
            {
                zoneGroupId = resident.ZoneGroupId;
                developmentStage = resident.DevelopmentStage;
                break;
            }
        }
        return (zoneGroupId, developmentStage);
    }

    public Resident GetResidentByZoneId(uint zoneId)
    {
        var res = new Resident();
        var id = ZoneManager.Instance.GetZoneIdByKey(zoneId);
        foreach (var resident in Residents)
        {
            if (resident.ZoneGroupId == id)
            {
                res = resident;
                break;
            }
        }
        return res;
    }

    public Resident GetResidentByZoneGroupId(uint zoneGroupId)
    {
        var res = new Resident();
        foreach (var resident in Residents)
        {
            if (resident.ZoneGroupId == zoneGroupId)
            {
                res = resident;
                break;
            }
        }
        return res;
    }

    public void AddResidenMemberInfo(Character character)
    {
        var myHouses = new Dictionary<uint, House>();
        if (HousingManager.Instance.GetByCharacterId(myHouses, character.Id) > 0 && character.Level >= 30)
        {
            var zoneGroupId = ZoneManager.Instance.GetZoneIdByKey(character.Transform.ZoneId);
            var resident = GetResidentByZoneGroupId(zoneGroupId);
            if (resident == null) { return; }

            ResidentMember residentMember;
            if (resident.IsMember(character.Id)) { return; }

            residentMember = new ResidentMember(character);
            resident.AddMember(residentMember);
            character.SendPacket(new SCResidentMapPacket(resident.ZoneGroupId, Option.Insert));
        }
    }

    public void RemoveResidenMemberInfo(Character character)
    {
        var zoneGroupId = ZoneManager.Instance.GetZoneIdByKey(character.Transform.ZoneId);
        var resident = GetResidentByZoneGroupId(zoneGroupId);
        if (resident == null) { return; }

        if (resident.IsMember(character.Id))
        {
            var residentMember = resident.GetMember(character.Id);
            resident.RemoveMember(residentMember);
            character.SendPacket(new SCResidentMapPacket(resident.ZoneGroupId, Option.Delete));
        }
    }

    public void UpdateResidenMemberInfo(ushort zoneGroupId, Character member)
    {
        var resident = GetResidentByZoneGroupId(zoneGroupId);
        if (resident == null) { return; }

        member.SendPacket(new SCResidentMemberInfoPacket(resident));
        member.SendPacket(new SCResidentBalanceInfoPacket(resident));
    }
    public void UpdateDevelopmentStage(Character character)
    {
        var zoneGroupId = ZoneManager.Instance.GetZoneIdByKey(character.Transform.ZoneId);
        var resident = GetResidentByZoneGroupId(zoneGroupId);
        if (resident == null) { return; }

        resident.DevelopmentStage += 1;
        character.SendPacket(new SCResidentInfoListPacket(GetInfo()));
    }

    public void UpdateAtLogin(Character character)
    {
        AddResidenMemberInfo(character);
        foreach (var resident in Residents)
        {
            foreach (var member in resident.Members.Where(member => member.Character.Id == character.Id))
            {
                character.SendPacket(new SCResidentMapPacket(resident.ZoneGroupId, Option.Insert));
                character.SendPacket(new SCResidentInfoPacket(resident.ZoneGroupId, member));
                //return; // проверяем во всех резиденциях, так как может быть в нескольких
            }
        }
    }
}
