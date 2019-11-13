using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game.Expeditions
{
    public class Expedition : SystemFaction
    {
        private List<uint> _removedMembers;

        public List<ExpeditionMember> Members { get; set; }
        public List<ExpeditionRolePolicy> Policies { get; set; }

        public Expedition()
        {
            _removedMembers = new List<uint>();
            Members = new List<ExpeditionMember>();
            Policies = new List<ExpeditionRolePolicy>();
        }

        public void RemoveMember(ExpeditionMember member)
        {
            Members.Remove(member);
            _removedMembers.Add(member.CharacterId);
        }

        public void OnCharacterLogin(Character character)
        {
            var member = GetMember(character);
            if (member == null)
                return;

            member.Refresh(character);

            SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0));
        }

        public void OnCharacterLogout(Character character)
        {
            var member = GetMember(character);
            member.IsOnline = false;
            member.LastWorldLeaveTime = DateTime.Now;

            SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0));
        }

        public ExpeditionRolePolicy GetPolicyByRole(byte role)
        {
            foreach (var policy in Policies)
                if (policy.Role == role)
                    return policy;

            return null;
        }

        public ExpeditionMember GetMember(Character character)
        {
            foreach (var member in Members)
                if (member.CharacterId == character.Id)
                    return member;
            return null;
        }

        public ExpeditionMember GetMember(uint characterId)
        {
            foreach (var member in Members)
                if (member.CharacterId == characterId)
                    return member;
            return null;
        }

        public void SendPacket(GamePacket packet)
        {
            foreach (var member in Members)
                WorldManager.Instance.GetCharacterById(member.CharacterId)?.SendPacket(packet);
        }

        internal void Save(GameDBContext ctx)
        {
            if (_removedMembers.Count > 0)
            {
                ctx.ExpeditionMembers.RemoveRange(
                        ctx.ExpeditionMembers.Where(e => _removedMembers.Contains((uint)e.CharacterId)));
                ctx.SaveChanges();

                ctx.Characters.Where(c => _removedMembers.Contains((uint)c.Id))
                    .ToList()
                    .All(c => 
                        { 
                            c.ExpeditionId = 0;
                            return true;
                        });

                _removedMembers.Clear();
            }
            ctx.SaveChanges();

            ctx.Expeditions.RemoveRange(
                    ctx.Expeditions.Where(e => e.Id == this.Id && e.Owner == this.OwnerId));
            ctx.SaveChanges();

            ctx.Expeditions.Add(this.ToEntity());
            ctx.SaveChanges();

            foreach (var member in Members)
                member.Save(ctx);

            foreach (var policy in Policies)
                policy.Save(ctx);
        }

        public DB.Game.Expeditions ToEntity()
            =>
            new DB.Game.Expeditions()
            {
                Id          = this.Id         ,
                Owner       = this.OwnerId    ,
                OwnerName   = this.OwnerName  ,
                Name        = this.Name       ,
                Mother      = this.MotherId   ,
                CreatedAt   = this.Created    ,
            };

        public static explicit operator Expedition(DB.Game.Expeditions v) 
            =>
            new Expedition()
            {
                Id        = v.Id         ,
                MotherId  = v.Mother     ,
                OwnerId   = v.Owner      ,
                Name      = v.Name       ,
                OwnerName = v.OwnerName  ,
                Created   = v.CreatedAt  ,
            };

    }
}
