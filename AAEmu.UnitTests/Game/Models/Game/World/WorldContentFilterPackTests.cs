using System;
using System.IO;
using System.Linq;
using System.Text;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class WorldContentFilterPackTests
{
    private static ushort ReadUInt16(byte[] data, ref int offset)
    {
        var value = (ushort)(data[offset] | (data[offset + 1] << 8));
        offset += 2;
        return value;
    }

    private static string ReadString(byte[] data, ref int offset)
    {
        var length = data[offset++];
        var value = Encoding.UTF8.GetString(data, offset, length);
        offset += length;
        return value;
    }

    [Test]
    public async Task CategoryIds_FollowTheClientsOwnNumbering()
    {
        await Assert.That(WorldContentFilterPack.CategoryCount).IsEqualTo(18);
        await Assert.That(WorldContentFilterPack.CategoryIdOf("craft")).IsEqualTo((byte)0);
        await Assert.That(WorldContentFilterPack.CategoryIdOf("Craft")).IsEqualTo((byte)0); // table spells it capitalised
        await Assert.That(WorldContentFilterPack.CategoryIdOf(" festival_zone ")).IsEqualTo((byte)17);
        await Assert.That(WorldContentFilterPack.CategoryIdOf("quest")).IsEqualTo((byte)7);

        // The table writes the categories without underscores: GameSchedule, NpcSpawner, ArchePass ...
        await Assert.That(WorldContentFilterPack.CategoryIdOf("GameSchedule")).IsEqualTo((byte)3);
        await Assert.That(WorldContentFilterPack.CategoryIdOf("NpcSpawner")).IsEqualTo((byte)5);
        await Assert.That(WorldContentFilterPack.CategoryIdOf("ArchePass")).IsEqualTo((byte)12);
        await Assert.That(WorldContentFilterPack.CategoryIdOf("FestivalZone")).IsEqualTo((byte)17);

        // Quest contexts are the client's quest category.
        await Assert.That(WorldContentFilterPack.CategoryIdOf("QuestContext")).IsEqualTo((byte)7);

        // Anything else stays unconfigured rather than being guessed onto a neighbour.
        await Assert.That(WorldContentFilterPack.CategoryIdOf("DoodadAlmighty")).IsNull();
        await Assert.That(WorldContentFilterPack.CategoryIdOf("no_such_category")).IsNull();
        await Assert.That(WorldContentFilterPack.CategoryIdOf(null)).IsNull();
    }

    [Test]
    public async Task Serialize_WritesCountThenGroupsThenEntries()
    {
        var craft = new WorldContentGroup { CategoryId = 0, CategoryName = "craft" };
        craft.Names.Add("Open_1");
        craft.Names.Add("epherium");

        var achievement = new WorldContentGroup { CategoryId = 15, CategoryName = "achievement" };
        achievement.Names.Add("first_steps");

        // handed over out of order: the pack has to come out in the client's category order
        var data = WorldContentFilterPack.Serialize([achievement, craft]);
        var offset = 0;

        await Assert.That(ReadUInt16(data, ref offset)).IsEqualTo((ushort)2);

        // no id on the wire: the category name is what the client matches on
        await Assert.That(ReadString(data, ref offset)).IsEqualTo("craft");
        await Assert.That(ReadUInt16(data, ref offset)).IsEqualTo((ushort)2);
        await Assert.That(ReadString(data, ref offset)).IsEqualTo("Open_1");
        await Assert.That(ReadString(data, ref offset)).IsEqualTo("epherium");

        await Assert.That(ReadString(data, ref offset)).IsEqualTo("achievement");
        await Assert.That(ReadUInt16(data, ref offset)).IsEqualTo((ushort)1);
        await Assert.That(ReadString(data, ref offset)).IsEqualTo("first_steps");

        await Assert.That(offset).IsEqualTo(data.Length);
    }

    [Test]
    public async Task Serialize_DropsEmptyGroupsInsteadOfWritingAPlaceholder()
    {
        var empty = new WorldContentGroup { CategoryId = 3, CategoryName = "game_schedule" };
        var craft = new WorldContentGroup { CategoryId = 0, CategoryName = "craft" };
        craft.Names.Add("Open_1");

        var data = WorldContentFilterPack.Serialize([empty, craft]);
        var offset = 0;

        await Assert.That(ReadUInt16(data, ref offset)).IsEqualTo((ushort)1);
        await Assert.That(ReadString(data, ref offset)).IsEqualTo("craft");
    }

    [Test]
    public async Task Serialize_RefusesNamesTheWireCannotCarry()
    {
        var craft = new WorldContentGroup { CategoryId = 0, CategoryName = "craft" };
        craft.Names.Add("Open_1");
        craft.Names.Add(new string('x', 300)); // longer than the single length byte
        craft.Names.Add("");

        var data = WorldContentFilterPack.Serialize([craft]);
        var offset = 0;

        await Assert.That(ReadUInt16(data, ref offset)).IsEqualTo((ushort)1);
        await Assert.That(ReadString(data, ref offset)).IsEqualTo("craft");
        await Assert.That(ReadUInt16(data, ref offset)).IsEqualTo((ushort)1); // only the name that fits
        await Assert.That(ReadString(data, ref offset)).IsEqualTo("Open_1");
        await Assert.That(offset).IsEqualTo(data.Length);
    }

    [Test]
    public async Task CategoryNameOf_ReturnsTheClientsSpelling()
    {
        await Assert.That(WorldContentFilterPack.CategoryNameOf(0)).IsEqualTo("craft");
        await Assert.That(WorldContentFilterPack.CategoryNameOf(7)).IsEqualTo("quest");
        await Assert.That(WorldContentFilterPack.CategoryNameOf(17)).IsEqualTo("festival_zone");
        await Assert.That(WorldContentFilterPack.CategoryNameOf(200)).IsNull();
    }

    [Test]
    public async Task Serialize_WithNothingConfiguredWritesAnEmptyCount()
    {
        var data = WorldContentFilterPack.Serialize([]);

        await Assert.That(data.Length).IsEqualTo(2);
        await Assert.That((ushort)(data[0] | (data[1] << 8))).IsEqualTo((ushort)0);
    }

    [Test]
    public async Task Deserialize_ReadsBackWhatSerializeWrote()
    {
        var craft = new WorldContentGroup { CategoryId = 0, CategoryName = "craft" };
        craft.Names.Add("Open_1");
        craft.Names.Add("epherium");
        var instance = new WorldContentGroup { CategoryId = 13, CategoryName = "instance" };
        instance.Names.Add("instance_kadum");

        var groups = WorldContentFilterPack.Deserialize(WorldContentFilterPack.Serialize([craft, instance]));

        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups[0].CategoryName).IsEqualTo("craft");
        await Assert.That(string.Join(",", groups[0].Names)).IsEqualTo("Open_1,epherium");
        await Assert.That(groups[1].CategoryName).IsEqualTo("instance");
        await Assert.That(string.Join(",", groups[1].Names)).IsEqualTo("instance_kadum");
    }

    /// <summary>
    /// An absent payload must leave the filter empty, never fall back to the world_contents catalog. A
    /// received name is content the client BLOCKS, so cataloguing the table would hide every group it
    /// names - which is exactly how the instance window lost nearly all of its entries.
    /// </summary>
    [Test]
    public async Task ResolveFilterBuffer_WithoutAPayloadFileBlocksNothing()
    {
        var missing = Path.Combine(AppContext.BaseDirectory, "no_such_world_content_filter.bin");
        await Assert.That(File.Exists(missing)).IsFalse();

        var groups = WorldContentFilterPack.Deserialize(SCWorldContentPacket.ResolveFilterBuffer(missing));

        await Assert.That(groups.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ResolveFilterBuffer_UsesThePayloadFileWhenPresent()
    {
        var group = new WorldContentGroup { CategoryId = 13, CategoryName = "instance" };
        group.Names.Add("instance_kadum");
        var path = Path.Combine(Path.GetTempPath(), $"wc_filter_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, WorldContentFilterPack.Serialize([group]));

        try
        {
            var groups = WorldContentFilterPack.Deserialize(SCWorldContentPacket.ResolveFilterBuffer(path));

            await Assert.That(groups.Count).IsEqualTo(1);
            await Assert.That(groups[0].CategoryName).IsEqualTo("instance");
            await Assert.That(groups[0].Names.Contains("instance_kadum")).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
