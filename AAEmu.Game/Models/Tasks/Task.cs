// Authors: Nikes, AAGene, ZeromusXYZ
using System;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;
using NCrontab;

namespace AAEmu.Game.Models.Tasks;

public abstract class Task
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public bool Cancelled { get; set; }
    public int ExecuteCount { get; set; }
    
    public DateTime TriggerTime { get; set; }
    public TimeSpan RepeatInterval { get; set; }
    public int RepeatCount { get; set; }
    public CrontabSchedule CronSchedule { get; set; }

    protected Task()
    {
        Name = GetType().Name;
        Cancelled = false;
    }

    public abstract void Execute();

    public async Task<bool> Cancel()
    {
        var result = TaskManager.Instance.Cancel(this);
        if (result)
        {
            OnCancel();
            return true;
        }

        return false;
    }
    
    public virtual void OnCancel()
    {
        //
    }
}
