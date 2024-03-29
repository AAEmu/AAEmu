using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Managers.UnitManagers;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterAppellations
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public List<uint> Appellations { get; set; }
    public uint ActiveAppellation { get; set; }

    public Character Owner { get; set; }

    public CharacterAppellations(Character owner)
    {
        Owner = owner;
        Appellations = new List<uint>();
        ActiveAppellation = 0;
    }

    private void addBuff(uint titleId)
    {
        var buffId = CharacterManager.Instance.GetAppellationsTemplate(titleId)?.BuffId ?? 0;
        if (buffId != 0) Owner.Buffs.AddBuff(buffId, Owner);

        Logger.Info($"title: {titleId} giving buff {buffId}");
    }
    private void removeCurrentBuff()
    {
        if (ActiveAppellation != 0) //i.e. you have no title to begin with, so this is skipped
        {
            var buffId = CharacterManager.Instance.GetAppellationsTemplate(ActiveAppellation)?.BuffId ?? 0;
            if (buffId != 0) Owner.Buffs.RemoveBuff(buffId); //i.e. checking if title actually has a buff

            Logger.Info($"removing current buff: {buffId}");
        }
    }

    public void Add(uint id)
    {
        // SCAppellationGainedPacket
        if (Appellations.Contains(id))
        {
            Logger.Warn("Duplicate add {0}, ownerId {1}", id, Owner.Id);
            return;
        }

        Appellations.Add(id);
        Owner.SendPacket(new SCAppellationGainedPacket(id));
    }

    public void Change(uint id)
    {
        if (id == 0)
        {
            removeCurrentBuff();
            ActiveAppellation = 0;
        }
        else
        {
            if (Appellations.Contains(id))
            {
                removeCurrentBuff();
                ActiveAppellation = id;
                addBuff(id);
            }
            else
            {
                Logger.Warn("Id {0} doesn't exist, owner {1}", id, Owner.Id);
            }
        }
        Owner.BroadcastPacket(new SCAppellationChangedPacket(Owner.ObjId, ActiveAppellation), true);
    }

    public void Send()
    {
        for (var i = 0; i < Appellations.Count; i += 512)
        {
            var result = new (uint, bool)[Appellations.Count - i <= 512 ? Appellations.Count - i : 512];

            for (var j = 0; j < result.Length; j++)
                result[j] = (Appellations[i + j], Appellations[i + j] == ActiveAppellation);

            Owner.SendPacket(new SCAppellationsPacket(result));
        }
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, active FROM appellations WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var id = reader.GetUInt32("id");
                    var active = reader.GetBoolean("active");

                    Appellations.Add(id);
                    if (active)
                    {
                        ActiveAppellation = id;
                        addBuff(id);
                    }
                }
            }
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        foreach (var appellation in Appellations)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO appellations(`id`,`active`,`owner`) VALUES (@id, @active, @owner)";
                command.Parameters.AddWithValue("@id", appellation);
                command.Parameters.AddWithValue("@active", ActiveAppellation == appellation);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }
        }
    }
}
