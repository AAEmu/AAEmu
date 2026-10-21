using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// Runtime encounter policy used by NPC combat, dialogue and spawning.
/// </summary>
internal interface INpcEncounter
{
    bool CanDamage(BaseUnit attacker);
    int AdjustDamage(BaseUnit attacker, int amount);
    bool CanTalk(uint sourceId, uint recipientId);
    void AttachReplacement(Npc npc);
    void Retreat(Npc npc);
    bool AbandonCombat();
}
