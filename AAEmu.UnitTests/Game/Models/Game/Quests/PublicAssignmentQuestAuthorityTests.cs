using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.TodayAssignment;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

[NotInParallel] // seeds the process-wide TodayQuestGameData singleton
public sealed class PublicAssignmentQuestAuthorityTests
{
    [Test]
    public async Task ForgedPersonalStartCannotActivatePublicGuildQuest()
    {
        const uint publicQuestId = 10914;
        var step = new TodayQuestStepTemplate
        {
            Id = 41,
            RealStep = 13,
            SortId = TodayQuestStepTemplate.ExpeditionPublicBoardSortId
        };
        var group = new TodayQuestGroupTemplate { Id = 172, StepId = step.Id };
        group.QuestContextIds.Add(publicQuestId);
        step.Groups.Add(group);
        TodayQuestGameData.Instance.SetStepsForTest(step);
        try
        {
            var character = new Character(new UnitCustomModelParams()) { Id = 7, Name = "Member" };
            character.Quests = new CharacterQuests(character);

            var started = character.Quests.AddQuest(publicQuestId, forcibly: true);

            await Assert.That(started).IsFalse();
            await Assert.That(character.Quests.HasQuest(publicQuestId)).IsFalse();
        }
        finally
        {
            TodayQuestGameData.Instance.SetStepsForTest();
        }
    }
}
