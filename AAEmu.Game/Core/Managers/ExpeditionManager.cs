using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class ExpeditionManager : Singleton<ExpeditionManager>
    {
        private static Logger _log = NLog.LogManager.GetCurrentClassLogger();

        private Dictionary<uint, Expedition> _expeditions;

        public void Load()
        {
            _expeditions = new Dictionary<uint, Expedition>();

            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM expeditions";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Expedition expedition = new Expedition();
                            expedition.Id = reader.GetUInt32("id");
                            expedition.MotherId = reader.GetUInt32("mother");
                            expedition.Name = reader.GetString("name");
                            expedition.OwnerId = reader.GetUInt32("owner");
                            expedition.OwnerName = reader.GetString("owner_name");
                            expedition.UnitOwnerType = 0;
                            expedition.PoliticalSystem = 1;
                            expedition.Created = reader.GetDateTime("created_at");
                            expedition.AggroLink = false;
                            expedition.DiplomacyTarget = false;
                            _expeditions.Add(expedition.Id, expedition);
                        }
                    }
                }
            }
        }

        public Expedition GetExpedition(uint id) {
            if (_expeditions.ContainsKey(id))
                return _expeditions[id];

            return null;
        }

        // INTERACTION //

        public void CreateExpedition(string name, Character owner)
        {
            if (owner.Expedition != null) return;

            var expedition = Create(name, owner);
            _expeditions.Add(expedition.Id, expedition);

            owner.Expedition = expedition;

            owner.SendPacket(new SCFactionCreatedPacket(expedition, owner.ObjId,
                new[] {(owner.ObjId, owner.Id, owner.Name)}));

            owner.SendPacket(new SCFactionListPacket(expedition));
            owner.SendPacket(
                new SCUnitExpeditionChangedPacket(owner.ObjId, owner.Id, "", owner.Name, 0, expedition.Id, false)
            );

            owner.SendPacket(new SCExpeditionRolePolicyListPacket(expedition.Policies));
            owner.SendPacket(new SCExpeditionMemberListPacket(expedition));
        }

        public void SendExpeditionInfo(Character character) {
            var expedition = character.Expedition;
            character.SendPacket(new SCExpeditionRolePolicyListPacket(expedition.Policies));
            character.SendPacket(new SCExpeditionMemberListPacket(expedition));
        }

        // UTILS //

        public Expedition Create(string name, Character owner) {
            var expedition = new Expedition();
            expedition.Id = ExpeditionIdManager.Instance.GetNextId();
            expedition.MotherId = owner.Faction.Id;
            expedition.Name = name;
            expedition.OwnerId = owner.Id;
            expedition.OwnerName = owner.Name;
            expedition.UnitOwnerType = 0;
            expedition.PoliticalSystem = 1;
            expedition.Created = DateTime.Now;
            expedition.AggroLink = false;
            expedition.DiplomacyTarget = false;

            expedition.Policies = new List<ExpeditionRolePolicy>();

            var policy = new ExpeditionRolePolicy();
            policy.Id = expedition.Id;
            policy.Role = 255; // master
            policy.Name = owner.Name;
            policy.DominionDeclare = true;
            policy.Invite = true;
            policy.Expel = true;
            policy.Promote = true;
            policy.Dismiss = true;
            policy.Chat = true;
            policy.ManagerChat = true;
            policy.SiegeMaster = true;
            policy.JoinSiege = true;
            
            expedition.Policies.Add(policy);

            expedition.Members = new List<Member>();

            var member = GetMemberFromCharacter(owner, true);
            member.Id = expedition.Id;
            member.Id2 = owner.Id;
            
            expedition.Members.Add(member);

            return expedition;
        }

        public Member GetMemberFromCharacter(Character character, bool owner) {
            Member member = new Member();
            member.IsOnline = true;
            member.Name = character.Name;
            member.Level = character.Level;
            member.Role = 255;
            member.Memo = "";
            member.X = character.Position.X;
            member.Y = character.Position.Y;
            member.Z = character.Position.Z;
            member.ZoneId = (int)character.Position.ZoneId;

            return member;
        }

        public void SendExpeditions(Character character)
        {
            if (_expeditions.Values.Count > 0)
            {
                var expeditions = _expeditions.Values.ToArray();
                for (var i = 0; i < expeditions.Length; i += 20)
                {
                    var temp = new Expedition[expeditions.Length - i <= 20 ? expeditions.Length - i : 20];
                    Array.Copy(expeditions, i, temp, 0, temp.Length);
                    character.SendPacket(new SCFactionListPacket(temp));
                }
            }

            character.SendPacket(new SCExpeditionRolePolicyListPacket(new List<ExpeditionRolePolicy>()));
        }
    }
}
