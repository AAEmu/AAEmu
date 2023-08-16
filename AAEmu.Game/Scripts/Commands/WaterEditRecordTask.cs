using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Scripts.Commands;

namespace AAEmu.Game.Scripts.Commands;

public class WaterEditRecordTask : Task
{
    private Character _character;
    public List<Vector3> DataPoints { get; set; }
    public object Lock = new object();
    public bool Recording { get; set; }
    public int RecordInterval { get; set; } = 1000;

    public WaterEditRecordTask(Character character)
    {
        _character = character;
        DataPoints = new List<Vector3>();
        Recording = true;
        WaterEditCmd.RecordingCharacter = character;
    }

    public WaterEditRecordTask()
    {
        // Needed for dynamic compiler
        DataPoints = new List<Vector3>();
        Recording = false;
    }

    public override void Execute()
    {
        while (Recording)
        {
            lock (Lock)
            {
                if ((_character == null) || (!_character.IsOnline))
                {
                    Recording = false;
                    continue;
                }

                var charPos = _character.Transform.World.Position;

                if (DataPoints.Count > 1)
                {
                    var lastDistance = (charPos - DataPoints[^1]).Length();
                    // Auto stop recording if we are no longer moving
                    if (lastDistance <= 0.01f)
                        Recording = false;
                }
                DataPoints.Add(charPos);
            }
            Thread.Sleep(RecordInterval);
        }
        Recording = false;
        lock (Lock)
        {
            WaterEditCmd.RecordedData.Clear();
            WaterEditCmd.RecordedData.AddRange(DataPoints);
            WaterEditCmd.RecordingTask = null;
        }
        WaterEditCmd.OnRecodingEnded();
    }

    public bool IsRecording()
    {
        lock (Lock)
        {
            return Recording;
        }
    }
    
    public void StopRecording()
    {
        lock (Lock)
        {
            Recording = false;
        }
    }
}
