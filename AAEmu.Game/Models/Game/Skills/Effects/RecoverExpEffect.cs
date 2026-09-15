using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class RecoverExpEffect : EffectTemplate
{
    public bool NeedMoney { get; set; }
    public bool NeedLaborPower { get; set; }

    public bool NeedPriest { get; set; }
    public bool Penaltied { get; set; }

    /// <summary>Labour taken when the recovery is priced in labour.</summary>
    private const int ExpRecoveryLabor = 10;

    /// <summary>
    /// Coin price of clearing the penalty. Scales with level so a high character is not restored for pocket
    /// change, using the same copper-per-level basis the resurrection costs elsewhere are quoted in.
    /// </summary>
    private static int ExpRecoveryCost(Char.Character character) => character.Level * 1000; // 10.0.2.13: recover_exp_effects.penaltied present again

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Char.Character character)
            return;

        // Nothing to recover unless the character is actually carrying a resurrection penalty.
        if (character.RezPenaltyDuration <= 0)
            return;

        Logger.Debug($"RecoverExpEffect: recovering {character.Name} from a {character.RezPenaltyDuration}s rez penalty (needMoney {NeedMoney}, needLabor {NeedLaborPower}, needPriest {NeedPriest}, penaltied {Penaltied})");

        // NeedPriest restricts the recovery to a priest's cast, so the caster must be someone other than the
        // one being recovered — a player cannot self-serve a priest resurrection.
        if (NeedPriest && ReferenceEquals(caster, target))
        {
            character.SendErrorMessage(ErrorMessageType.PriestCannotCreateBuff);
            return;
        }

        // Both pools are checked before either is charged. Money used to leave the character before the
        // labour check ran, and a cast refused for labour kept the money with the penalty still in place.
        var cost = NeedMoney ? ExpRecoveryCost(character) : 0;
        if (NeedMoney && character.Money < cost)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            return;
        }

        if (NeedLaborPower && character.LaborPower + character.LocalLaborPower < ExpRecoveryLabor)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
            return;
        }

        if (NeedMoney &&
            !character.SubtractMoney(SlotType.Inventory, cost, ItemTaskType.SkillEffectConsumption))
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            return;
        }

        if (NeedLaborPower)
        {
            // Both pools pay; see Character.ChangeLabor.
            character.ChangeLabor((short)-ExpRecoveryLabor, 0);
        }

        // Costs are taken before the penalty clears, so a failed payment leaves the character as it was.
        // Clearing first made every refused cast a free resurrection: the penalty was gone and the state
        // packet was already on its way before the priest, money and labour checks had run.
        character.RezPenaltyDuration = 0;
        character.SendPacket(new Core.Packets.G2C.SCUnitStatePacket(character));
    }
}
