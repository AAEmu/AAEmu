using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Taxations;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Packets.Zw;
using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.WorldServer;

[NotInParallel]
public class ZonePacketWireTests
{
    private FieldInfo _butlerManagerSingletonField;
    private object _previousButlerManager;

    [Before(Test)]
    public void SetupButlerManager()
    {
        _butlerManagerSingletonField = typeof(Singleton<ButlerManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousButlerManager = _butlerManagerSingletonField.GetValue(null);
        _butlerManagerSingletonField.SetValue(null, new ButlerManager(
            Mock.Of<IButlerRepository>().Object,
            Mock.Of<IButlerUnbindService>().Object,
            Mock.Of<IItemManager>().Object));
    }

    [After(Test)]
    public void RestoreButlerManager()
    {
        _butlerManagerSingletonField.SetValue(null, _previousButlerManager);
    }

    [Test]
    public async Task SkillEnded_UsesNativeTimelineAndCasterLayout()
    {
        var frame = new PacketStream(
            new WZSkillEndedPacket(0x1234, new SkillCasterUnit(0x010203)).Encode());

        await Assert.That(frame.ReadUInt16()).IsEqualTo((ushort)8); // opcode + u16 timeline + caster type/Bc
        await Assert.That(frame.ReadUInt16()).IsEqualTo(WzOpcodes.SkillEnded);
        await Assert.That(frame.ReadUInt16()).IsEqualTo((ushort)0x1234);
        await Assert.That(frame.ReadByte()).IsEqualTo((byte)SkillCasterType.Unit);
        await Assert.That(frame.ReadBc()).IsEqualTo(0x010203u);
        await Assert.That(frame.Pos).IsEqualTo(frame.Count);
    }

    [Test]
    public async Task TargetChanged_WritesUnitThenTarget()
    {
        var stream = new PacketStream();
        new SCTargetChangedPacket(0x010203, 0x123456).Write(stream);

        await Assert.That(stream.Count).IsEqualTo(6);
        stream.Rollback();
        await Assert.That(stream.ReadBc()).IsEqualTo(0x010203u);
        await Assert.That(stream.ReadBc()).IsEqualTo(0x123456u);
    }

    [Test]
    public async Task UnitBond_WritesBondDataAndTrailingUnitRoot()
    {
        // Free-world doodad ObjIds live above unit space; body carries the seat, root must not.
        var doodad = new Doodad { ObjId = 105992 };
        var bond = new BondDoodad(
            doodad, AttachPointKind.Driver, BondKind.BondChairSingle, space: 7, spot: 11);

        var freeRoot = BondDoodad.ResolveZoneRootUnitId(doodad);
        await Assert.That(freeRoot).IsEqualTo(0u);

        var frame = new PacketStream(new WZUnitBondToDoodadPacket(0x040506, bond, freeRoot).Encode());

        await Assert.That(frame.ReadUInt16()).IsEqualTo((ushort)24); // opcode + 22-byte body
        await Assert.That(frame.ReadUInt16()).IsEqualTo(WzOpcodes.UnitBondToDoodad);
        await Assert.That(frame.ReadBc()).IsEqualTo(0x040506u);
        await Assert.That(frame.ReadByte()).IsEqualTo((byte)AttachPointKind.Driver);
        await Assert.That(frame.ReadBc()).IsEqualTo(doodad.ObjId);
        await Assert.That(frame.ReadInt32()).IsEqualTo(7);
        await Assert.That(frame.ReadInt32()).IsEqualTo(11);
        await Assert.That(frame.ReadUInt32()).IsEqualTo((uint)BondKind.BondChairSingle);
        await Assert.That(frame.ReadBc()).IsEqualTo(0u);
        await Assert.That(frame.Pos).IsEqualTo(frame.Count);

        var houseUnitId = 1500u;
        doodad.ParentObjId = houseUnitId;
        await Assert.That(BondDoodad.ResolveZoneRootUnitId(doodad)).IsEqualTo(houseUnitId);

        doodad.ParentObjId = 105999u;
        await Assert.That(BondDoodad.ResolveZoneRootUnitId(doodad)).IsEqualTo(0u);
        await Assert.That(BondDoodad.ResolveZoneRootUnitId(null)).IsEqualTo(0u);
    }

    [Test]
    public async Task SeatLeaveIntent_RequiresLocomotionFlagsNotResidueVel()
    {
        await Assert.That(
            BondDoodad.IsIntentionalSeatLeave(MoveTypeFlags.None, (ushort)MoveTypeActorFlags.None))
            .IsFalse();

        await Assert.That(
            BondDoodad.IsIntentionalSeatLeave(MoveTypeFlags.Stopping, (ushort)MoveTypeActorFlags.None))
            .IsFalse();

        await Assert.That(
            BondDoodad.IsIntentionalSeatLeave(MoveTypeFlags.Moving, (ushort)MoveTypeActorFlags.None))
            .IsTrue();

        await Assert.That(
            BondDoodad.IsIntentionalSeatLeave(MoveTypeFlags.Jumping, (ushort)MoveTypeActorFlags.None))
            .IsTrue();

        await Assert.That(
            BondDoodad.IsIntentionalSeatLeave(MoveTypeFlags.None, (ushort)MoveTypeActorFlags.Jumping))
            .IsTrue();
    }

    [Test]
    public async Task SlaveMasterChanged_WritesPersistentMasterAndWorldId()
    {
        var frame = new PacketStream(
            new WZSlaveMasterChangedPacket(0x040506, 0x0102030405060708L, 9).Encode());

        await Assert.That(frame.ReadUInt16()).IsEqualTo((ushort)14); // opcode + 12-byte body
        await Assert.That(frame.ReadUInt16()).IsEqualTo(WzOpcodes.SlaveMasterChanged);
        await Assert.That(frame.ReadBc()).IsEqualTo(0x040506u);
        await Assert.That(frame.ReadInt64()).IsEqualTo(0x0102030405060708L);
        await Assert.That(frame.ReadByte()).IsEqualTo((byte)9);
        await Assert.That(frame.Pos).IsEqualTo(frame.Count);
    }

    [Test]
    public async Task HouseBuildPackets_UseTimelineNotDatabaseId()
    {
        var done = new PacketStream(new WZHouseBuildDonePacket(0x1234).Encode());
        await Assert.That(done.ReadUInt16()).IsEqualTo((ushort)4); // opcode + 2-byte body
        await Assert.That(done.ReadUInt16()).IsEqualTo(WzOpcodes.HouseBuildDone);
        await Assert.That(done.ReadUInt16()).IsEqualTo((ushort)0x1234);
        await Assert.That(done.Pos).IsEqualTo(done.Count);

        var progress = new PacketStream(
            new WZHouseBuildProgressPacket(0x1234, 5, 100, 40).Encode());
        await Assert.That(progress.ReadUInt16()).IsEqualTo((ushort)16); // opcode + 14-byte body
        await Assert.That(progress.ReadUInt16()).IsEqualTo(WzOpcodes.HouseBuildProgress);
        await Assert.That(progress.ReadUInt16()).IsEqualTo((ushort)0x1234);
        await Assert.That(progress.ReadUInt32()).IsEqualTo(5u);
        await Assert.That(progress.ReadInt32()).IsEqualTo(100);
        await Assert.That(progress.ReadInt32()).IsEqualTo(40);
        await Assert.That(progress.Pos).IsEqualTo(progress.Count);
    }

    [Test]
    public async Task HouseState_UsesNativeCompressedFieldsAndFullPositions()
    {
        var house = CreateHouse();
        var template = house.Template;
        house.Transform.Local.SetPosition(100f, 200f, 12.5f);

        var stream = new PacketStream(HousingZoneBridge.BuildHouseStateBody(house));
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0x1234);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0x11223344u);
        await Assert.That(stream.ReadBc()).IsEqualTo(0x050607u);
        await Assert.That(stream.ReadPisc(3)).IsEquivalentTo(new uint[] { template.Id, 10, 4 });
        // Verified in-game: wire moneyAmount is SellPrice (44), not tax (75).
        await Assert.That(stream.ReadInt64()).IsEqualTo(44L);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.ReadInt64()).IsEqualTo(11L);
        await Assert.That(stream.ReadInt64()).IsEqualTo(22L);
        await Assert.That(stream.ReadString()).IsEqualTo("");
        await Assert.That(stream.ReadUInt64()).IsEqualTo(33UL);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)HousingPermission.Private);
        await Assert.That(stream.ReadInt64()).IsEqualTo(Helpers.ConvertLongX(100f));
        await Assert.That(stream.ReadInt64()).IsEqualTo(Helpers.ConvertLongY(200f));
        await Assert.That(stream.ReadSingle()).IsEqualTo(12.5f);
        await Assert.That(stream.ReadString()).IsEqualTo("Test House");
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadInt64()).IsEqualTo(44L);
        await Assert.That(stream.ReadString()).IsEqualTo("");
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(55u);
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);

        for (var i = 0; i < 5; i++)
        {
            await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
            await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
            await Assert.That(stream.ReadInt32()).IsEqualTo(0);
            await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        }

        for (var i = 0; i < 2; i++)
        {
            await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
            await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
            await Assert.That(stream.ReadSingle()).IsEqualTo(0f);
        }

        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task HouseCreated_BuildsHousingUnitStateBeforeProgressState()
    {
        var house = CreateHouse();
        byte[] actualUnitState = null;
        byte[] actualHouseState = null;
        uint actualZoneId = 0;
        ushort actualTl = 0;
        uint actualModel = 0;
        int actualAll = 0;
        int actualCurrent = 0;
        WorldIntegration.ZoneAuthority = true;
        WorldIntegration.RelayHouseStateToZone = (zoneId, unitState, houseState) =>
        {
            actualZoneId = zoneId;
            actualUnitState = unitState;
            actualHouseState = houseState;
        };
        WorldIntegration.RelayHouseBuildProgressToZone = (zoneId, tl, model, all, current) =>
        {
            actualZoneId = zoneId;
            actualTl = tl;
            actualModel = model;
            actualAll = all;
            actualCurrent = current;
        };

        try
        {
            HousingZoneBridge.NotifyZoneHouseCreated(house);

            await Assert.That(actualHouseState).IsNotNull();
            await Assert.That(actualZoneId).IsEqualTo(house.Transform.ZoneId);
            var unit = new PacketStream(actualUnitState);
            await Assert.That(unit.ReadBc()).IsEqualTo(house.ObjId);
            await Assert.That(unit.ReadString()).IsEqualTo(house.Name);
            await Assert.That(unit.ReadByte()).IsEqualTo((byte)0xFF);
            await Assert.That(unit.ReadByte()).IsEqualTo((byte)0xFF);
            await Assert.That(unit.ReadBoolean()).IsFalse();
            await Assert.That(unit.ReadByte()).IsEqualTo((byte)BaseUnitType.Housing);
            await Assert.That(unit.ReadUInt16()).IsEqualTo(house.TlId);
            await Assert.That(unit.ReadUInt32()).IsEqualTo(house.TemplateId);
            await Assert.That(unit.ReadUInt16()).IsEqualTo((ushort)house.CurrentStep);
            await Assert.That(actualTl).IsEqualTo(house.TlId);
            await Assert.That(actualModel).IsEqualTo(house.ModelId);
            await Assert.That(actualAll).IsEqualTo(house.AllAction);
            await Assert.That(actualCurrent).IsEqualTo(house.CurrentAction);
        }
        finally
        {
            WorldIntegration.ZoneAuthority = false;
            WorldIntegration.RelayHouseStateToZone = null;
            WorldIntegration.RelayHouseBuildProgressToZone = null;
        }
    }

    [Test]
    public async Task HouseModelPosture_PacksDoorAndWindowFlagsIntoOneByte()
    {
        var stream = new PacketStream();
        Unit.ModelPosture(stream, CreateHouse(), animActionId: 0, activateAnimation: true);

        await Assert.That(stream.Count).IsEqualTo(3);
        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ModelPostureType.HouseState);
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0xFF);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task RemoveHouse_ReadsExactTimelineId()
    {
        ushort actualTl = 0;
        WorldIntegration.OnZoneRemoveHouse = tl => actualTl = tl;
        try
        {
            var body = new PacketStream();
            body.Write((ushort)0x1234);
            var handled = new ZoneSimRelay().TryHandle(
                ZwOpcodes.RemoveHouse, body.GetBytes(), body.Count);

            await Assert.That(handled).IsTrue();
            await Assert.That(actualTl).IsEqualTo((ushort)0x1234);

            body.Write((ushort)0x5678);
            handled = new ZoneSimRelay().TryHandle(
                ZwOpcodes.RemoveHouse, body.GetBytes(), body.Count);
            await Assert.That(handled).IsFalse();
        }
        finally
        {
            WorldIntegration.OnZoneRemoveHouse = null;
        }
    }

    [Test]
    public async Task CreateDoodad_ModelKindDoesNotAddFreshnessAndUsesUnixTimestamps()
    {
        // Freshness is gated by ItemBackpack(item_id).type ∈ {3,8}, NOT model_kind_id.
        // Putting ModelKindId in pisc[2] made dedicate expect/reject the wrong size (zone 133).
        var normal = CreateDoodad(modelKindId: 0);
        var flowerKind = CreateDoodad(modelKindId: 3);
        var itemManager = Mock.Of<IItemManager>();
        var normalBytes = new WZCreateDoodadPacket(normal, itemManager.Object).Encode();
        var flowerBytes = new WZCreateDoodadPacket(flowerKind, itemManager.Object).Encode();

        await Assert.That(flowerBytes.Length).IsEqualTo(normalBytes.Length);

        var stream = new PacketStream(normalBytes);
        stream.ReadUInt16();
        await Assert.That(stream.ReadUInt16()).IsEqualTo(WzOpcodes.CreateDoodad);
        stream.ReadBc();
        var pisc = stream.ReadPisc(4);
        await Assert.That(pisc[2]).IsEqualTo(0u);
        stream.ReadByte();
        stream.ReadBc();
        stream.ReadBc();
        stream.ReadByte();
        stream.ReadPosition();
        stream.ReadInt16();
        stream.ReadInt16();
        stream.ReadInt16();
        stream.ReadSingle();
        stream.ReadInt64();
        stream.ReadInt64();
        stream.ReadUInt32();
        stream.ReadUInt32();
        var plantTime = stream.ReadUInt64();
        stream.ReadInt32();
        stream.ReadInt32();
        stream.ReadByte();
        stream.ReadUInt32();
        stream.ReadInt32();
        stream.ReadInt32();
        var updatedTime = stream.ReadUInt64();

        await Assert.That(plantTime).IsEqualTo(1_700_000_000UL);
        await Assert.That(updatedTime).IsLessThan(10_000_000_000UL);
    }

    [Test]
    public async Task PhysicalGoodsPackets_WritePersistedFreshnessAndZeroCompanions()
    {
        var template = new BackpackTemplate
        {
            Id = 31840,
            BackpackType = BackpackType.TradePack,
            FreshnessGroupId = 7
        };
        var freshnessStart = DateTimeOffset.FromUnixTimeSeconds(1_777_777_777).UtcDateTime;
        var backpack = new Backpack(900, template, 1);
        backpack.InitializeFreshness(freshnessStart, 22);
        var itemManager = Mock.Of<IItemManager>();
        itemManager.GetItemByItemId(backpack.Id).Returns(backpack);
        itemManager.GetTemplate(template.Id).Returns(template);
        var doodad = CreateDoodad(modelKindId: 0);
        doodad.ItemId = backpack.Id;
        doodad.ItemTemplateId = template.Id;

        var scCreated = new PacketStream();
        doodad.Write(scCreated, itemManager.Object);
        var scCreatedPisc = ReadDoodadCreateToUpdatedTime(scCreated, hasFrameHeader: false);

        var wzCreated = new PacketStream(new WZCreateDoodadPacket(doodad, itemManager.Object).Encode());
        var wzCreatedPisc = ReadDoodadCreateToUpdatedTime(wzCreated, hasFrameHeader: true);

        var phase = new PacketStream();
        new SCDoodadPhaseChangedPacket(doodad, itemManager.Object).Write(phase);
        phase.Rollback();
        phase.ReadBc();
        phase.ReadUInt32();
        phase.ReadInt32();
        phase.ReadUInt32();
        phase.ReadInt32();

        await Assert.That(scCreatedPisc[2]).IsEqualTo(template.Id);
        await AssertGoodsBlock(scCreated, 1_777_777_777UL);
        await Assert.That(scCreated.ReadInt64()).IsEqualTo(0L);
        await Assert.That(scCreated.ReadInt64()).IsEqualTo(0L);
        await Assert.That(scCreated.Pos).IsEqualTo(scCreated.Count);
        await Assert.That(wzCreatedPisc[2]).IsEqualTo(template.Id);
        await AssertGoodsBlock(wzCreated, 1_777_777_777UL);
        await Assert.That(wzCreated.ReadInt64()).IsEqualTo(0L);
        await Assert.That(wzCreated.ReadInt64()).IsEqualTo(0L);
        await Assert.That(wzCreated.Pos).IsEqualTo(wzCreated.Count);
        await Assert.That(phase.ReadUInt32()).IsEqualTo(template.Id);
        await Assert.That(phase.ReadBoolean()).IsTrue();
        await AssertGoodsBlock(phase, 1_777_777_777UL);
        await Assert.That(phase.Pos).IsEqualTo(phase.Count);
    }

    [Test]
    public async Task PhysicalGoodsResolver_UsesZeroForMalformedPersistedFreshness()
    {
        var template = new BackpackTemplate
        {
            Id = 31840,
            BackpackType = BackpackType.TradePack,
            FreshnessGroupId = 7
        };
        var backpack = new Backpack(900, template, 1)
        {
            DetailType = ItemDetailType.BackpackFreshness,
            Detail = new byte[9]
        };
        var itemManager = Mock.Of<IItemManager>();
        itemManager.GetItemByItemId(backpack.Id).Returns(backpack);
        var doodad = CreateDoodad(modelKindId: 0);
        doodad.ItemId = backpack.Id;
        doodad.ItemTemplateId = template.Id;

        var isGoods = DoodadPhysicalGoods.TryResolve(doodad, itemManager.Object, out var goods);

        await Assert.That(isGoods).IsTrue();
        await Assert.That(goods.ItemTemplateId).IsEqualTo(template.Id);
        await Assert.That(goods.FreshnessTime).IsEqualTo(0UL);
    }

    [Test]
    public async Task AreaEvents_ReadCompactUnitAndAreaGroup()
    {
        uint actualUnit = 0;
        uint actualArea = 0;
        int actualValue1 = 0;
        int actualValue2 = 0;
        bool? actualEntering = null;
        uint actualZone = 0;
        WorldIntegration.OnZoneEnterArea = (unit, area, value1, value2) =>
        {
            actualUnit = unit;
            actualArea = area;
            actualValue1 = value1;
            actualValue2 = value2;
        };
        WorldIntegration.OnZoneAreaEvent = (zoneId, unit, area, value1, value2, entering) =>
        {
            actualZone = zoneId;
            actualEntering = entering;
        };

        try
        {
            var body = new PacketStream();
            body.WriteBc(0x010203);
            body.Write((byte)0x16);
            body.Write(-123);
            body.Write(456);

            var handled = new ZoneSimRelay().TryHandle(
                ZwOpcodes.EnterArea, body.GetBytes(), body.Count);

            await Assert.That(handled).IsTrue();
            await Assert.That(actualUnit).IsEqualTo(0x010203u);
            await Assert.That(actualArea).IsEqualTo(0x16u);
            await Assert.That(actualValue1).IsEqualTo(-123);
            await Assert.That(actualValue2).IsEqualTo(456);
            await Assert.That(actualZone).IsEqualTo(0u);
            await Assert.That(actualEntering).IsTrue();
        }
        finally
        {
            WorldIntegration.OnZoneEnterArea = null;
            WorldIntegration.OnZoneAreaEvent = null;
        }
    }

    [Test]
    public async Task AreaEvents_LeaveCarriesTheZoneThatReportedIt()
    {
        bool? actualEntering = null;
        uint actualZone = 0;
        WorldIntegration.OnZoneAreaEvent = (zoneId, unit, area, value1, value2, entering) =>
        {
            actualZone = zoneId;
            actualEntering = entering;
        };

        try
        {
            var body = new PacketStream();
            body.WriteBc(0x010203);
            body.Write((byte)0x16);
            body.Write(25);
            body.Write(0);

            var handled = new ZoneSimRelay().TryHandle(
                ZwOpcodes.LeaveArea, body.GetBytes(), body.Count);

            await Assert.That(handled).IsTrue();
            await Assert.That(actualZone).IsEqualTo(0u);
            await Assert.That(actualEntering).IsFalse();
        }
        finally
        {
            WorldIntegration.OnZoneAreaEvent = null;
        }
    }

    [Test]
    public async Task AreaEvents_AThrowingConsumerDoesNotStopTheOthersOrThePacket()
    {
        var legacyEnterRan = false;
        WorldIntegration.OnZoneAreaEvent = (_, _, _, _, _, _) =>
            throw new InvalidOperationException("area-event consumer blew up");
        WorldIntegration.OnZoneEnterArea = (_, _, _, _) => legacyEnterRan = true;

        try
        {
            var handled = new ZoneSimRelay().TryHandle(ZwOpcodes.EnterArea, AreaBody(), 12);

            // The legacy quest-area hook still ran, and a parsed-and-dispatched packet
            // is never reported back as unhandled because one consumer threw.
            await Assert.That(legacyEnterRan).IsTrue();
            await Assert.That(handled).IsTrue();
        }
        finally
        {
            WorldIntegration.OnZoneAreaEvent = null;
            WorldIntegration.OnZoneEnterArea = null;
        }
    }

    [Test]
    public async Task AreaEvents_AThrowingLegacyConsumerStillLeavesThePacketHandled()
    {
        var areaEventRan = false;
        WorldIntegration.OnZoneAreaEvent = (_, _, _, _, _, _) => areaEventRan = true;
        WorldIntegration.OnZoneLeaveArea = (_, _, _, _) =>
            throw new InvalidOperationException("legacy consumer blew up");

        try
        {
            var handled = new ZoneSimRelay().TryHandle(ZwOpcodes.LeaveArea, AreaBody(), 12);

            await Assert.That(areaEventRan).IsTrue();
            await Assert.That(handled).IsTrue();
        }
        finally
        {
            WorldIntegration.OnZoneAreaEvent = null;
            WorldIntegration.OnZoneLeaveArea = null;
        }
    }

    private static byte[] AreaBody()
    {
        var body = new PacketStream();
        body.WriteBc(0x010203);
        body.Write((byte)0x16);
        body.Write(25);
        body.Write(0);
        return body.GetBytes();
    }

    [Test]
    [Arguments(ZwOpcodes.EnterArea, 11, false)]
    [Arguments(ZwOpcodes.EnterArea, 13, false)]
    [Arguments(ZwOpcodes.EnterArea, 15, false)]
    [Arguments(ZwOpcodes.LeaveArea, 11, false)]
    [Arguments(ZwOpcodes.EnterArea, 12, true)]
    [Arguments(ZwOpcodes.LeaveArea, 12, true)]
    public async Task AreaEvents_AcceptOnlyTheExactWireLength(ushort opcode, int bodyLen, bool expected)
    {
        var body = new byte[bodyLen];
        if (bodyLen > 3)
        {
            body[0] = 0x01;
            body[1] = 0x02;
            body[2] = 0x03;
        }

        var handled = new ZoneSimRelay().TryHandle(opcode, body, bodyLen);

        await Assert.That(handled).IsEqualTo(expected);
    }

    private static Doodad CreateDoodad(uint modelKindId) => new()
    {
        ObjId = 1,
        TemplateId = 2,
        Template = new DoodadTemplate { ModelKindId = modelKindId },
        OwnerType = DoodadOwnerType.Character,
        PlantTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime
    };

    private static uint[] ReadDoodadCreateToUpdatedTime(PacketStream stream, bool hasFrameHeader)
    {
        stream.Rollback();
        if (hasFrameHeader)
        {
            stream.ReadUInt16();
            stream.ReadUInt16();
        }
        stream.ReadBc();
        var pisc = stream.ReadPisc(4);
        stream.ReadByte();
        stream.ReadBc();
        stream.ReadBc();
        stream.ReadByte();
        stream.ReadPosition();
        stream.ReadInt16();
        stream.ReadInt16();
        stream.ReadInt16();
        stream.ReadSingle();
        stream.ReadInt64();
        stream.ReadInt64();
        stream.ReadUInt32();
        stream.ReadUInt32();
        stream.ReadUInt64();
        stream.ReadInt32();
        stream.ReadInt32();
        stream.ReadByte();
        stream.ReadUInt32();
        stream.ReadInt32();
        stream.ReadInt32();
        stream.ReadUInt64();
        return pisc;
    }

    private static async Task AssertGoodsBlock(PacketStream stream, ulong freshnessTime)
    {
        await Assert.That(stream.ReadUInt64()).IsEqualTo(freshnessTime);
        await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0);
    }

    private static House CreateHouse()
    {
        var template = new HousingTemplate
        {
            Id = 0x01020304,
            MainModelId = 900,
            Taxation = new Taxation { Tax = 75 },
            HousingBindingDoodad = []
        };
        template.BuildSteps.Add(0, new HousingBuildStep { ModelId = 800, NumActions = 10 });
        return new House
        {
            Template = template,
            TemplateId = template.Id,
            Id = 0x11223344,
            ObjId = 0x050607,
            TlId = 0x1234,
            Name = "Test House",
            CoOwnerId = 11,
            OwnerId = 22,
            AccountId = 33,
            Permission = HousingPermission.Private,
            AllowRecover = true,
            SellPrice = 44,
            SellToPlayerId = 55,
            CurrentStep = 0,
            NumAction = 4
        };
    }
}
