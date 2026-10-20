using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class KillNpcWithoutCorpseEffect : EffectTemplate
{
    public uint NpcId { get; set; }
    public float Radius { get; set; }
    public bool GiveExp { get; set; }
    public bool Vanish { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Info($"KillNpcWithoutCorpseEffect npc={NpcId}, Radius={Radius}, Vanish={Vanish}");

        if (caster is Character) { return; } // does not apply to the character
        if (Vanish)
        {
            // Fixed: "Trainer Daru" disappears after selling a bear
            // quest 3449, buff=4112
            RemoveEffectsAndDelete((Unit)caster);
        }
        else
        {
            var npcs = WorldManager.GetAround<Npc>(target, Radius);
            if (caster is Npc thisNpc)
                npcs.Add(thisNpc);
            if (npcs == null) { return; }
            foreach (var npc in npcs.Where(npc => npc.TemplateId == NpcId))
            {
                RemoveEffectsAndDelete(npc);
            }
        }
    }

    private void RemoveEffectsAndDelete(Unit unit)
    {
        if (!Vanish && unit is Npc { Encounter: { } encounter } encounterNpc)
        {
            encounter.Retreat(encounterNpc);
            return;
        }

        if (!Vanish && unit is Npc defeatedNpc && defeatedNpc.CharacterTagging.Tagger != null)
        {
            // A scripted defeat can remove an NPC before DoDie awards quest credit.
            CreditDefeat(defeatedNpc, QuestManager.Instance, TeamManager.Instance,
                AppConfiguration.Instance.World.TagShareEnabled);
            defeatedNpc.CharacterTagging.ClearAllTaggers();
        }

        unit.Buffs.RemoveAllEffects();
        if (unit is Npc npc && npc.Spawner != null)
        {
            npc.DoDespawn(npc);
            npc.Spawner.DespawnWithRespawn(npc);
        }
    }

    internal static void CreditDefeat(Npc npc, IQuestManager questManager, ITeamManager teamManager, bool tagShareEnabled)
    {
        var players = new HashSet<Character>();
        var tagger = npc.CharacterTagging.Tagger;
        if (tagger != null)
        {
            players.Add(tagger);
            // Retreats at 50% health can happen before a party earns tagging rights.
            var team = teamManager.GetTeamByObjId(tagger.ObjId);
            if (team != null)
            {
                foreach (var member in team.Members)
                {
                    if (member?.Character != null &&
                        member.Character.GetDistanceTo(npc, true) <= LootingContainer.MaxLootingRange)
                    {
                        players.Add(member.Character);
                    }
                }
            }
        }

        if (tagShareEnabled)
        {
            players.UnionWith(npc.CharacterTagging.GetAllContributors(LootingContainer.MaxLootingRange));
        }

        foreach (var player in players)
        {
            questManager.DoOnMonsterHuntEvents(player, npc);
        }
    }
}
