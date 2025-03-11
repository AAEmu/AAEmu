using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;

public class MoveToLocationEffect : EffectTemplate
{
    public bool OwnHouseOnly { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Debug("MoveToLocationEffect");
        if (caster is Character character /*&& skillObject is SkillObjectUnk2 so*/)
        {
            var xyz = character.Transform.World.Position;
            //character.Portals.AddPrivatePortal(
            //    xyz.X,
            //    xyz.Y,
            //    xyz.Z,
            //    character.Transform.World.Rotation.Z,
            //    character.Transform.ZoneId, so.Name);

            character.SendPacket(new SCUnitTeleportPacket(TeleportReason.MoveToLocation, ErrorMessageType.NoErrorMessage, xyz.X, xyz.Y, xyz.Z, character.Transform.World.Rotation.Z));

        }
    }
}
