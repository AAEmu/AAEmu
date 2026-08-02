using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Expeditions;

public class ExpeditionMember : PacketMarshaler
{
    private const int MaxNameLength = 128;
    private const int MaxMemoLength = 63;

    public FactionsEnum ExpeditionId { get; set; }
    public uint CharacterId { get; set; }
    public bool InParty { get; set; }
    public bool IsOnline { get; set; }
    public DateTime LastWorldLeaveTime { get; set; }
    public string Name { get; set; }
    public byte Level { get; set; }
    public byte HeirLevel { get; set; }
    public uint ZoneId { get; set; }
    public FactionsEnum FactionId { get; set; }
    public byte[] Abilities { get; set; } = [11, 11, 11];
    public byte Role { get; set; }
    public Vector3 Position { get; set; } = Vector3.Zero;
    public string Memo { get; set; }
    public DateTime TransferRequestedTime { get; set; }
    public uint ContributionPoint { get; set; }
    public uint WeeklyContributionPoint { get; set; }
    public uint GearScore { get; set; }

    public void Refresh(Character character)
    {
        IsOnline = true;
        Position = character.Transform.World.Position;
        ZoneId = character.Transform.ZoneId;
        Abilities = [(byte)character.Ability1, (byte)character.Ability2, (byte)character.Ability3];
        Level = character.Level;
        HeirLevel = character.HeirLevel;
        FactionId = character.Faction.Id;
        Name = character.Name;
        LastWorldLeaveTime = DateTime.UtcNow;
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO expedition_members(`character_id`,`expedition_id`,`name`,`level`,`role`,`last_leave_time`,`ability1`,`ability2`,`ability3`,`memo`,`contribution_point`,`weekly_contribution_point`) VALUES (@character_id,@expedition_id,@name,@level,@role,@last_leave_time,@ability1,@ability2,@ability3,@memo,@contribution_point,@weekly_contribution_point)";
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
            command.Parameters.AddWithValue("@contribution_point", this.ContributionPoint);
            command.Parameters.AddWithValue("@weekly_contribution_point", this.WeeklyContributionPoint);
            command.ExecuteNonQuery();
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        if (Abilities is not { Length: 3 })
            throw new InvalidOperationException("Native expedition members contain exactly three ability fields.");

        stream.Write((int)ExpeditionId);                         // i32 type
        stream.Write((ulong)CharacterId);                        // u64 type
        stream.Write(InParty);
        stream.Write(IsOnline);
        stream.Write(LastWorldLeaveTime);
        stream.Write((Name ?? string.Empty)[..Math.Min(Name?.Length ?? 0, MaxNameLength)]);
        stream.Write(Level);
        stream.Write(HeirLevel);
        stream.Write(ZoneId);
        stream.Write((int)FactionId);                            // i32 type
        foreach (var ability in Abilities)
            stream.Write(ability);
        stream.Write(Role);
        stream.Write(Helpers.ConvertLongX(Position.X));
        stream.Write(Helpers.ConvertLongY(Position.Y));
        stream.Write(Position.Z);
        stream.Write((Memo ?? string.Empty)[..Math.Min(Memo?.Length ?? 0, MaxMemoLength)]);
        stream.Write(TransferRequestedTime);
        stream.Write(ContributionPoint);
        stream.Write(WeeklyContributionPoint);
        stream.Write(GearScore);
        return stream;
    }
}
