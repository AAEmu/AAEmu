using System;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class SpawnGimmickEffect : EffectTemplate
{
    public uint GimmickId { get; set; } // here we mean TemplateId
    public bool OffsetFromSource { get; set; }
    public uint OffsetCoordiateId { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float OffsetZ { get; set; }
    public float Scale { get; set; }
    public uint VelocityCoordiateId { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public uint AngVelCoordiateId { get; set; }
    public float AngVelX { get; set; }
    public float AngVelY { get; set; }
    public float AngVelZ { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        var npc = (Npc)caster;

        if (npc?.CurrentTarget == null)
            return;

        if (npc.Gimmick != null)
            return; // don't spawn another Gimmick until the current one disappears

        Logger.Info($"SpawnGimmickEffect GimmickId={GimmickId}, scale={Scale}");

        var spawner = new GimmickSpawner(this, caster);

        if (npc.Gimmick == null)
            return;

        if ((Character)npc.CurrentTarget is { } character)
        {
            npc.Gimmick.CurrentTarget = character;
            return;
        }

        foreach (var character2 in WorldManager.GetAround<Character>(npc))
        {
            npc.Gimmick.CurrentTarget = character2;
            break;
        }
    }
}
