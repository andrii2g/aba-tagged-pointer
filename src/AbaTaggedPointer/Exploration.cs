using System.Diagnostics;

namespace AbaTaggedPointer;

public sealed class ExplorationResult
{
    public string Strategy { get; init; } = "";
    public int MaxSteps { get; init; }
    public long MaxTransitions { get; init; }
    public long Transitions { get; set; }
    public long CompletedSchedules { get; set; }
    public long InvalidSchedules { get; set; }
    public long DepthLimitedPrefixes { get; set; }
    public long UnexploredBranches { get; set; }
    public long CasAttempts { get; set; }
    public long CasSuccesses { get; set; }
    public long CasFailures => CasAttempts - CasSuccesses;
    public bool ExhaustedBoundedTree => UnexploredBranches == 0;
    public bool AllExecutionsTerminated => ExhaustedBoundedTree && DepthLimitedPrefixes == 0;
    public SortedDictionary<string, long> TerminalReachability { get; set; } = [];
    public int[]? FirstInvalidSchedule { get; set; }
    public double RuntimeMilliseconds { get; set; }
}

public static class Exploration
{
    public static ExplorationResult Run(CasStrategy strategy, int maxSteps = 40, long maxTransitions = 1_000_000)
    {
        if (maxSteps is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxSteps), "Use a step bound from 1 to 100.");
        if (maxTransitions < 1) throw new ArgumentOutOfRangeException(nameof(maxTransitions));
        var watch = Stopwatch.StartNew();
        var result = new ExplorationResult { Strategy = strategy.ToString(), MaxSteps = maxSteps, MaxTransitions = maxTransitions };
        void Visit(Simulation state)
        {
            if (state.Invalid || state.Complete)
            {
                if (state.Invalid)
                {
                    result.InvalidSchedules++;
                    result.FirstInvalidSchedule ??= state.Trace.Select(e => e.Worker).ToArray();
                }
                else result.CompletedSchedules++;
                var key = state.Stack();
                result.TerminalReachability[key] = result.TerminalReachability.GetValueOrDefault(key) + 1;
                return;
            }
            if (state.Trace.Count == maxSteps) { result.DepthLimitedPrefixes++; return; }
            for (int worker = 0; worker < 2; worker++)
            {
                if (!state.Enabled(worker)) continue;
                if (result.Transitions >= maxTransitions) { result.UnexploredBranches++; continue; }
                var child = state.Copy();
                var entry = child.Step(worker);
                result.Transitions++;
                if (entry.CasSucceeded is bool success)
                {
                    result.CasAttempts++;
                    if (success) result.CasSuccesses++;
                }
                Visit(child);
            }
        }
        Visit(new Simulation { Strategy = strategy });
        result.RuntimeMilliseconds = watch.Elapsed.TotalMilliseconds;
        return result;
    }
}
