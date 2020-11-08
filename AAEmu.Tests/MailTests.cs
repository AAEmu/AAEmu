using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Tests.Utils;
using Xunit;

namespace AAEmu.Tests
{
    public class MailTests : IDisposable
    {
        CharacterMock character;
        CharacterMails mails;

        public MailTests()
        {
            var modelParams = new UnitCustomModelParams();
            character = new CharacterMock();
            character.AccountId = 1;
            character.Id = 1;
            character.Name = "tester";
            character.Money = 1000;

            mails = new CharacterMails(character);

            NameManager.Instance.AddCharacterName(character.Id, character.Name);
            MailIdManager.Instance.Initialize();
            MailManager.Instance._allPlayerMails = new Dictionary<long, BaseMail>();
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
            var type = MailType.Express;
            var receiverCharName = "tester";
            var title = "test";
            var text = "test";
            var attachments = (byte)0;
            var money0 = 500;
            var money1 = 0;
            var money2 = 0;
            var extra = 0;
            var itemSlots = new List<(SlotType slotType, byte slot)>();
                      
            Assert.True(mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, extra, itemSlots));
            Assert.Equal(400, character.Money);        
        }
        
        [Fact]
        public void PlayerNotFoundTest()
        {
         
            var type = MailType.Express;
            var receiverCharName = "bob";
            var title = "test";
            var text = "test";
            var attachments = (byte)0;
            var money0 = 500;
            var money1 = 0;
            var money2 = 0;
            var extra = 0;
            var itemSlots = new List<(SlotType slotType, byte slot)>();

            Assert.False(mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, extra, itemSlots));
            Assert.Equal(1000, character.Money);
        }
    }
}
