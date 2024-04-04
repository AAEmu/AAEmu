﻿using System;
using System.Collections.Generic;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.World;

public enum AreaSphereTriggerCondition
{
    Invalid = 0,
    TriggerOnceAtAll = 1,
    TriggerOnceInRuntime = 2,
    TriggerEveryNTimeAfter = 3
}

public class SphereQuest
{
    public uint ZoneId { get; set; }
    public string WorldId { get; set; }
    public uint QuestId { get; set; }
    public uint ComponentId { get; set; }
    public float Radius { get; set; }
    public Vector3 Xyz { get; set; } = Vector3.Zero;

    public bool Contains(Vector3 pos)
    {
        return MathUtil.CalculateDistance(Xyz, pos, true) <= Radius;
    }
}

public class SphereQuestTrigger
{
    /// <summary>
    /// Sphere data to check against
    /// </summary>
    public SphereQuest Sphere { get; set; }

    /// <summary>
    /// Owner of this SphereQuestTrigger
    /// </summary>
    public ICharacter Owner { get; set; }

    /// <summary>
    /// Related Quest object
    /// </summary>
    public Quest Quest { get; set; }

    /// <summary>
    /// If set, the nearest NPC with this template Id is used as a center point for the check
    /// </summary>
    public uint NpcTemplate { get; set; }

    /// <summary>
    /// Last location of the Owner
    /// </summary>
    public Vector3 LastCheckLocation { get; set; } = Vector3.Zero;

    /// <summary>
    /// Minimum time needed between checks
    /// </summary>
    public int TickRate { get; set; }

    /// <summary>
    /// Last check time
    /// </summary>
    private DateTime LastTick { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Handle the tick for this SphereQuestTrigger
    /// </summary>
    /// <param name="delta"></param>
    public void Tick(TimeSpan delta)
    {
        if ((TickRate > 0) && (DateTime.UtcNow - LastTick).TotalMilliseconds > TickRate)
        {
            var oldInside = false;
            var newInside = false;
            if (NpcTemplate <= 0)
            {
                // Normal distance check
                oldInside = Sphere.Contains(LastCheckLocation);
                newInside = Sphere.Contains(Owner?.Transform?.World?.Position ?? Vector3.Zero);
            }
            else
            {
                // Using NPC Template, find nearby NPCs with it first
                var npcsNear = WorldManager.GetAround<Npc>((Character)Owner, Sphere.Radius * 1.5f, false);
                foreach (var npc in npcsNear)
                {
                    if (MathUtil.CalculateDistance(npc.Transform.World.Position, LastCheckLocation, true) <=
                        Sphere.Radius)
                        oldInside = true;
                    if (MathUtil.CalculateDistance(npc, (Character)Owner, true) <= Sphere.Radius)
                        newInside = true;
                }
            }

            if (!oldInside && newInside)
            {
                QuestManager.Instance.DoOnEnterSphereEvents(Owner, Sphere);
            }
            else if (oldInside && !newInside)
            {
                QuestManager.Instance.DoOnExitSphereEvents(Owner, Sphere);
            }

            LastCheckLocation = Owner?.Transform?.World?.Position ?? Vector3.Zero;
            LastTick = DateTime.UtcNow;
        }
    }
}
