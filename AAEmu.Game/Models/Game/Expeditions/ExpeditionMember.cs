using System;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.DB.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game.Expeditions
{
    public class ExpeditionMember : PacketMarshaler
    {
        public uint ExpeditionId { get; set; } // TODO mb faction/family id?
        public uint CharacterId { get; set; } // TODO mb characterId?
        public bool InParty { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastWorldLeaveTime { get; set; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public int ZoneId { get; set; }
        public uint Id3 { get; set; } // TODO mb system faction.Id?
        public byte[] Abilities { get; set; } = { 11, 11, 11 };
        public byte Role { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public string Memo { get; set; }
        public DateTime TransferRequestedTime { get; set; }

        public void Refresh(Character character)
        {
            IsOnline = true;
            X = character.Position.X;
            Y = character.Position.Y;
            Z = character.Position.Z;
            ZoneId = (int)character.Position.ZoneId;
            Abilities = new[] { (byte)character.Ability1, (byte)character.Ability2, (byte)character.Ability3 };
        }

        internal void Save(GameDBContext ctx)
        {
            ctx.ExpeditionMembers.RemoveRange(
                ctx.ExpeditionMembers.Where(m => m.CharacterId == m.CharacterId));
            ctx.SaveChanges();

            ctx.ExpeditionMembers.Add(this.ToEntity());
            ctx.SaveChanges();
        }

        public override PacketStream Write(PacketStream stream)
        {
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
            stream.Write(Helpers.ConvertLongX(X));
            stream.Write(Helpers.ConvertLongY(Y));
            stream.Write(Z);
            stream.Write(Memo);
            stream.Write(TransferRequestedTime);
            return stream;
        }

        public DB.Game.ExpeditionMembers ToEntity()
            =>
            new DB.Game.ExpeditionMembers()
            {
                CharacterId   = this.CharacterId        ,
                ExpeditionId  = this.ExpeditionId       ,
                Name          = this.Name               ,
                Level         = this.Level              ,
                Role          = this.Role               ,
                LastLeaveTime = this.LastWorldLeaveTime ,
                Ability1      = this.Abilities[0]       ,
                Ability2      = this.Abilities[1]       ,
                Ability3      = this.Abilities[2]       ,
                Memo          = this.Memo               ,
            };

        public static explicit operator ExpeditionMember(ExpeditionMembers v) 
            => 
            new ExpeditionMember()
            {
                CharacterId         = v.CharacterId   ,
                ExpeditionId        = v.ExpeditionId  ,
                Role                = v.Role          ,
                Memo                = v.Memo          ,
                LastWorldLeaveTime  = v.LastLeaveTime ,
                Name                = v.Name          ,
                Level               = v.Level         ,
                Abilities           = new byte[3]
                                    {
                                        v.Ability1    ,
                                        v.Ability2    ,
                                        v.Ability3    ,
                                    }                 ,
            };
    }
}
