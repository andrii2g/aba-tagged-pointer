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
        Console.WriteLine("PASS: initial state, canonical ABA, ownership violation, sequential execution.");
    }
}
