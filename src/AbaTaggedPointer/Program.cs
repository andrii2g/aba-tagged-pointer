using AbaTaggedPointer;

if (args.Length > 0 && args[0] == "verify")
{
    Verification.Run();
    return;
}
var simulation = Simulation.Demo();
foreach (var entry in simulation.Trace)
    Console.WriteLine($"{entry.Step,2} W{entry.Worker} {entry.Action,-10} {entry.Observation}; stack: {entry.After}");
Console.WriteLine(string.Join(Environment.NewLine, simulation.Check()));
