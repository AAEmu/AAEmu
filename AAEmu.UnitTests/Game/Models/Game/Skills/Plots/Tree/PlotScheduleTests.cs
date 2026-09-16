using System.Diagnostics;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

public class PlotScheduleTests
{
    [Test]
    public async Task NodesDueAt0_10_50_RunInDueOrderWithinTolerance()
    {
        var schedule = new PlotSchedule<int>();
        var start = DateTime.UtcNow;
        // Enqueued out of due order on purpose: the old FIFO queue ran them in arrival order and pushed a
        // pending node to the back of the line on every 15 ms pass.
        schedule.Enqueue(0, start);
        schedule.Enqueue(50, start.AddMilliseconds(50));
        schedule.Enqueue(10, start.AddMilliseconds(10));

        var order = new List<int>();
        var ranAtMs = new Dictionary<int, double>();
        var guard = Stopwatch.StartNew();
        while (schedule.Count > 0 && guard.ElapsedMilliseconds < 5_000)
        {
            if (!schedule.TryDequeueDue(DateTime.UtcNow, out var item))
            {
                var wait = PlotSchedule<int>.WaitSliceMs(schedule.NextDueUtc, DateTime.UtcNow);
                if (wait > 0)
                    await Task.Delay(wait);
                continue;
            }

            order.Add(item);
            ranAtMs[item] = (DateTime.UtcNow - start).TotalMilliseconds;
        }

        await Assert.That(string.Join(",", order)).IsEqualTo("0,10,50");
        // Neither node ran before its due time. Deliberately only a lower bound: the full suite runs these
        // tests in parallel, so an upper bound on a 10 ms delay measures the machine, not the schedule.
        await Assert.That(ranAtMs[10]).IsGreaterThanOrEqualTo(5);
        await Assert.That(ranAtMs[50]).IsGreaterThanOrEqualTo(45);
    }

    [Test]
    public async Task NotDueYet_LeavesTheNodeInTheSchedule()
    {
        var schedule = new PlotSchedule<string>();
        schedule.Enqueue("later", DateTime.UtcNow.AddSeconds(30));

        await Assert.That(schedule.TryDequeueDue(DateTime.UtcNow, out _)).IsFalse();
        await Assert.That(schedule.Count).IsEqualTo(1);
    }

    [Test]
    public async Task EqualDueTimes_RunInEnqueueOrder()
    {
        var schedule = new PlotSchedule<int>();
        var due = DateTime.UtcNow.AddMilliseconds(-1);
        schedule.Enqueue(1, due);
        schedule.Enqueue(2, due);
        schedule.Enqueue(3, due);

        var order = new List<int>();
        while (schedule.TryDequeueDue(DateTime.UtcNow, out var item))
            order.Add(item);

        await Assert.That(string.Join(",", order)).IsEqualTo("1,2,3");
    }

    [Test]
    public async Task EarlierDueEnqueuedLast_RunsFirst()
    {
        var schedule = new PlotSchedule<string>();
        var now = DateTime.UtcNow;
        schedule.Enqueue("late", now.AddMilliseconds(50));
        schedule.Enqueue("early", now.AddMilliseconds(-1));

        await Assert.That(schedule.NextDueUtc).IsEqualTo(now.AddMilliseconds(-1));
        await Assert.That(schedule.TryDequeueDue(now, out var first)).IsTrue();
        await Assert.That(first).IsEqualTo("early");
    }

    [Test]
    public async Task DrainAll_EmptiesInDueOrderAndKeepsDueTimes()
    {
        var schedule = new PlotSchedule<string>();
        var now = DateTime.UtcNow;
        schedule.Enqueue("c", now.AddMilliseconds(30));
        schedule.Enqueue("a", now.AddMilliseconds(10));
        schedule.Enqueue("b", now.AddMilliseconds(20));

        var drained = schedule.DrainAll();

        await Assert.That(schedule.Count).IsEqualTo(0);
        await Assert.That(string.Join(",", drained.Select(entry => entry.Item))).IsEqualTo("a,b,c");
        await Assert.That(drained[1].DueUtc).IsEqualTo(now.AddMilliseconds(20));
    }

    [Test]
    public async Task WaitSlice_StopsAtTheDueTimeAndAtThePollInterval()
    {
        var now = DateTime.UtcNow;

        await Assert.That(PlotSchedule<int>.WaitSliceMs(null, now)).IsEqualTo(0);
        await Assert.That(PlotSchedule<int>.WaitSliceMs(now, now)).IsEqualTo(0);
        await Assert.That(PlotSchedule<int>.WaitSliceMs(now.AddMilliseconds(-40), now)).IsEqualTo(0);
        await Assert.That(PlotSchedule<int>.WaitSliceMs(now.AddMilliseconds(5), now)).IsEqualTo(5);
        await Assert.That(PlotSchedule<int>.WaitSliceMs(now.AddMilliseconds(4.2), now)).IsEqualTo(5);
        await Assert.That(PlotSchedule<int>.WaitSliceMs(now.AddSeconds(5), now)).IsEqualTo(PlotSchedule<int>.PollIntervalMs);
        await Assert.That(PlotSchedule<int>.WaitSliceMs(now.AddSeconds(5), now, 250)).IsEqualTo(250);
    }
}
