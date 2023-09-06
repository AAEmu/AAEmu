using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Item
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? CategoryId { get; set; }

    public long? Level { get; set; }

    public string Description { get; set; }

    public long? Price { get; set; }

    public long? Refund { get; set; }

    public long? BindId { get; set; }

    public long? PickupLimit { get; set; }

    public long? MaxStackSize { get; set; }

    public long? IconId { get; set; }

    public byte[] Sellable { get; set; }

    public long? UseSkillId { get; set; }

    public byte[] UseSkillAsReagent { get; set; }

    public long? ImplId { get; set; }

    public long? PickupSoundId { get; set; }

    public long? MilestoneId { get; set; }

    public long? BuffId { get; set; }

    public byte[] Gradable { get; set; }

    public byte[] LootMulti { get; set; }

    public long? LootQuestId { get; set; }

    public byte[] NotifyUi { get; set; }

    public long? UseOrEquipmentSoundId { get; set; }

    public long? HonorPrice { get; set; }

    public long? ExpAbsLifetime { get; set; }

    public long? ExpOnlineLifetime { get; set; }

    public byte[] ExpDate { get; set; }

    public long? SpecialtyZoneId { get; set; }

    public long? LevelRequirement { get; set; }

    public string Comment { get; set; }

    public long? AuctionACategoryId { get; set; }

    public long? AuctionBCategoryId { get; set; }

    public long? AuctionCCategoryId { get; set; }

    public long? LevelLimit { get; set; }

    public long? FixedGrade { get; set; }

    public byte[] Disenchantable { get; set; }

    public long? LivingPointPrice { get; set; }

    public long? ActabilityGroupId { get; set; }

    public long? ActabilityRequirement { get; set; }

    public byte[] GradeEnchantable { get; set; }

    public long? CharGenderId { get; set; }

    public byte[] OneTimeSale { get; set; }

    public long? LimitedSaleCount { get; set; }

    public long? MaleIconId { get; set; }

    public long? OverIconId { get; set; }

    public byte[] Translate { get; set; }

    public byte[] AutoRegisterToActionbar { get; set; }
}
