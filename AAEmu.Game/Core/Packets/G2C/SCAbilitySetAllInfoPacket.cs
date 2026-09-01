using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Full skillsaver list for login / resync. Client <c>OnAbilitySetAllInfo</c> /
/// <c>DWAbilitySetAllInfoPacket::Init</c> feeds <c>GetSavedAbilitySets</c> / preview skill lists.
/// Opcode 0x149.
/// </summary>
/// <remarks>
/// Wire layout recovered from 10.0.2.13 <c>x2game-dev.dll</c> archive reader (RVA ~0xc881a7):
/// <list type="bullet">
/// <item><c>usedFreeActivationCount</c> u8</item>
/// <item>exactly <see cref="CharacterAbilitySets.MaxSlots"/> (=5) slots, always written</item>
/// <item>per slot: u32 skillCount → u8×3 abilities → u32[] skills → u32 passiveCount →
/// u32[] passives → heirSkills (u32 size + elements; size 0 is fine)</item>
/// </list>
/// Skill count is clamped to 36 (<c>over max skill count!!</c>). Sending 10 slots / abilities
/// before count desynced the reader and thrashed the machine on character select.
/// </remarks>
public class SCAbilitySetAllInfoPacket(IReadOnlyList<AbilitySetSlot> slotsByIndex, byte usedFreeActivationCount)
    : GamePacket(SCOffsets.SCAbilitySetAllInfoPacket, 1)
{
    public const int MaxSkillsPerSlot = 36;
    public const int MaxPassivesPerSlot = 33;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(usedFreeActivationCount);

        for (byte i = 0; i < CharacterAbilitySets.MaxSlots; i++)
        {
            AbilitySetSlot slot = null;
            foreach (var candidate in slotsByIndex)
            {
                if (candidate.SlotIndex == i)
                {
                    slot = candidate;
                    break;
                }
            }

            if (slot == null || !slot.IsOccupied)
            {
                stream.Write(0u); // skillCount
                stream.Write((byte)0);
                stream.Write((byte)0);
                stream.Write((byte)0);
                stream.Write(0u); // passiveCount
                stream.Write(0u); // heirSkills.Size
                continue;
            }

            var skillCount = Math.Min(slot.SkillIds.Count, MaxSkillsPerSlot);
            stream.Write((uint)skillCount);
            stream.Write((byte)slot.Ability1);
            stream.Write((byte)slot.Ability2);
            stream.Write((byte)slot.Ability3);
            for (var s = 0; s < skillCount; s++)
                stream.Write(slot.SkillIds[s]);

            var passiveCount = Math.Min(slot.PassiveBuffIds.Count, MaxPassivesPerSlot);
            stream.Write((uint)passiveCount);
            for (var p = 0; p < passiveCount; p++)
                stream.Write(slot.PassiveBuffIds[p]);

            // heirSkills: Size + elements. Empty list is enough until heir skillsavers are wired.
            stream.Write(0u);
        }

        return stream;
    }
}
