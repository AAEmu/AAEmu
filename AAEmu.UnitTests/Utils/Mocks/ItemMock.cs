﻿using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Utils.Mocks;

public class ItemMock : Item
{
    public ItemTemplate DefaultTemplate = new()
    {
        Id = 1,
        BindType = ItemBindType.Normal
    };

    public ItemMock(uint id, int count = 1)
    {
        Id = id;
        Template = DefaultTemplate;
        TemplateId = DefaultTemplate.Id;
        Count = count;
    }

    public ItemMock(uint id, ItemTemplate template, int count = 1)
    {
        Id = id;
        Template = template;
        TemplateId = template.Id;
        Count = count;
    }
}
