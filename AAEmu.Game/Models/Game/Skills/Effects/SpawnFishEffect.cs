using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class SpawnFishEffect : EffectTemplate
{
    public uint Range { get; set; }
    public uint DoodadId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character player) return;

        var fishSpawnerId = GetFishSpawnerId(player);
        if (fishSpawnerId == 0)
        {
            Logger.Debug($"Fish Spawner ID not found for player {player.Name}.");
            return;
        }

        var template = NpcGameData.Instance.GetNpcSpawnerTemplate(fishSpawnerId);
        if (template?.Npcs == null || template.Npcs.Count == 0)
        {
            Logger.Warn($"No NPC templates available for fish spawner {fishSpawnerId}.");
            return;
        }

        // Weighted random fish selection
        NpcSpawnerNpc npcTemplateEntry = template.Npcs.Count == 1
            ? template.Npcs[0]
            : SelectWeightedRandom(template.Npcs);

        if (npcTemplateEntry == null)
        {
            Logger.Warn($"Failed to select random fish for spawner {fishSpawnerId}");
            return;
        }

        Logger.Debug($"Selected fish template {npcTemplateEntry.MemberId} from spawner {fishSpawnerId}");

        // Create temporary spawner with correct position
        var tempSpawner = new NpcSpawner
        {
            ParentWorld = player.ParentWorld,
            SpawnerId = fishSpawnerId,
            UnitId = npcTemplateEntry.MemberId,
            Template = template
        };

        // Fix for CloneAsSpawnPosition not existing / type mismatch
        using var spawnPos = target.Transform.Clone();
        tempSpawner.Position = spawnPos.CloneAsSpawnPosition();

        try
        {
            var spawnedList = npcTemplateEntry.Spawn(tempSpawner, player.Id);
            if (spawnedList == null || spawnedList.Count == 0)
            {
                Logger.Warn($"npcTemplate.Spawn returned no fish for template {npcTemplateEntry.MemberId}");
                return;
            }
            // Register so that Despawn() -> RemoveNpcFromSpawnedList doesn't log a false warning
            tempSpawner.SpawnedNpcs.TryAdd(fishSpawnerId, spawnedList);
            var fish = spawnedList.First();
            // Aggro & targeting
            fish.CurrentTarget = player;
            fish.AddUnitAggro(AggroKind.Damage, player, 10000);
            player.CurrentTarget = fish;
            player.BroadcastPacket(new SCTargetChangedPacket(player.ObjId, fish.ObjId), true);
            Logger.Debug($"Successfully spawned fish {npcTemplateEntry.MemberId} (owner {player.Id}) at bobber for {player.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error spawning fish from template {npcTemplateEntry.MemberId}");
        }
    }

    private NpcSpawnerNpc SelectWeightedRandom(List<NpcSpawnerNpc> npcs)
    {
        var totalWeight = npcs.Sum(x => x.Weight);
        if (totalWeight <= 0) return npcs[0];

        var roll = Random.Shared.NextDouble() * totalWeight;
        var current = 0.0;

        foreach (var entry in npcs)
        {
            current += entry.Weight;
            if (roll < current)
                return entry;
        }
        return npcs[0];
    }

    private uint GetFishSpawnerId(Character player)
    {
        var doodads = WorldManager.GetAround<Doodad>(player, 100);
        foreach (var doodad in doodads)
        {
            if (doodad.Template.GroupId == 65)
            {
                foreach (var func in doodad.CurrentPhaseFuncs)
                {
                    var funcTemplate = DoodadManager.Instance.GetPhaseFuncTemplate(func.FuncId, func.FuncType);
                    if (funcTemplate is DoodadFuncFishSchool schoolFunc)
                        return schoolFunc.NpcSpawnerId;
                }
            }
        }
        return 0;
    }
}
