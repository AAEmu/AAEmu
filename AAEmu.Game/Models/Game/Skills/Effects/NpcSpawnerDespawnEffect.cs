using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class NpcSpawnerDespawnEffect : EffectTemplate
{
    public uint SpawnerId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (WorldIntegration.ZoneAuthority)
        {
            if (!WorldIntegration.PublishNpcSpawnerEvent(caster, SpawnerId, NpcSpawnerEvent.DespawnAll))
                Logger.Warn($"NpcSpawnerDespawnEffect: no loaded Zone accepted spawner {SpawnerId}.");
            return;
        }

        // Counterpart to NpcSpawnerSpawnEffect, resolved the same way: the spawner id may map to
        // several spawners, and each clears the npcs it owns.
        var spawners = caster?.ParentWorld?.SpawnManager.GetNpcSpawner(SpawnerId);
        if (spawners is not { Count: not 0 })
        {
            Logger.Info($"NpcSpawnerDespawnEffect: SpawnerId={SpawnerId} not found in spawners.");
            return;
        }

        foreach (var spawner in spawners)
            spawner.DespawnNpcsNow();

        Logger.Debug("NpcSpawnerDespawnEffect: despawned {0} spawner(s) for SpawnerId={1}",
            spawners.Count, SpawnerId);
    }
}
