using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncConditionalUse : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint SkillId { get; set; }
        public uint FakeSkillId { get; set; }
        public uint QuestId { get; set; }
        public uint QuestTriggerPhase { get; set; }
        public uint ItemId { get; set; }
        public uint ItemTriggerPhase { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug($"DoodadFuncConditionalUse: skillId {SkillId}, fakeSkillId {FakeSkillId}, questId {QuestId}, questTriggerPhase {QuestTriggerPhase}, itemId {ItemId}, itemTriggerPhase {ItemTriggerPhase}");

            // TODO for quest ID=3124, "A Familiar Machine"
            if (FakeSkillId != 0 || SkillId != 0)
            {
                owner.ToNextPhase = true;
            }

            if (caster is Character character2 && character2.Inventory.GetItemsCount(ItemId) > 0)
            {
                owner.OverridePhase = (int)ItemTriggerPhase;
                owner.ToNextPhase = true;
                return;
            }

            if (caster is Character character && character.Quests.HasQuest(QuestId))
            {
                owner.OverridePhase = (int)QuestTriggerPhase;
                owner.ToNextPhase = true;
                return;
            }
        }
    }
}
