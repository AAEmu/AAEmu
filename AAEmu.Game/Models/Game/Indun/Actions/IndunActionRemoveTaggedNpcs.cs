using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Actions
{
    internal class IndunActionRemoveTaggedNpcs : IndunAction
    {
        public uint TagId { get; set; }

        public override void Execute(InstanceWorld world)
        {
            foreach (var npc in GetTaggedNpcs(world))
            {
                npc.Delete();
            }

            Logger.Warn($"IndunActionRemoveTaggedNpcs: {TagId}");
        }

        private List<Npc> GetTaggedNpcs(InstanceWorld world)
        {
            var npcList = new List<Npc>();

            foreach (var region in world.Regions)
            {
                region.GetList(npcList, 0);
            }

            var taggedNpcIds = TagsGameData.Instance.GetIdsByTagId(TagsGameData.TagType.Npcs, TagId);
            return npcList.Where(npc => taggedNpcIds.Contains(npc.TemplateId)).ToList();
        }
    }
}
