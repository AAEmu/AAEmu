using System;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSelectCharacterPacket : GamePacket
{
    public CSSelectCharacterPacket() : base(CSOffsets.CSSelectCharacterPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var characterId = stream.ReadUInt32();
        var gm = stream.ReadBoolean();
        stream.ReadByte();

        if (Connection.Characters.TryGetValue(characterId, out var character))
        {
            // Despawn any old pets this character might have even before loading it
            //var character = Connection.Characters[characterId];
            character.Load();
            character.Connection = Connection;
            var houses = Connection.Houses.Values.Where(x => x.OwnerId == character.Id).ToList();
            MateManager.Instance.RemoveAndDespawnAllActiveOwnedMates(character);

            Connection.ActiveChar = character;
            if (Character.UsedCharacterObjIds.TryGetValue(character.Id, out var oldObjId))
            {
                Connection.ActiveChar.ObjId = oldObjId;
            }
            else
            {
                Connection.ActiveChar.ObjId = ObjectIdManager.Instance.GetNextId();
                Character.UsedCharacterObjIds.TryAdd(character.Id, character.ObjId);
            }

            var mySlave = SlaveManager.Instance.GetSlaveByOwnerObjId(Connection.ActiveChar.ObjId);
            if (mySlave != null)
            {
                Logger.Warn($"{Connection.ActiveChar.Name}: Interrupting the transport shutdown task");
                mySlave.CancelTokenSource.Cancel();
                // TODO найти, как восстанавливать контроль
                Unit.DespawSlave(Connection.ActiveChar); // despawn because we lost control over them
            }
            var myMates = MateManager.Instance.GetActiveMates(Connection.ActiveChar.ObjId);
            if (myMates != null)
            {
                Unit.DespawnMate(Connection.ActiveChar); // despawn because we lost control over them
            }

            Connection.ActiveChar.Simulation = new Simulation(character);

            // TODO: Fix the patron and auction house license buff issue
            if (Connection.ActiveChar is { } unit)
            {
                if (!unit.Buffs.CheckBuff((uint)SkillConstants.Patron)) //TODO Wrong place
                {
                    unit.Buffs.AddBuff(new Buff(unit, unit, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(8000011), null, System.DateTime.Now));
                }

                if (!unit.Buffs.CheckBuff((uint)SkillConstants.AuctionLicense)) //TODO Wrong place
                {
                    unit.Buffs.AddBuff(new Buff(unit, unit, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(8000012), null, System.DateTime.Now));
                }
            }

            Connection.SendPacket(new SCResidentInfoListPacket(ResidentManager.Instance.GetInfo()));
            Connection.SendPacket(new SCCharacterStatePacket(character));
            Connection.SendPacket(new SCCharacterGamePointsPacket(character));
            Connection.ActiveChar.Inventory.Send();
            Connection.SendPacket(new SCActionSlotsPacket(Connection.ActiveChar.Slots));

            Connection.ActiveChar.Quests.Send();
            Connection.ActiveChar.Quests.SendCompleted();

            Connection.ActiveChar.Actability.Send();
            Connection.ActiveChar.Mails.SendUnreadMailCount();
            Connection.ActiveChar.Appellations.Send();
            Connection.ActiveChar.Portals.Send();
            Connection.ActiveChar.Friends.Send();
            Connection.ActiveChar.Blocked.Send();

            foreach (var house in houses)
            {
                Connection.SendPacket(new SCMyHousePacket(house));
            }

            foreach (var conflict in ZoneManager.Instance.GetConflicts())
            {
                Connection.SendPacket(new SCConflictZoneStatePacket(conflict.ZoneGroupId, conflict.CurrentZoneState, conflict.NextStateTime));
            }

            FactionManager.Instance.SendFactions(Connection.ActiveChar);
            ExpeditionManager.Instance.SendExpeditions(Connection.ActiveChar);
            ExpeditionManager.SendMyExpeditionInfo(Connection.ActiveChar);
            FactionManager.Instance.SendRelations(Connection.ActiveChar);

            Connection.ActiveChar.SendOption(1);
            Connection.ActiveChar.SendOption(2);
            Connection.ActiveChar.SendOption(5);

            Connection.ActiveChar.Buffs.AddBuff((uint)BuffConstants.LoggedOn, Connection.ActiveChar);

            var template = CharacterManager.Instance.GetTemplate(character.Race, character.Gender);

            foreach (var buff in template.Buffs)
            {
                var buffTemplate = SkillManager.Instance.GetBuffTemplate(buff);
                var casterObj = new SkillCasterUnit(character.ObjId);
                character.Buffs.AddBuff(new Buff(character, character, casterObj, buffTemplate, null, DateTime.UtcNow) { Passive = true });
            }

            character.Breath = character.LungCapacity;

            Connection.SendPacket(new SCScheduledEventStartedPacket());

            Connection.ActiveChar.OnZoneChange(0, Connection.ActiveChar.Transform.ZoneId);
        }
        else
        {
            // TODO ...
        }
    }
}
