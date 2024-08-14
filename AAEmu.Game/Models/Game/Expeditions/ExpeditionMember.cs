using System;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World.Transform;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Expeditions;

public class ExpeditionMember : PacketMarshaler
{
    public uint ExpeditionId { get; set; }
    public uint CharacterId { get; set; }
    public bool InParty { get; set; }
    public bool IsOnline { get; set; }
    public DateTime LastWorldLeaveTime { get; set; }
    public string Name { get; set; }
    public byte Level { get; set; }
    public uint ZoneId { get; set; }
    public uint Id3 { get; set; } // TODO mb system faction.Id?
    public byte[] Abilities { get; set; } = [11, 11, 11];
    public byte Role { get; set; }
    public Vector3 Position { get; set; } = Vector3.Zero;
    public string Memo { get; set; }
    public DateTime TransferRequestedTime { get; set; }
    public DateTime NationJoinTime { get; set; }
    public int ContributionPoint { get; set; }
    public int GearScore { get; set; }

    public void Refresh(Character character)
    {
        IsOnline = character.IsOnline;
        InParty = character.InParty;
        Position = character.Transform.World.Position;
        ZoneId = character.Transform.ZoneId;
        Abilities = new[] { (byte)character.Ability1, (byte)character.Ability2, (byte)character.Ability3 };
        Level = character.Level;
        Name = character.Name;
        LastWorldLeaveTime = DateTime.UtcNow;
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO expedition_members(`character_id`,`expedition_id`,`name`,`level`,`role`,`last_leave_time`,`ability1`,`ability2`,`ability3`, `memo`) VALUES (@character_id,@expedition_id,@name,@level,@role,@last_leave_time,@ability1,@ability2,@ability3,@memo)";
            command.Parameters.AddWithValue("@character_id", this.CharacterId);
            command.Parameters.AddWithValue("@expedition_id", this.ExpeditionId);
            command.Parameters.AddWithValue("@name", this.Name);
            command.Parameters.AddWithValue("@level", this.Level);
            command.Parameters.AddWithValue("@role", this.Role);
            command.Parameters.AddWithValue("@last_leave_time", this.LastWorldLeaveTime);
            command.Parameters.AddWithValue("@ability1", this.Abilities[0]);
            command.Parameters.AddWithValue("@ability2", this.Abilities[1]);
            command.Parameters.AddWithValue("@ability3", this.Abilities[2]);
            command.Parameters.AddWithValue("@memo", this.Memo);
            command.ExecuteNonQuery();

        }

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = $"UPDATE characters SET expedition_id = {ExpeditionId} WHERE `characters`.`id` IN ({CharacterId})";
            command.Prepare();
            command.ExecuteNonQuery();
        }
    }

    public void Refresh(uint characterId)
    {
        var character = WorldManager.Instance.GetCharacterById(characterId);

        if (character == null)
        {
            var characterInfo = ExpeditionManager.Instance.GetOfflineCharacterInfo(characterId);
            var position = new Transform(null, null, characterInfo.Transform.WorldId, characterInfo.Transform.ZoneId, 1, characterInfo.Transform.World.Position.X, characterInfo.Transform.World.Position.Y, characterInfo.Transform.World.Position.Z, 0, 0, 0);
            Position = position.World.Position;
            ZoneId = position.ZoneId;

            InParty = false;
            IsOnline = false;

            var Ability1 = characterInfo.Ability1;
            var Ability2 = characterInfo.Ability2;
            var Ability3 = characterInfo.Ability3;
            Abilities = [(byte)Ability1, (byte)Ability2, (byte)Ability3];
        }
        else
        {
            IsOnline = character.IsOnline;
            InParty = character.InParty;
            Position = character.Transform.World.Position;
            ZoneId = character.Transform.ZoneId;
            Abilities = [(byte)character.Ability1, (byte)character.Ability2, (byte)character.Ability3];
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        Refresh(CharacterId);

        stream.Write(ExpeditionId);
        stream.Write(CharacterId);
        stream.Write(InParty);
        stream.Write(IsOnline);
        stream.Write(LastWorldLeaveTime);
        stream.Write(Name);
        stream.Write(Level);
        stream.Write(ZoneId);
        stream.Write(Id3);
        foreach (var ability in Abilities)
            stream.Write(ability);
        stream.Write(Role);
        stream.Write(Helpers.ConvertLongX(Position.X));
        stream.Write(Helpers.ConvertLongY(Position.Y));
        stream.Write(Position.Z);
        stream.Write(Memo);
        stream.Write(TransferRequestedTime); // transferRequestedTime
        stream.Write(NationJoinTime);        // nationJoinTime, add in 3+
        stream.Write(ContributionPoint);     // contributionPoint, add in 3+
        stream.Write(GearScore);             // gearScore, add in 3+
        return stream;
    }
}
