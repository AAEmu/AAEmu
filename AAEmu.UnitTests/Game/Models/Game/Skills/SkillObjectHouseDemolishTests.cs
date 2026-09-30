using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Interactions;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>X2House:Demolish(package, sealCount)</c> casts the demolish skill with skill-object type 0x11:
/// <c>bool package, u32 sealCount</c>, then the usual inputDirection byte (x2game FUN_39998130 and the
/// skill-object serializer FUN_39c7cfa0, case 0x11).
/// </summary>
public sealed class SkillObjectHouseDemolishTests
{
    private const int DemolishFlag = 0x11;
    private const uint DemolishSkillId = 12945;

    [Test]
    public async Task DemolishFlag_IsAShapeTheServerReads()
    {
        // Unknown types are clamped to None without reading their body.
        await Assert.That(SkillObject.IsKnownType(DemolishFlag)).IsTrue();
    }

    [Test]
    public async Task DemolishBody_IsReadUpToTheInputDirection()
    {
        // package true, sealCount 3, then inputDirection 0.
        var stream = new PacketStream([0x01, 0x03, 0x00, 0x00, 0x00, 0x00]);

        SkillObject.GetByType((SkillObjectType)DemolishFlag).Read(stream);

        await Assert.That(stream.LeftBytes).IsEqualTo(1);
    }

    [Test]
    public async Task DemolishBody_CarriesThePackageChoiceAndSealCount()
    {
        var obj = SkillObject.GetByType(SkillObjectType.HouseDemolish);
        obj.Read(new PacketStream([0x01, 0x26, 0x00, 0x00, 0x00, 0x00]));

        await Assert.That(obj).IsTypeOf<SkillObjectHouseDemolish>();
        var demolish = (SkillObjectHouseDemolish)obj;
        await Assert.That(demolish.Flag).IsEqualTo(SkillObjectType.HouseDemolish);
        await Assert.That(demolish.Package).IsTrue();
        await Assert.That(demolish.SealCount).IsEqualTo(38u);
    }

    [Test]
    public async Task SkillStartedEcho_IsFlagNone()
    {
        var stream = new PacketStream();
        stream.WriteSkillCastExtra(new SkillObjectHouseDemolish { Flag = SkillObjectType.HouseDemolish, Package = true, SealCount = 3 });

        // flag 0 and inputDirection 0, as before this type was known.
        await Assert.That(stream.GetBytes()).IsEquivalentTo(new byte[] { 0, 0 });
    }

    [Test]
    public async Task PlainDemolition_IsNotRefused()
    {
        var house = House(currentStep: -1);

        await Assert.That(Demolish.PackageRefusal(house, null)).IsNull();
        await Assert.That(Demolish.PackageRefusal(house, new SkillObject())).IsNull();
        await Assert.That(Demolish.PackageRefusal(house, new SkillObjectHouseDemolish { Package = false })).IsNull();
    }

    [Test]
    public async Task FullKitDemolition_IsRefused()
    {
        var request = new SkillObjectHouseDemolish { Package = true, SealCount = 1 };

        await Assert.That(Demolish.PackageRefusal(House(currentStep: 0), request))
            .IsEqualTo(ErrorMessageType.HousePackageDemolishFailedUnderConstruction);
        await Assert.That(Demolish.PackageRefusal(House(currentStep: -1), request))
            .IsEqualTo(ErrorMessageType.InternalError);
    }

    [Test]
    public async Task InteractionEffect_HandsTheSkillObjectToDemolish()
    {
        // A Full Kit request that reaches Demolish is answered with an error and never gets as far as
        // HousingManager. Without the skill object, Demolish would run a plain demolition instead.
        var sent = new List<byte[]>();
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => sent.Add(bytes));
        var connection = new GameConnection(session.Object);
        var owner = new Character(new UnitCustomModelParams())
        {
            Id = 5,
            ObjId = 5,
            Name = "Owner",
            Connection = connection,
        };
        connection.ActiveChar = owner;
        var house = House(currentStep: -1);
        house.OwnerId = owner.Id;

        var effect = new InteractionEffect { WorldInteraction = WorldInteractionType.Demolish };
        effect.Apply(owner, new SkillCasterUnit(owner.ObjId), house, new SkillCastUnitTarget(house.ObjId),
            new CastSkill(DemolishSkillId, 1), new EffectSource(new Skill(new SkillTemplate { Id = DemolishSkillId })),
            new SkillObjectHouseDemolish { Flag = SkillObjectType.HouseDemolish, Package = true, SealCount = 1 },
            DateTime.UtcNow);

        var (opcode, _) = SentPacket.Read(sent.Single());
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCErrorMsgPacket);
        await Assert.That(house.OwnerId).IsEqualTo(owner.Id);
    }

    private static House House(int currentStep)
    {
        var template = new HousingTemplate { Id = 175, Hp = 1000, HousingBindingDoodad = [] };
        template.BuildSteps.Add(0, new HousingBuildStep { Step = 0, ModelId = 2722, SkillId = 14575, NumActions = 1 });
        var house = new House { Id = 1, ObjId = 903, TemplateId = template.Id, Template = template };
        house.CurrentStep = currentStep;
        house.Hp = 1000;
        return house;
    }
}
