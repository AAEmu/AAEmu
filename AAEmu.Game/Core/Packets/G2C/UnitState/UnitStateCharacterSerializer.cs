using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C.UnitState;

/// <summary>Character-only UnitState tail beginning with the fixed 29-entry expertise block.</summary>
internal static class UnitStateCharacterSerializer
{
    private const int ExpertiseSlotCount = 29;

    public static void Write(PacketStream stream, UnitStateWireContext context)
    {
        var character = context.Character;
        if (character is null)
            return;

        var abilities = character.Abilities.Values.OrderBy(ability => ability.Id)
            .Take(ExpertiseSlotCount).ToList();
        foreach (var ability in abilities)
        {
            stream.Write(ability.Exp);
            stream.Write(ability.Order);
        }
        for (var index = abilities.Count; index < ExpertiseSlotCount; index++)
        {
            stream.Write(0u);
            stream.Write((byte)0);
        }

        var activeAbilities = character.Abilities.GetActiveAbilities().Take(byte.MaxValue).ToList();
        stream.Write((byte)activeAbilities.Count);
        foreach (var ability in activeAbilities)
            stream.Write((byte)ability);

        // This bc is the DUEL STATE object, not the faction. The client reads the block as
        // "bc, duelTeamType, camp", and we wrote the faction id into it - so every
        // player, having a non-zero faction, looked to the client as if they were already duelling,
        // and "That person is already dueling." blocked every invite before the client sent one.
        stream.WriteBc(character.DuelStateObjectId);
        stream.Write(unchecked((sbyte)character.DuelTeamType));
        stream.Write(unchecked((sbyte)character.Camp));
        character.VisualOptions.WriteOptions(stream);
        stream.Write(character.PremiumGrade);

        (character.BlessUthstin ?? new CharacterBlessUthstin()).WritePageInfos(stream);
        stream.Write(0u); // equipSlotReinforces.slotInfoList count (u32)
        stream.Write(0u); // equipSlotReinforces.levelEffectList count (u32)
    }
}
