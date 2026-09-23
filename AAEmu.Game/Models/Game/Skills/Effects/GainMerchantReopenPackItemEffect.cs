using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.Skills;
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

    /// <summary>Minutes the opened box stays available. The pack row is what the session uses.</summary>
    public int LifeTime { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character character)
            return;
        if (casterObj is not SkillItem skillItem || skillItem.ItemId == 0)
        {
            Logger.Warn("GainMerchantReopenPackItemEffect: pack {0} was used without a box item instance", MerchantReopenPackId);
            return;
        }

        var opened = ReopenBoxManager.Instance.TryRefresh(
            character.Id, (long)skillItem.ItemId, MerchantReopenPackId, false, time,
            deliverExpired: expired => ReopenBoxItemRules.MailExpiredRoll(character, expired));
        if (opened != ReopenRefreshResult.Refreshed)
        {
            Logger.Warn("GainMerchantReopenPackItemEffect: pack {0} did not open for {1}: {2}",
                MerchantReopenPackId, character.Name, opened);
            return;
        }

        var state = ReopenBoxManager.Instance.TryGetState(character.Id, (long)skillItem.ItemId);
        if (state == null)
            return;
        var pack = ReopenBoxManager.Instance.TryGetPack(MerchantReopenPackId);
        character.SendPacket(new SCReopenRandomBoxInfoPacket(state, pack));
    }
}
