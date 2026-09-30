using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Housing;

/// <summary>
/// What a house offers on <c>SCNpcInteractionSkillList</c>. The client acts on the first entry only:
/// 11001/12106 open the house window, anything else is cast on the house.
/// </summary>
public sealed class HouseInteractionRulesTests
{
    // Stone house step: skill 14575 consumes one stone pack (17684) in its CraftEffect.
    private const uint StepSkillId = 14575;
    private const uint StonePack = 17684;

    [Test]
    public async Task FinishedHouse_OpensTheHouseWindow()
    {
        var house = House(currentStep: -1);

        var skills = HouseInteractionRules.ComposeSkills(house, _ => StepSkill(), _ => 0);

        await Assert.That(skills).IsEquivalentTo([SkillsEnum.HousingInteraction]);
    }

    [Test]
    public async Task UnfinishedHouse_WithTheStepMaterials_OffersTheBuildStep()
    {
        var house = House(currentStep: 0);

        var skills = HouseInteractionRules.ComposeSkills(house, _ => StepSkill(),
            itemId => itemId == StonePack ? 1 : 0);

        await Assert.That(skills).IsEquivalentTo([StepSkillId]);
    }

    [Test]
    public async Task UnfinishedHouse_WithoutTheStepMaterials_OpensTheMaterialsWindow()
    {
        var house = House(currentStep: 0);

        var skills = HouseInteractionRules.ComposeSkills(house, _ => StepSkill(), _ => 0);

        await Assert.That(skills).IsEquivalentTo([SkillsEnum.ConstructionInfo]);
    }

    [Test]
    public async Task UnfinishedHouse_MaterialsAreSummedPerItem()
    {
        var house = House(currentStep: 0);
        var skill = StepSkill();
        skill.Effects.Add(new SkillEffect { ConsumeItemId = StonePack, ConsumeItemCount = 2 });

        var oneShort = HouseInteractionRules.ComposeSkills(house, _ => skill, _ => 2);
        var enough = HouseInteractionRules.ComposeSkills(house, _ => skill, _ => 3);

        await Assert.That(oneShort).IsEquivalentTo([SkillsEnum.ConstructionInfo]);
        await Assert.That(enough).IsEquivalentTo([StepSkillId]);
    }

    [Test]
    public async Task UnfinishedHouse_StepThatConsumesNothing_OffersTheBuildStep()
    {
        var house = House(currentStep: 0);
        var skill = new SkillTemplate { Id = StepSkillId };
        skill.Effects.Add(new SkillEffect { ConsumeItemId = 0, ConsumeItemCount = 1 });

        var skills = HouseInteractionRules.ComposeSkills(house, _ => skill, _ => 0);

        await Assert.That(skills).IsEquivalentTo([StepSkillId]);
    }

    [Test]
    public async Task UnfinishedHouse_UnknownStepSkill_OpensTheMaterialsWindow()
    {
        var house = House(currentStep: 0);

        var skills = HouseInteractionRules.ComposeSkills(house, _ => null, _ => 99);

        await Assert.That(skills).IsEquivalentTo([SkillsEnum.ConstructionInfo]);
    }

    [Test]
    public async Task WreckedHouse_OffersNothing()
    {
        var house = House(currentStep: -1);
        house.Hp = 0;

        var skills = HouseInteractionRules.ComposeSkills(house, _ => StepSkill(), _ => 0);

        await Assert.That(skills).IsEmpty();
    }

    private static SkillTemplate StepSkill()
    {
        var skill = new SkillTemplate { Id = StepSkillId };
        skill.Effects.Add(new SkillEffect { ConsumeItemId = StonePack, ConsumeItemCount = 1 });
        // The step's buff effect consumes nothing.
        skill.Effects.Add(new SkillEffect { ConsumeItemId = 0, ConsumeItemCount = 1 });
        return skill;
    }

    private static House House(int currentStep)
    {
        var template = new HousingTemplate { Id = 175, Hp = 1000, HousingBindingDoodad = [] };
        template.BuildSteps.Add(0, new HousingBuildStep { Step = 0, ModelId = 2722, SkillId = StepSkillId, NumActions = 1 });
        template.BuildSteps.Add(1, new HousingBuildStep { Step = 1, ModelId = 2723, SkillId = 14574, NumActions = 1 });

        var house = new House { Id = 1, TemplateId = template.Id, Template = template };
        house.CurrentStep = currentStep;
        house.Hp = 1000;
        return house;
    }
}
