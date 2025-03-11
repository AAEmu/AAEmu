using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Mails.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public sealed class MailTests : IDisposable
{
    private CharacterMock _character;
    private CharacterMails _mails;

    public MailTests()
    {
        var modelParams = new UnitCustomModelParams();
        _character = new CharacterMock();
        _character.AccountId = 1;
        _character.Id = 1;
        _character.Name = "tester";
        _character.Money = 1000;

        _mails = new CharacterMails(_character);

        NameManager.Instance.AddCharacterName(_character.Id, _character.Name, 1);
        MailIdManager.Instance.Initialize();
        MailManager.Instance._allPlayerMails = new Dictionary<long, BaseMail>();
    }

    public void Dispose()
    {
        NameManager.Instance.RemoveCharacterName(_character.Id);
        MailManager.Instance._allPlayerMails = null;
        _character = null;
        _mails = null;
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

        Assert.Equal(MailResult.Success, _mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, extra, itemSlots));
        Assert.Equal(400, _character.Money);
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

        Assert.NotEqual(MailResult.Success, _mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, extra, itemSlots));
        Assert.Equal(1000, _character.Money);
    }
}
