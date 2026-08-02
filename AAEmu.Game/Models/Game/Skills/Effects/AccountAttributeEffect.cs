using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class AccountAttributeEffect : EffectTemplate
{
    public uint KindId { get; set; }
    public bool BindWorld { get; set; }
    public bool IsAdd { get; set; }
    public uint Count { get; set; }
    public uint Time { get; set; }
    public uint KindValue { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Char.Character character)
            return;

        if (KindId > byte.MaxValue || !Enum.IsDefined((AccountAttributeKind)(byte)KindId))
        {
            Logger.Error("AccountAttributeEffect {0} has unsupported kind {1}", Id, KindId);
            return;
        }

        if (Count > int.MaxValue || Time > int.MaxValue)
        {
            Logger.Error("AccountAttributeEffect {0} exceeds the server count/time range", Id);
            return;
        }

        var kind = (AccountAttributeKind)KindId;

        Logger.Debug($"AccountAttributeEffect: kind {KindId} isAdd {IsAdd} count {Count} time {Time} bindWorld {BindWorld} for {character.Name}");

        // bind_world confines the entry to this shard; otherwise it follows the account everywhere.
        var worldId = BindWorld ? (uint)AppConfiguration.Instance.Id : 0u;

        var result = Core.Managers.AccountAttributeManager.Instance.Change(
            character.AccountId, KindId, KindValue, worldId, IsAdd, checked((int)Count), checked((int)Time));

        if (result == null)
        {
            character.SendPacket(new Core.Packets.G2C.SCAccountAttributeRemovedPacket(kind, KindValue));
            return;
        }

        character.SendPacket(new Core.Packets.G2C.SCAccountAttributeUpdatedPacket(
            kind, KindValue, checked((byte)worldId), checked((uint)result.Count), result.Starts, result.Expires));
    }
}
