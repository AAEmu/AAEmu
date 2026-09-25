using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.CommonFarm;
using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The farm placement request (CS 0x164): the client names a farm tab, a count, and that many target
/// points. The server validates the whole batch against the farm area and the group's content capacity.
/// </summary>
/// <remarks>
/// <para>
/// The body is <c>u32 type</c>, <c>s32 count</c>, then — only when <c>count &gt; 0</c> — a loop of
/// <c>count</c> 12-byte <c>vec3</c> points. The count is signed, so a negative or zero count carries
/// no points at all. The whole loop must be consumed; reading a single point would leave the rest of
/// the request unread.
/// </para>
/// <para>
/// This request carries no doodad or item identity, so it cannot itself create a farm doodad. The
/// actual per-doodad creation is the already-wired <see cref="CSCreateDoodadPacket"/> path, which
/// validates the same farm area and capacity and then calls the doodad manager. What this handler
/// adds is the request-level answer: an invalid area, an unknown tab or a request past the farm's
/// capacity is refused with the farm's own error message instead of being silently dropped.
/// </para>
/// </remarks>
public class CSPlaceCommonFarmPacket() : GamePacket(CSOffsets.CSPlaceCommonFarmPacket, 1)
{
    /// <summary>
    /// Upper bound on points honoured from one request. It matches the wire's own clamp, so a
    /// well-behaved client never reaches it and a hostile count is bounded instead of looping.
    /// </summary>
    public const int MaxPointCount = 128;

    public uint TypeValue { get; private set; }
    public int SignedCount { get; private set; }
    public List<Vector3> Points { get; } = [];

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadUInt32();
        SignedCount = stream.ReadInt32();

        Points.Clear();
        if (SignedCount <= 0)
            return;

        var toRead = Math.Min(SignedCount, MaxPointCount);
        for (var index = 0; index < toRead; index++)
        {
            Points.Add(new Vector3(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle()));
        }

        if (SignedCount > MaxPointCount)
        {
            Logger.Warn("PlaceCommonFarm: count {0} exceeds the {1}-point bound; extra points ignored.",
                SignedCount, MaxPointCount);
        }
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
        {
            Logger.Warn("PlaceCommonFarm ignored: no active character.");
            return;
        }

        var manager = PublicFarmManager.Instance;
        var requestedType = (FarmType)TypeValue;

        // Every point in the batch must resolve to the farm tab the request names.
        FarmType areaType = requestedType;
        foreach (var point in Points)
        {
            areaType = manager.GetFarmType(character.ParentWorld, point);
            if (areaType != requestedType)
                break;
        }

        var maxCount = CommonFarmGameData.Instance.GetFarmGroupMaxCount(requestedType);
        var alreadyPlanted = manager.GetPlantedCount(character, requestedType);

        var failure = PublicFarmPlacementRules.ValidatePlacement(
            requestedType, areaType, maxCount, alreadyPlanted, (uint)Math.Max(SignedCount, 0));

        var error = PublicFarmPlacementRules.ToErrorMessage(failure);
        if (error is { } refusal)
        {
            Logger.Debug("PlaceCommonFarm refused ({0}) for {1}: type={2} count={3} points={4}.",
                failure, character.Name, TypeValue, SignedCount, Points.Count);
            character.SendErrorMessage(refusal);
        }
    }
}
