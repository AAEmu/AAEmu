using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public class ShipyardCraftEffectTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Construction_ChargesSuccessfulActionAndRejectsWrongSkill(bool wrongSkill)
    {
        var world = new WorldManager(Mock.Of<ITickManager>().Object, Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));
        var interaction = (WorldInteractionType)1;
        typeof(WorldManager).GetField("_worldInteractionGroups", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(world, new Dictionary<uint, WorldInteractionGroup> { [1] = WorldInteractionGroup.Craft });
        using var worldScope = new SingletonScope<WorldManager>(world);
        using var skills = new SingletonScope<SkillManager>(new SkillManager(
            Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object));
        using var quests = new SingletonScope<QuestManager>(new QuestManager(
            Mock.Of<ITaskManager>().Object, Mock.Of<IZoneManager>().Object));
        var character = new LaborCharacter { Id = 1, ObjId = 1 };
        character.Craft = new CharacterCraft(character, Mock.Of<ICraftManager>().Object,
            Mock.Of<IDoodadManager>().Object, Mock.Of<ISkillManager>().Object,
            Mock.Of<IItemManager>().Object, Mock.Of<IZoneManager>().Object);
        character.InitializeLaborCache(100, 0, DateTime.UtcNow);
        var inventory = DetachedInventory.Create(character);
        var material = new ItemMock(50, 3)
        {
            OwnerId = character.Id, Slot = 0, SlotType = SlotType.Inventory,
            _holdingContainer = inventory.Bag
        };
        inventory.Bag.Items.Add(material);
        var shipyard = new Shipyard
        {
            ObjId = 2,
            ShipyardData = new ShipyardData(),
            Template = new ShipyardsTemplate
            {
                ShipyardSteps = new() { [0] = new ShipyardSteps { SkillId = 30, NumActions = 5 } }
            }
        };
        var skill = new Skill(new SkillTemplate
        {
            Id = wrongSkill ? 31u : 30u,
            ConsumeLaborPower = 10,
            Effects =
            [
                new SkillEffect
                {
                    Template = new CraftEffect { WorldInteraction = interaction },
                    EndLevel = 255, Chance = 100, ApplicationMethod = SkillEffectApplicationMethod.Target,
                    ConsumeItemId = material.TemplateId, ConsumeItemCount = 1
                }
            ]
        });

        // Runs the real effect dispatch and queued material consumption, then ordinary skill labor settlement.
        skill.ApplyEffects(character, new SkillCasterUnit { ObjId = character.ObjId }, shipyard,
            new SkillCastUnitTarget(shipyard.ObjId), null);
        skill.EndSkill(character);

        await Assert.That(shipyard.CurrentAction).IsEqualTo(wrongSkill ? 0 : 1);
        await Assert.That(material.Count).IsEqualTo(wrongSkill ? 3 : 2);
        await Assert.That(character.LaborPower).IsEqualTo((short)(wrongSkill ? 100 : 90));
        await Assert.That(skill.Cancelled).IsEqualTo(wrongSkill);
    }

    private sealed class LaborCharacter : CharacterMock
    {
        // Replace the account database/XP boundary, retaining Skill's real eligibility and once-only charge logic.
        public override void ChangeLabor(short change, int actabilityId) =>
            InitializeLaborCache(checked((short)(LaborPower + change)), LocalLaborPower, LaborPowerModified);
    }

    private sealed class SingletonScope<T> : IDisposable where T : class
    {
        private readonly FieldInfo _field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;
        public SingletonScope(T value)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, value);
        }
        public void Dispose() => _field.SetValue(null, _previous);
    }
}
