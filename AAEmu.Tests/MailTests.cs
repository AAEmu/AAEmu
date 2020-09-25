using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using Xunit;

namespace AAEmu.Tests
{
    public class MailTests : IDisposable
    {
        Character character;
        CharacterMails mails;

        public MailTests()
        {
            var modelParams = new UnitCustomModelParams();
            character = new Character(modelParams);
            character.AccountId = 1;
            character.Id = 1;
            character.Name = "tester";
            character.Money = 1000;

            mails = new CharacterMails(character);

            NameManager.Instance.AddCharacterName(character.Id, character.Name);
            MailManager.Instance._allPlayerMails = new Dictionary<long, Mail>();
        }
        
        public void Dispose()
        {
            NameManager.Instance.RemoveCharacterName(character.Id);
            MailManager.Instance._allPlayerMails = null;
            character = null;
            mails = null;
        }

        [Fact]
        public void MoneyTest()
        {
            var type = (byte)2;
            var receiverCharName = "tester";
            var title = "test";
            var text = "test";
            var attachments = (byte)0;
            var money0 = 500;
            var money1 = 0;
            var money2 = 0;
            var extra = 0;
            var itemSlots = new List<(SlotType slotType, byte slot)>();
                      
            mails.SendMail(type, receiverCharName,character.Name, title, text, attachments, money0, money1, money2, extra, itemSlots);
            Assert.Equal(400, character.Money);        
        }
        
        [Fact]
        public void PlayerNotFoundTest()
        {
         
            var type = (byte)2;
            var receiverCharName = "bob";
            var title = "test";
            var text = "test";
            var attachments = (byte)0;
            var money0 = 500;
            var money1 = 0;
            var money2 = 0;
            var extra = 0;
            var itemSlots = new List<(SlotType slotType, byte slot)>();
            
            Assert.False(mails.SendMail(type, receiverCharName, character.Name, title, text, attachments, money0, money1, money2, extra, itemSlots));
            Assert.Equal(1000, character.Money);
        }
    }
}
