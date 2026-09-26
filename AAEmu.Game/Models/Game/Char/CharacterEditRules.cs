using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// CSBeautyshopData (0x185) as the client fills it in PayBeautyShop (via
/// ): the model view's race and gender, its hair, horn and tail slots (equipment slots 20,
/// 22, 23), the appearance block, and the ticket it found in the bag with the count it needs (1), or
/// the none id with count 0 when it found none.
/// </summary>
public sealed record BeautyshopEditRequest(
    byte Race,
    byte Gender,
    int HairItemId,
    int HornItemId,
    int TailItemId,
    UnitCustomModelParams Model,
    int TicketItemId,
    uint TicketCount);

/// <summary>
/// CSEditCharacter (0x04A), the lobby edit: the character id, then the
/// CSCreateCharacter body without its trailing introZoneId.
/// </summary>
public sealed record CharacterEditRequest(
    uint CharacterId,
    string Name,
    Race Race,
    Gender Gender,
    uint[] BodyItems,
    UnitCustomModelParams Model,
    AbilityType Ability1,
    AbilityType Ability2,
    AbilityType Ability3,
    byte Level);

public enum CharacterEditError
{
    None = 0,
    PeriodClosed,
    NotInLobby,
    NotOwned,
    IdentityChanged,
    UnknownBodyItem
}

/// <summary>
/// The lobby character edit. The client only offers it during the pre-select character period
/// (ApplyEditCharacter logs "not preSeleteCharacter Period" otherwise), which is the
/// <c>enable</c> flag of SCInitialConfig.
/// </summary>
public static class CharacterEditRules
{
    /// <summary>
    /// What SCInitialConfigPacket publishes as <c>enable</c>. It stays false: publishing it routes the
    /// lobby into its character reservation gate (see the packet), and the edit period is a launch-time
    /// event with no config of its own. A CSEditCharacter that arrives while it is false can only come
    /// from a modified client and is refused.
    /// </summary>
    public const bool PreSelectCharacterPeriod = false;

    /// <summary>The seven body slots CSCreateCharacter and CSEditCharacter carry, in wire order (face.. beard).</summary>
    public const int BodyItemCount = 7;
    public const EquipmentItemSlot FirstBodySlot = EquipmentItemSlot.Face;

    /// <summary>Wire index 0..6 to equipment slot 19..25, as CharacterManager.Create places them.</summary>
    public static EquipmentItemSlot BodySlot(int index) => (EquipmentItemSlot)((int)FirstBodySlot + index);

    /// <summary>
    /// The lobby edit is an appearance edit only. The client fills race, gender and the name from the
    /// character it edits, so any difference is a crafted packet; a rename has its own
    /// paid flow and level and abilities are not editable anywhere.
    /// </summary>
    public static CharacterEditError Validate(
        bool periodOpen,
        bool inLobby,
        bool ownsCharacter,
        bool sameName,
        bool sameRace,
        bool sameGender,
        bool sameLevel,
        bool sameAbilities)
    {
        if (!periodOpen)
            return CharacterEditError.PeriodClosed;
        if (!inLobby)
            return CharacterEditError.NotInLobby;
        if (!ownsCharacter)
            return CharacterEditError.NotOwned;
        if (!sameName || !sameRace || !sameGender || !sameLevel || !sameAbilities)
            return CharacterEditError.IdentityChanged;
        return CharacterEditError.None;
    }

    /// <summary>
    /// Each non-zero body item must be an item_body_parts row for the character's model in that slot,
    /// the check the client's own ApplyEditCharacter runs ("the character equip invalid custom item,
    /// slot %d, item type %u"). 0 keeps the current item, as the create path treats it.
    /// </summary>
    public static CharacterEditError ValidateBodyItems(ICharacterCustomizationCatalog catalog, uint modelId, uint[] bodyItems)
    {
        if (bodyItems == null || bodyItems.Length != BodyItemCount)
            return CharacterEditError.UnknownBodyItem;

        for (var i = 0; i < BodyItemCount; i++)
        {
            if (bodyItems[i] == 0)
                continue;
            if (!catalog.IsBodyPartItem(modelId, BodySlot(i), bodyItems[i]))
                return CharacterEditError.UnknownBodyItem;
        }

        return CharacterEditError.None;
    }
}
