using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Observers;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBroadcastVisualOption_0_Packet : GamePacket
{
    public CSBroadcastVisualOption_0_Packet() : base(CSOffsets.CSBroadcastVisualOption_0_Packet, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Connection.State = GameState.World;
        var character = Connection.ActiveChar;

        Connection.ActiveChar.VisualOptions = new CharacterVisualOptions();
        Connection.ActiveChar.VisualOptions.Read(stream);

        Connection.ActiveChar.Buffs.AddBuff((uint)BuffConstants.LoggedOn, Connection.ActiveChar);
        var template = CharacterManager.Instance.GetTemplate(character.Race, character.Gender);
        foreach (var buff in template.Buffs)
        {
            var buffTemplate = SkillManager.Instance.GetBuffTemplate(buff);
            var casterObj = new SkillCasterUnit(character.ObjId);
            character.Buffs.AddBuff(new Buff(character, character, casterObj, buffTemplate, null, DateTime.UtcNow) { Passive = true });
        }
        character.Breath = character.LungCapacity;
        // TODO: Fix the patron and auction house license buff issue
        Connection.ActiveChar.Buffs.AddBuff((uint)SkillConstants.PatronStatus, Connection.ActiveChar);
        //Connection.ActiveChar.Buffs.AddBuff((uint)SkillConstants.PatronPlus, Connection.ActiveChar);
        //Connection.ActiveChar.Buffs.AddBuff((uint)SkillConstants.Patron, Connection.ActiveChar);
        //Connection.ActiveChar.Buffs.AddBuff((uint)SkillConstants.AuctionLicense, Connection.ActiveChar);

        Connection.SendPacket(new SCUnitStatePacket(Connection.ActiveChar));

        Connection.ActiveChar.PushSubscriber(TimeManager.Instance.Subscribe(Connection, new TimeOfDayObserver(Connection.ActiveChar)));

        Connection.SendPacket(new SCCooldownsPacket(Connection.ActiveChar));
        Connection.SendPacket(new SCListSkillActiveTypsPacket([]));
        Connection.SendPacket(new SCDetailedTimeOfDayPacket(12f));
        Connection.SendPacket(new SCActionSlotsPacket(Connection.ActiveChar.Slots));

        Connection.ActiveChar.BroadcastPacket(new SCReputationChangedPacket(DateTime.UtcNow, false), true);
        //Connection.ActiveChar.BroadcastPacket(new SCItemTaskSuccessPacket(ItemTaskType.ItemTaskRemoveHeroReward, [], []), true);

        Connection.ActiveChar.BroadcastPacket(new SCUnitVisualOptionsPacket(Connection.ActiveChar.ObjId, Connection.ActiveChar.VisualOptions), true);

        Logger.Info("CSBroadcastVisualOption_0");
    }
}
