namespace AbaTaggedPointer;

public static class Verification
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        var initial = new Simulation();
        Require(initial.Check().Length == 0, "Initial stack must be valid.");
        var demo = Simulation.Demo();
        Require(demo.Trace.Take(11).All(e => e.Violations.Length == 0), "Failure must occur only at stale CAS.");
        Require(demo.Attempts == 4 && demo.Successes == 4, "All four pointer-only CAS attempts must succeed.");
        Require(demo.Reachable().SequenceEqual(new[] { 1, 2 }), "Stale CAS must resurrect B.");
        Require(demo.Reference.SequenceEqual(new[] { 2 }), "Reference must contain only C.");
        Require(demo.Check().Any(e => e.Contains("both reachable")), "Ownership check must detect resurrection.");
        var sequential = new Simulation();
        while (sequential.Enabled(1)) sequential.Step(1);
        while (sequential.Enabled(0)) sequential.Step(0);
        Require(!sequential.Invalid && sequential.Complete, "Sequential execution must be valid.");
        var tagged = Simulation.Demo(CasStrategy.Tagged);
        Require(!tagged.Invalid && tagged.Complete, "Tagged retry must complete correctly.");
        Require(tagged.Trace[11].CasSucceeded == false, "Tagged stale CAS must fail.");
        Require(tagged.Attempts == 5 && tagged.Successes == 4 && tagged.Failures == 1, "Tagged CAS metrics must include retry.");
        Require(tagged.Version == 4 && tagged.Reachable().SequenceEqual(new[] { 2 }), "Tagged result must contain only C at version 4.");
        Require(tagged.Trace[0].AfterState.Nodes[0].Next == 1 && tagged.Trace[9].AfterState.Nodes[0].Next == 2,
            "Historical snapshots must preserve the changed A.Next link.");
        Require(tagged.Trace[11].BeforeState.Version == tagged.Trace[11].AfterState.Version &&
            tagged.Trace[11].BeforeState.Head == tagged.Trace[11].AfterState.Head, "Failed CAS snapshot must preserve the head pair.");
        var copy = initial.Copy();
        copy.Next[0] = null;
        copy.Workers[0].Owned.Add(2);
        Require(initial.Next[0] == 1 && initial.Workers[0].Owned.Count == 0, "Search branches must be isolated.");
        var cycle = new Simulation();
        cycle.Next[2] = 0;
        Require(cycle.Check().Any(e => e.Contains("Cycle")), "Cycle must be detected.");
        var unknown = new Simulation();
        unknown.Next[0] = 99;
        Require(unknown.Check().Any(e => e.Contains("Unknown")), "Unknown node must be detected.");
        foreach (var strategy in Enum.GetValues<CasStrategy>())
        {
            var explored = Exploration.Run(strategy);
            Require(explored.AllExecutionsTerminated, "Default search must exhaust the complete execution tree.");
            Require(explored.CompletedSchedules > 0, "Search must find completed schedules.");
            Require(strategy == CasStrategy.PointerOnly ? explored.InvalidSchedules > 0 : explored.InvalidSchedules == 0,
                "Only pointer-only exploration must expose invalid schedules.");
            // Independently enumerate fixed worker-choice words. Stopped prefixes count once,
            // by accepting only their all-zero padded representative.
            const int bound = 12;
            long complete = 0, invalid = 0, limited = 0;
            for (int word = 0; word < (1 << bound); word++)
            {
                var replay = new Simulation { Strategy = strategy };
                int step = 0;
                bool admissible = true;
                for (; step < bound && !replay.Complete && !replay.Invalid; step++)
                {
                    int worker = (word >> step) & 1;
                    if (!replay.Enabled(worker)) { admissible = false; break; }
                    replay.Step(worker);
                }
                if (!admissible || (word >> step) != 0) continue;
                if (replay.Invalid) invalid++;
                else if (replay.Complete) complete++;
                else limited++;
            }
            var bounded = Exploration.Run(strategy, bound);
            Require(bounded.CompletedSchedules == complete && bounded.InvalidSchedules == invalid && bounded.DepthLimitedPrefixes == limited,
                "DFS counts must match independent fixed-word enumeration.");
            var budgeted = Exploration.Run(strategy, maxTransitions: 1);
            Require(!budgeted.ExhaustedBoundedTree && budgeted.Transitions == 1, "Budget must report unexplored branches.");
            var shallow = Exploration.Run(strategy, maxSteps: 1);
            Require(shallow.ExhaustedBoundedTree && !shallow.AllExecutionsTerminated && shallow.DepthLimitedPrefixes == 2,
                "Exhausting a shallow bound must not imply completed executions.");
            Console.WriteLine($"PASS: {strategy}: {explored.CompletedSchedules} complete, {explored.InvalidSchedules} invalid, {explored.Transitions} transitions; independent enumeration agrees.");
        }
        Console.WriteLine("PASS: initial state, canonical ABA, ownership violation, sequential execution, tagged rejection and retry.");
    }
}
