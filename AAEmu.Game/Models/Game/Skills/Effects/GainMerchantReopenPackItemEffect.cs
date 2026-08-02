using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Grants from a reopenable merchant pack — the "재개봉 랜박 상자" boxes.
///
/// The pack is a two stage weighted draw: <c>merchant_reopen_groups</c> holds the rank tiers for a pack,
/// weighted against each other, and <c>merchant_reopen_goods</c> holds the items inside a tier, weighted
/// against each other. Ten packs, 48 groups and 254 goods ship in this build.
/// </summary>
public class GainMerchantReopenPackItemEffect : EffectTemplate
{
    public uint MerchantReopenPackId { get; set; }

    /// <summary>Minutes before the pack may be opened again; the shipped rows run 10, 70 and 1440.</summary>
    public int LifeTime { get; set; }

    public override bool OnActionTime => false;

    /// <summary>
    /// Account attribute kind the reopen cooldown is filed under. Above enum_account_attribute_kinds' three
    /// shipped values so it cannot collide with a client-known kind.
    /// </summary>
    private const uint ReopenCooldownKind = 1000;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character character)
            return;

        // Still cooling down from the last open of this same pack.
        var cooldown = AccountAttributeManager.Instance.Find(character.AccountId, ReopenCooldownKind, MerchantReopenPackId, 0);
        if (cooldown is { IsExpired: false })
        {
            character.SendErrorMessage(ErrorMessageType.CraftCooldown);
            return;
        }

        var good = MerchantReopenPackGameData.Instance.Roll(MerchantReopenPackId);
        if (good == null)
        {
            Logger.Warn($"GainMerchantReopenPackItemEffect: pack {MerchantReopenPackId} yielded nothing for {character.Name}");
            return;
        }

        Logger.Debug($"GainMerchantReopenPackItemEffect: pack {MerchantReopenPackId} -> item {good.ItemId} x{good.Count} grade {good.GradeId} for {character.Name}");

        // Straight to the bag when it fits, otherwise to the mail attachment container, which is how the rest
        // of the grant paths avoid dropping a reward a full inventory cannot take.
        if (character.Inventory.Bag.SpaceLeftForItem(good.ItemId) >= good.Count)
        {
            character.Inventory.Bag.AcquireDefaultItemEx(ItemTaskType.SkillEffectGainItem, good.ItemId,
                good.Count, good.GradeId, out _, out _, character.Id);
        }
        else
        {
            character.Inventory.MailAttachments.AcquireDefaultItemEx(ItemTaskType.Invalid, good.ItemId,
                good.Count, good.GradeId, out _, out _, character.Id);
            character.SendErrorMessage(ErrorMessageType.BagFull);
        }

        // LifeTime is the reopen cooldown in minutes. It is per account rather than per character - the box
        // belongs to the account - so it is recorded through the account attribute store under the pack id,
        // which is what stops a player shuffling characters to reopen the same box immediately.
        if (LifeTime > 0)
        {
            AccountAttributeManager.Instance.Change(
                character.AccountId, ReopenCooldownKind, MerchantReopenPackId,
                0, true, 1, LifeTime);
        }

    }
}
