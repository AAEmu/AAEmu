using System.Numerics;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Gimmicks;

/// <summary>
/// GimmickSpawnData shared by SCGimmicksCreated, ZWRequestSpawnStaticGimmick,
/// and WZGimmickCreated. WZGimmickCreated appends ownerZoneId after this record.
/// </summary>
public sealed record GimmickSpawnData(
    uint Id,
    uint Type,
    ulong EntityGuid,
    uint Type2,
    uint SpawnerUnitId,
    uint GrasperUnitId,
    uint StaticZoneId,
    string ModelPath,
    long X,
    long Y,
    float Z,
    Quaternion Rotation,
    float Scale,
    Vector3 Velocity,
    Vector3 AngularVelocity,
    float ScaleVelocity)
{
    public const int MinimumSerializedLength = 102;

    private const int BytesAfterModelPath = 68;

    public PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write(Type);
        stream.Write(EntityGuid);
        stream.Write(Type2);
        stream.Write(SpawnerUnitId);
        stream.Write(GrasperUnitId);
        stream.Write(StaticZoneId);
        stream.Write(ModelPath ?? string.Empty);
        stream.Write(X);
        stream.Write(Y);
        stream.Write(Z);
        stream.Write(Rotation.X);
        stream.Write(Rotation.Y);
        stream.Write(Rotation.Z);
        stream.Write(Rotation.W);
        stream.Write(Scale);
        stream.Write(Velocity.X);
        stream.Write(Velocity.Y);
        stream.Write(Velocity.Z);
        stream.Write(AngularVelocity.X);
        stream.Write(AngularVelocity.Y);
        stream.Write(AngularVelocity.Z);
        stream.Write(ScaleVelocity);
        return stream;
    }

    public static bool TryRead(PacketStream stream, out GimmickSpawnData data)
    {
        data = null;
        if (stream == null || stream.LeftBytes < MinimumSerializedLength)
            return false;

        var id = stream.ReadUInt32();
        var type = stream.ReadUInt32();
        var entityGuid = stream.ReadUInt64();
        var type2 = stream.ReadUInt32();
        var spawnerUnitId = stream.ReadUInt32();
        var grasperUnitId = stream.ReadUInt32();
        var staticZoneId = stream.ReadUInt32();

        var modelPathLength = stream.ReadUInt16();
        if (modelPathLength > stream.LeftBytes - BytesAfterModelPath)
            return false;
        var modelPath = stream.ReadString(modelPathLength);

        var x = stream.ReadInt64();
        var y = stream.ReadInt64();
        var z = stream.ReadSingle();
        var rotation = new Quaternion(
            stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());
        var scale = stream.ReadSingle();
        var velocity = new Vector3(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());
        var angularVelocity = new Vector3(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());
        var scaleVelocity = stream.ReadSingle();

        data = new GimmickSpawnData(
            id, type, entityGuid, type2, spawnerUnitId, grasperUnitId, staticZoneId,
            modelPath, x, y, z, rotation, scale, velocity, angularVelocity, scaleVelocity);
        return true;
    }
}
