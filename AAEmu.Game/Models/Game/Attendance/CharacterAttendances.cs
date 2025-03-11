using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Attendance;

public class CharacterAttendances
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public List<Attendances> Attendances { get; set; }
    public ulong AccountId { get; set; }
    private Character Owner { get; set; }
    private int AttendanceCount { get; set; } = 31;

    public CharacterAttendances()
    {
    }

    public CharacterAttendances(Character owner)
    {
        Owner = owner;
        AccountId = owner.AccountId;

        Load(MySQL.CreateConnection());
        if (Attendances is not { Count: not 0 })
        {
            CreateAttendances();
        }
    }

    private void CreateAttendances()
    {
        Attendances = new List<Attendances>();
        for (var i = 0; i < AttendanceCount; i++)
        {
            Attendances.Add(new Attendances());
        }
    }

    public void Add(Character character)
    {
        var currentDate = DateTime.UtcNow;
        var year = currentDate.Year;
        var month = currentDate.Month;
        var day = currentDate.Day;

        if (Checking(day, character.AccountId))
        {
            return; // already received a gift
        }

        Attendances[day].Accept = true;
        Attendances[day].AccountAttendance = currentDate;

        var dayCount = DayCount();

        var (itemId, itemCount) = AttendanceGameData.Instance.GetReward(year, month, dayCount);
        if (itemCount > 0 && itemId > 0)
        {
            character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.TakeScheduleItem, itemId, itemCount);
        }
        var (additionalItemId, additionalItemCount) = AttendanceGameData.Instance.GetAdditionalReward(year, month, dayCount);
        if (additionalItemCount > 0 && additionalItemId > 0)
        {
            character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.TakeScheduleItem, additionalItemId, additionalItemCount);
        }
        character.SendPacket(new SCDbAttendanceTimePacket(true, currentDate));

        Logger.Warn($"Attendance: {character.Name}:{character.Id} received a gift");
    }

    private bool Checking(int day, ulong accountId)
    {
        return Attendances[day].Accept && AccountId == accountId;
    }

    private int DayCount()
    {
        return Attendances.Count(attendance => attendance.Accept);
    }

    public void SendEmptyAttendances()
    {
        var attendances = new List<Attendances>();
        for (var i = 0; i < AttendanceCount; i++)
        {
            attendances.Add(new Attendances());
        }

        Owner.SendPacket(new SCAccountAttendancePacket(attendances));
    }

    public void Send()
    {
        Owner.SendPacket(new SCAccountAttendancePacket(Attendances));
    }

    public void Load(MySqlConnection connection)
    {
        Attendances = new List<Attendances>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM attendances WHERE `owner` = @owner";
        command.Parameters.AddWithValue("@owner", AccountId);
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var template = new Attendances();
            //Owner.AccountId = reader.GetUInt32("owner");
            template.AccountAttendance = reader.GetDateTime("account_attendance");
            template.Accept = reader.GetBoolean("accept");
            Attendances.Add(template);
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var indx = 0;
        foreach (var attendance in Attendances)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO attendances(`id`,`owner`,`account_attendance`,`accept`) " +
                                  "VALUES (@id, @owner, @account_attendance, @accept)";
            command.Parameters.AddWithValue("@id", indx);
            command.Parameters.AddWithValue("@owner", AccountId);
            command.Parameters.AddWithValue("@account_attendance", attendance.AccountAttendance);
            command.Parameters.AddWithValue("@accept", attendance.Accept);
            command.ExecuteNonQuery();
            indx++;
        }
    }
}
