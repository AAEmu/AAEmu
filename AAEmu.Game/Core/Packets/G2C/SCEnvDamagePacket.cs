using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Static;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// <c>u8 s, bc target, u32 amount</c>, then per source — <see cref="EnvSource.Gimmick"/> adds
/// <c>u32 gimmickId</c>, <see cref="EnvSource.Collision"/> adds
/// <c>long x, long y, f32 z, f32 impact, u8 part</c>. Omitting the collision tail makes the client
/// read past the body and desync the whole SC stream, so it must be written whenever s == 3.
/// </summary>
public class SCEnvDamagePacket : GamePacket
{
    private readonly EnvSource _source;
    private readonly uint _target;
    private readonly uint _amount;
    private readonly uint _gimmickId;
    private readonly long _collisionX;
    private readonly long _collisionY;
    private readonly float _collisionZ;
    private readonly float _collisionImpact;
    private readonly byte _collisionPart;

    public SCEnvDamagePacket(EnvSource source, uint target, uint amount, uint gimmickId = 0)
        : base(SCOffsets.SCEnvDamagePacket, 1)
    {
        _source = source;
        _target = target;
        _amount = amount;
        _gimmickId = gimmickId;
    }

    /// <summary>
    /// Collision floater. Position is the quantized world point the physics contact happened at
    /// (the client spawns the impact effect there), impact is the approach speed along the contact
    /// normal, and part is the hull face that was struck.
    /// </summary>
    public SCEnvDamagePacket(uint target, uint amount, long x, long y, float z, float impact, byte part)
        : base(SCOffsets.SCEnvDamagePacket, 1)
    {
        _source = EnvSource.Collision;
        _target = target;
        _amount = amount;
        _collisionX = x;
        _collisionY = y;
        _collisionZ = z;
        _collisionImpact = impact;
        _collisionPart = part;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_source);
        stream.WriteBc(_target);
        stream.Write(_amount);
        switch (_source)
        {
            case EnvSource.Gimmick:
                stream.Write(_gimmickId);
                break;
            case EnvSource.Collision:
                stream.Write(_collisionX);
                stream.Write(_collisionY);
                stream.Write(_collisionZ);
                stream.Write(_collisionImpact);
                stream.Write(_collisionPart);
                break;
        }

        return stream;
    }
}
