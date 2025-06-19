using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

internal class IndunActionRemoveTaggedNpcs : IndunAction
{
    public uint TagId { get; set; }

    public override void Execute(WorldInstance worldInstance)
    {
        foreach (var npc in GetTaggedNpcs(worldInstance))
        {
            npc.Delete();
        }

        Logger.Warn($"IndunActionRemoveTaggedNpcs: {TagId}");
    }

    private List<Npc> GetTaggedNpcs(WorldInstance worldInstance)
    {
        var npcList = new List<Npc>();

        foreach (var region in worldInstance.Regions)
        {
            region.GetList(npcList, 0);
        }

        var taggedNpcIds = TagsGameData.Instance.GetIdsByTagId(TagsGameData.TagType.Npcs, TagId);
        return npcList.Where(npc => taggedNpcIds.Contains(npc.TemplateId)).ToList();
    }
}
