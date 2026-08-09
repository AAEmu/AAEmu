using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
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
        await Assert.That(stream.ReadInt64()).IsEqualTo(75L);
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
        var normalBytes = new WZCreateDoodadPacket(normal).Encode();
        var flowerBytes = new WZCreateDoodadPacket(flowerKind).Encode();

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
    public async Task AreaEvents_ReadCompactUnitAndByteArea()
    {
        uint actualUnit = 0;
        uint actualArea = 0;
        int actualValue1 = 0;
        int actualValue2 = 0;
        WorldIntegration.OnZoneEnterArea = (unit, area, value1, value2) =>
        {
            actualUnit = unit;
            actualArea = area;
            actualValue1 = value1;
            actualValue2 = value2;
        };

        try
        {
            var body = new PacketStream();
            body.WriteBc(0x010203);
            body.Write((byte)0x7F);
            body.Write(-123);
            body.Write(456);

            var handled = new ZoneSimRelay().TryHandle(
                ZwOpcodes.EnterArea, body.GetBytes(), body.Count);

            await Assert.That(handled).IsTrue();
            await Assert.That(actualUnit).IsEqualTo(0x010203u);
            await Assert.That(actualArea).IsEqualTo(0x7Fu);
            await Assert.That(actualValue1).IsEqualTo(-123);
            await Assert.That(actualValue2).IsEqualTo(456);
        }
        finally
        {
            WorldIntegration.OnZoneEnterArea = null;
        }
    }

    private static Doodad CreateDoodad(uint modelKindId) => new()
    {
        ObjId = 1,
        TemplateId = 2,
        Template = new DoodadTemplate { ModelKindId = modelKindId },
        OwnerType = DoodadOwnerType.Character,
        PlantTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime
    };

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
