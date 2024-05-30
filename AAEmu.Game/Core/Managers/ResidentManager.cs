using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Residents;

using MySql.Data.MySqlClient;

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

        SaveDirectlyToDatabase();
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

            var residentMember = new ResidentMember(character);
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

        if (resident.IsMember(member.Id))
        {
            var m = resident.GetMember(member.Id);
            m.IsOnline = member.IsOnline;
            m.IsInParty = member.InParty;
        }

        member.SendPacket(new SCResidentMemberInfoPacket(resident));
        member.SendPacket(new SCResidentBalanceInfoPacket(resident));
    }

    public void UpdateDevelopmentStage(Character character)
    {
        var zoneGroupId = ZoneManager.Instance.GetZoneIdByKey(character.Transform.ZoneId);
        var resident = GetResidentByZoneGroupId(zoneGroupId);
        if (resident == null) { return; }

        if (resident.IsMember(character.Id))
        {
            var m = resident.GetMember(character.Id);
            m.IsOnline = character.IsOnline;
            m.IsInParty = character.InParty;
        }

        resident.DevelopmentStage += 1;
        character.SendPacket(new SCResidentInfoListPacket(GetInfo()));
    }
    public int GetResidentTokenCount(Character character)
    {
        var zoneGroupId = ZoneManager.Instance.GetZoneIdByKey(character.Transform.ZoneId);
        var resident = GetResidentByZoneGroupId(zoneGroupId);
        if (resident == null) { return 0; }

        if (resident.IsMember(character.Id))
        {
            var m = resident.GetMember(character.Id);
            m.IsOnline = character.IsOnline;
            m.IsInParty = character.InParty;
        }

        return resident.ResidentTokenCount;
    }

    public void UpdateResidentTokenCount(Character character, int count)
    {
        var zoneGroupId = ZoneManager.Instance.GetZoneIdByKey(character.Transform.ZoneId);
        var resident = GetResidentByZoneGroupId(zoneGroupId);
        if (resident == null) { return; }

        if (resident.IsMember(character.Id))
        {
            var m = resident.GetMember(character.Id);
            m.IsOnline = character.IsOnline;
            m.IsInParty = character.InParty;
        }

        resident.ResidentTokenCount = count;
    }

    public void UpdateAtLogin(Character character)
    {
        AddResidenMemberInfo(character);
        foreach (var resident in Residents)
        {
            foreach (var member in resident.Members.Where(member => member.Id == character.Id))
            {

                member.IsOnline = character.IsOnline;
                member.IsInParty = character.InParty;
                character.SendPacket(new SCResidentMapPacket(resident.ZoneGroupId, Option.Insert));
                character.SendPacket(new SCResidentInfoPacket(resident.ZoneGroupId, member));
                //return; // проверяем во всех резиденциях, так как может быть в нескольких
            }
        }
    }

    #region Database
    public bool SaveDirectlyToDatabase()
    {
        var saved = false;
        using var sqlConnection = MySQL.CreateConnection();
        using var transaction = sqlConnection.BeginTransaction();
        try
        {
            foreach (var resident in Residents)
            {
                saved = Save(sqlConnection, transaction, resident);
            }
            transaction.Commit();
        }
        catch (Exception e)
        {
            saved = false;
            Logger.Error(e, $"Residents save failed!\n");
            try
            {
                transaction.Rollback();
            }
            catch (Exception eRollback)
            {
                // Really failed here
                Logger.Fatal(eRollback, $"Residents save rollback failed!\n");
            }
        }

        return saved;
    }

    public bool Save(MySqlConnection connection, MySqlTransaction transaction, Resident resident)
    {
        bool result;
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO `residents` (`id`, `zone_group_id`, `point`, `resident_token`, `development_stage`, `zone_point`, `charge`)" +
                                                       " VALUES (@id,  @zone_group_id,  @point,  @resident_token,  @development_stage,  @zone_point,  @charge)";
                command.Parameters.AddWithValue("@id", resident.Id);
                command.Parameters.AddWithValue("@zone_group_id", resident.ZoneGroupId);
                command.Parameters.AddWithValue("@point", resident.Point);
                command.Parameters.AddWithValue("@resident_token", resident.ResidentTokenCount);
                command.Parameters.AddWithValue("@development_stage", resident.DevelopmentStage);
                command.Parameters.AddWithValue("@zone_point", resident.ZonePoint);
                command.Parameters.AddWithValue("@charge", resident.Charge);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                foreach (var member in resident.Members)
                {
                    command.CommandText = "REPLACE INTO `resident_members` (`id`, `resident_id`, `name`, `level`, `family`, `service_point`)" +
                                                                  " VALUES (@id,  @resident_id,  @name,  @level,  @family,  @service_point)";
                    command.Parameters.AddWithValue("@id", member.Id);
                    command.Parameters.AddWithValue("@resident_id", resident.Id);
                    command.Parameters.AddWithValue("@name", member.Name);
                    command.Parameters.AddWithValue("@level", member.Level);
                    command.Parameters.AddWithValue("@family", member.Family);
                    command.Parameters.AddWithValue("@service_point", member.ServicePoint);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                }
            }
            result = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            result = false;
        }

        return result;
    }

    public void Load()
    {
        Logger.Info("Loading Residents...");

        Residents = new List<Resident>();
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM residents";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var resident = new Resident();
                        resident.Id = reader.GetUInt32("id");
                        resident.ZoneGroupId = reader.GetUInt16("zone_group_id");
                        resident.Point = reader.GetInt32("point");
                        resident.ResidentTokenCount = reader.GetInt32("resident_token");
                        resident.DevelopmentStage = reader.GetByte("development_stage");
                        resident.ZonePoint = reader.GetInt32("zone_point");
                        resident.Charge = reader.GetDateTime("charge");

                        var members = new List<ResidentMember>();
                        using (var connection2 = MySQL.CreateConnection())
                        {
                            using (var command2 = connection2.CreateCommand())
                            {
                                command2.CommandText = "SELECT * FROM resident_members WHERE resident_id=@resident_id";
                                command2.Parameters.AddWithValue("@resident_id", resident.Id);
                                using var reader2 = command2.ExecuteReader();
                                while (reader2.Read())
                                {
                                    var residentMember = new ResidentMember();
                                    residentMember.Id = reader2.GetUInt32("id");
                                    residentMember.Name = reader2.GetString("name");
                                    residentMember.Level = reader2.GetByte("level");
                                    residentMember.Family = reader2.GetUInt32("family");
                                    residentMember.IsOnline = false;
                                    residentMember.IsInParty = false;
                                    residentMember.ServicePoint = reader2.GetInt32("service_point");

                                    members.Add(residentMember);
                                }

                                resident.Members = members;
                                Residents.Add(resident);
                            }
                        }
                    }
                }
            }
        }

        if (Residents.Count > 0) { return; }
        for (uint i = 0; i < MaxCountResident; i++)
        {
            var resident = new Resident();
            resident.Id = i + 1;
            resident.ZoneGroupId = ResidentGameData.Instance.GetZoneGroupId(i + 1);
            Residents.Add(resident);
        }

        SaveDirectlyToDatabase();
    }
    #endregion
}
