using System.Diagnostics;
using AbaTaggedPointer;

try
{
    string command = args.FirstOrDefault() ?? "demo";
    if (command is "help" or "--help" or "-h")
    {
        Console.WriteLine("""
            ABA tagged-pointer simulator (.NET 10)
            demo    [--strategy both|pointer|tagged] [--output artifacts]
            explore [--strategy both|pointer|tagged] [--max-steps 40] [--max-transitions 1000000] [--output artifacts]
            verify
            Node IDs: 0=A, 1=B, 2=C. Each event is one indivisible simulated step.
            """);
        return;
    }
    if (command == "verify")
    {
        if (args.Length != 1) throw new ArgumentException("verify takes no options.");
        Verification.Run();
        return;
    }
    if (command is not ("demo" or "explore")) throw new ArgumentException($"Unknown command: {command}");
    Dictionary<string, string> options = [];
    for (int i = 1; i < args.Length; i += 2)
    {
        string key = args[i];
        bool valid = key is "--strategy" or "--output" || (command == "explore" && key is "--max-steps" or "--max-transitions");
        if (!valid || i + 1 == args.Length || !options.TryAdd(key, args[i + 1])) throw new ArgumentException($"Invalid, missing, or duplicate option: {key}");
    }
    CasStrategy[] strategies = options.GetValueOrDefault("--strategy", "both") switch
    {
        "both" => [CasStrategy.PointerOnly, CasStrategy.Tagged],
        "pointer" => [CasStrategy.PointerOnly],
        "tagged" => [CasStrategy.Tagged],
        _ => throw new ArgumentException("Strategy must be both, pointer, or tagged.")
    };
    string output = Path.GetFullPath(options.GetValueOrDefault("--output", "artifacts"));
    if (command == "demo")
    {
        Directory.CreateDirectory(output);
        List<Simulation> simulations = [];
        List<object> reports = [];
        foreach (var strategy in strategies)
        {
            var watch = Stopwatch.StartNew();
            var simulation = Simulation.Demo(strategy);
            watch.Stop();
            simulations.Add(simulation);
            Console.WriteLine($"\n{strategy}");
            foreach (var entry in simulation.Trace)
                Console.WriteLine($"{entry.Step,2} W{entry.Worker} {entry.Action,-10} {entry.Observation}; stack: {entry.After}");
            Console.WriteLine($"CAS {simulation.Attempts}: {simulation.Successes} success, {simulation.Failures} failure; invalid={simulation.Invalid}; runtime={watch.Elapsed.TotalMilliseconds:F3} ms");
            foreach (var error in simulation.Check()) Console.WriteLine(error);
            reports.Add(new { Strategy = strategy.ToString(), simulation.Attempts, simulation.Successes, simulation.Failures,
                simulation.CompletedOperations, InvalidStates = simulation.Trace.Count(e => e.Violations.Length > 0),
                ReachableCount = simulation.Reachable().Count, RuntimeMilliseconds = watch.Elapsed.TotalMilliseconds,
                FinalState = simulation.Snapshot(), simulation.Trace });
            Reports.Timeline(Path.Combine(output, strategy == CasStrategy.PointerOnly ? "aba-pointer-only.svg" : "aba-tagged.svg"), simulation);
        }
        Reports.Json(Path.Combine(output, "trace.json"), reports);
        Reports.Timeline(Path.Combine(output, "aba-comparison.svg"), simulations.ToArray());
    }
    else
    {
        int maxSteps = int.Parse(options.GetValueOrDefault("--max-steps", "40"));
        long maxTransitions = long.Parse(options.GetValueOrDefault("--max-transitions", "1000000"));
        var results = strategies.Select(s => Exploration.Run(s, maxSteps, maxTransitions)).ToArray();
        Directory.CreateDirectory(output);
        Reports.Json(Path.Combine(output, "exploration-summary.json"), results);
        foreach (var result in results)
            Console.WriteLine($"{result.Strategy}: completed={result.CompletedSchedules}, invalid={result.InvalidSchedules}, depth-limited={result.DepthLimitedPrefixes}, unexplored branches={result.UnexploredBranches}; transitions={result.Transitions}; CAS={result.CasAttempts}/{result.CasSuccesses}/{result.CasFailures}; runtime={result.RuntimeMilliseconds:F3} ms");
    }
    Console.WriteLine($"\nReports: {output}");
}
catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Error: {error.Message}");
    Environment.ExitCode = 2;
}
