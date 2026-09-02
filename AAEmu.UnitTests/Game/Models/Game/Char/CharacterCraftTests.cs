using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class CharacterCraftTests
{
    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(CharacterCraft.MaxBatchCount + 1)]
    public async Task Craft_RejectsInvalidBatchCount(int count)
    {
        var context = CreateContext();

        var started = context.CharacterCraft.Craft(context.Craft, count, context.Doodad.ObjId);

        await Assert.That(started).IsFalse();
        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task Craft_RejectsCraftOutsideActivePack()
    {
        var context = CreateContext(craftInPack: false);

        var started = context.CharacterCraft.Craft(context.Craft, 1, context.Doodad.ObjId);

        await Assert.That(started).IsFalse();
        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task Craft_RejectsDespawnedDoodad()
    {
        var context = CreateContext();
        context.Doodad.Despawn = DateTime.UtcNow;

        var started = context.CharacterCraft.Craft(context.Craft, 1, context.Doodad.ObjId);

        await Assert.That(started).IsFalse();
        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task Craft_RejectsDoodadOutsideSkillRange()
    {
        var context = CreateContext(doodadX: 20f, maxRange: 5);

        var started = context.CharacterCraft.Craft(context.Craft, 1, context.Doodad.ObjId);

        await Assert.That(started).IsFalse();
        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task Craft_RejectsFreshPackFromMismatchedProductionZone()
    {
        var context = CreateContext(freshnessGroupId: 4, specialtyZoneId: 8, productionZoneGroupId: 22);

        var started = context.CharacterCraft.Craft(context.Craft, 1, context.Doodad.ObjId);

        await Assert.That(started).IsFalse();
        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task Craft_RejectsFreshPackWithoutLiveProductionZone()
    {
        var context = CreateContext(freshnessGroupId: 4, specialtyZoneId: 8);

        var started = context.CharacterCraft.Craft(context.Craft, 1, context.Doodad.ObjId);

        await Assert.That(started).IsFalse();
        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task Craft_RejectsMultipleItemsForAutoEquippedProduct()
    {
        var context = CreateContext(autoEquipProduct: true, productAmount: 2);

        var started = context.CharacterCraft.Craft(context.Craft, 1, context.Doodad.ObjId);

        await Assert.That(started).IsFalse();
        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task Craft_RejectsMultipleAutoEquippedProducts()
    {
        var context = CreateContext(autoEquipProduct: true, autoEquipProductRows: 2);

        var started = context.CharacterCraft.Craft(context.Craft, 1, context.Doodad.ObjId);

        await Assert.That(started).IsFalse();
        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task Craft_RejectsSecondStartWithoutCancellingActiveCraft()
    {
        var context = CreateContext();
        SeedActiveCraft(context, context.Doodad.FuncGroupId);

        var started = context.CharacterCraft.Craft(context.Craft, 1, context.Doodad.ObjId);

        await Assert.That(started).IsFalse();
        await Assert.That(context.CharacterCraft.IsCrafting).IsTrue();
    }

    [Test]
    public async Task EndCraft_RejectsChangedFunctionGroup()
    {
        const uint originalFunctionGroupId = 100;
        const uint currentFunctionGroupId = 101;
        var context = CreateContext(functionGroupId: currentFunctionGroupId);
        SeedActiveCraft(context, originalFunctionGroupId);

        context.CharacterCraft.EndCraft();

        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task EndCraft_RejectsDoodadThatDespawnedDuringCraft()
    {
        var context = CreateContext();
        SeedActiveCraft(context, context.Doodad.FuncGroupId);
        context.Doodad.Despawn = DateTime.UtcNow;

        context.CharacterCraft.EndCraft();

        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task EndCraft_RejectsChangedProductionZone()
    {
        var context = CreateContext(freshnessGroupId: 4, specialtyZoneId: 8, productionZoneGroupId: 8);
        SeedActiveCraft(context, context.Doodad.FuncGroupId);
        SetPrivateField(context.Doodad.Transform, "_zoneId", 71u);

        context.CharacterCraft.EndCraft();

        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    [Test]
    public async Task EndCraft_RejectsSkillOtherThanOriginatingInstance()
    {
        var context = CreateContext();
        var expectedSkill = new Skill(new SkillTemplate { Id = context.Craft.SkillId });
        SeedActiveCraft(context, context.Doodad.FuncGroupId, expectedSkill);
        var forgedSkill = new Skill(new SkillTemplate { Id = context.Craft.SkillId });

        var completed = context.CharacterCraft.EndCraft(forgedSkill);
        var repeated = context.CharacterCraft.EndCraft(forgedSkill);

        await Assert.That(completed).IsFalse();
        await Assert.That(repeated).IsFalse();
        await Assert.That(context.CharacterCraft.IsCrafting).IsFalse();
    }

    private static CraftContext CreateContext(
        bool craftInPack = true,
        uint functionGroupId = 100,
        float doodadX = 0f,
        int maxRange = 5,
        uint freshnessGroupId = 0,
        uint specialtyZoneId = 0,
        uint productionZoneGroupId = 0,
        bool autoEquipProduct = false,
        int productAmount = 1,
        int autoEquipProductRows = 1)
    {
        const uint craftId = 10;
        const uint craftPackId = 20;
        const uint skillId = 30;
        const uint doodadObjId = 40;
        const uint productItemId = 60;
        const uint zoneKey = 70;

        var craft = new Craft
        {
            Id = craftId,
            SkillId = skillId,
            CraftProducts = Enumerable.Range(0, autoEquipProductRows)
                .Select(_ => new CraftProduct { ItemId = productItemId, Amount = productAmount })
                .ToList()
        };
        var craftManager = new CraftManager();
        SetPrivateField(craftManager, "_craftsByPack", new Dictionary<uint, HashSet<uint>>
        {
            [craftPackId] = craftInPack ? [craftId] : [craftId + 1]
        });

        var itemManager = Mock.Of<IItemManager>();
        ItemTemplate productTemplate = freshnessGroupId > 0
            ? new BackpackTemplate
            {
                Id = productItemId,
                FreshnessGroupId = freshnessGroupId,
                SpecialtyZoneId = specialtyZoneId
            }
            : new ItemTemplate { Id = productItemId };
        itemManager.GetTemplate(productItemId).Returns(productTemplate);
        itemManager.IsAutoEquipTradePack(productItemId).Returns(autoEquipProduct);

        var zoneManager = Mock.Of<IZoneManager>();
        if (productionZoneGroupId > 0)
            zoneManager.GetZoneByKey(zoneKey).Returns(new Zone { ZoneKey = zoneKey, GroupId = productionZoneGroupId });

        var doodadManager = new DoodadManager(
            Mock.Of<INonUnitObjectIdManager>().Object,
            Mock.Of<IDoodadIdManager>().Object,
            itemManager.Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ISusManager>().Object,
            Mock.Of<IFactionManager>().Object);
        var function = new DoodadFunc
        {
            GroupId = functionGroupId,
            FuncId = 50,
            FuncType = nameof(DoodadFuncCraftPack)
        };
        var craftPack = new DoodadFuncCraftPack { Id = function.FuncId, CraftPackId = craftPackId };
        SetPrivateField(doodadManager, "_funcsByGroups", new Dictionary<uint, List<DoodadFunc>>
        {
            [functionGroupId] = [function]
        });
        SetPrivateField(doodadManager, "_funcTemplates", new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>
        {
            [nameof(DoodadFuncCraftPack)] = new() { [function.FuncId] = craftPack }
        });

        var skillManager = Mock.Of<ISkillManager>();
        skillManager.GetSkillTemplate(skillId).Returns(new SkillTemplate { Id = skillId, MaxRange = maxRange });

        var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "craft-test" }, 0, true, 1);
        var character = new CharacterMock { ObjId = 1 };
        SetPrivateField(character, "_parentWorld", world);
        character.Transform.Local.SetPosition(0f, 0f, 0f);
        var doodad = new Doodad { ObjId = doodadObjId };
        SetPrivateField(doodad, "_parentWorld", world);
        SetPrivateField(doodad.Transform, "_zoneId", zoneKey);
        doodad.Transform.Local.SetPosition(doodadX, 0f, 0f);
        SetPrivateField(doodad, "_funcGroupId", functionGroupId);
        world.AddObject(doodad);

        return new CraftContext(
            new TestCharacterCraft(
                character,
                craftManager,
                doodadManager,
                skillManager.Object,
                itemManager.Object,
                zoneManager.Object,
                Math.Abs(doodadX)),
            craft,
            doodad,
            craftPackId,
            productionZoneGroupId);
    }

    private static void SeedActiveCraft(CraftContext context, uint originalFunctionGroupId, Skill skill = null)
    {
        SetPrivateProperty(context.CharacterCraft, "CurrentCraft", context.Craft);
        SetPrivateProperty(context.CharacterCraft, "Count", 1);
        SetPrivateProperty(context.CharacterCraft, "DoodadId", context.Doodad.ObjId);
        SetPrivateProperty(context.CharacterCraft, "DoodadFuncGroupId", originalFunctionGroupId);
        SetPrivateProperty(context.CharacterCraft, "CraftPackId", context.CraftPackId);
        SetPrivateProperty(context.CharacterCraft, "ProductionZoneGroupId", context.ProductionZoneGroupId);
        SetPrivateProperty(context.CharacterCraft, "CurrentSkill", skill);
        context.CharacterCraft.IsCrafting = true;
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                continue;

            field.SetValue(target, value);
            return;
        }

        throw new InvalidOperationException($"Field {name} was not found on {target.GetType().Name}.");
    }

    private static void SetPrivateProperty(object target, string name, object value)
    {
        target.GetType()
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(target, value);
    }

    private sealed record CraftContext(
        CharacterCraft CharacterCraft,
        Craft Craft,
        Doodad Doodad,
        uint CraftPackId,
        uint ProductionZoneGroupId);

    private sealed class TestCharacterCraft(
        Character owner,
        ICraftManager craftManager,
        IDoodadManager doodadManager,
        ISkillManager skillManager,
        IItemManager itemManager,
        IZoneManager zoneManager,
        float doodadDistance)
        : CharacterCraft(owner, craftManager, doodadManager, skillManager, itemManager, zoneManager)
    {
        protected override float GetDistanceTo(Doodad doodad) => doodadDistance;
    }
}
