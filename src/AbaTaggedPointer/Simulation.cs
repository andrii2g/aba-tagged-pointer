using System.Numerics;

namespace AbaTaggedPointer;

public enum CasStrategy { PointerOnly, Tagged }

public sealed class Worker
{
    public int Operation { get; set; }
    public int Phase { get; set; }
    public BigInteger CachedVersion { get; set; }
    public int? CachedHead { get; set; }
    public int? CachedNext { get; set; }
    public int? ReinsertNode { get; set; }
    public HashSet<int> Owned { get; set; } = [];
    public Worker Copy() => new()
    {
        Operation = Operation, Phase = Phase, CachedVersion = CachedVersion, CachedHead = CachedHead,
        CachedNext = CachedNext, ReinsertNode = ReinsertNode, Owned = [.. Owned]
    };
}

public sealed record TraceEvent(int Step, int Worker, string Action, string Before,
    string After, string Observation, bool? CasSucceeded, string[] Violations);

public sealed class Simulation
{
    public CasStrategy Strategy { get; init; }
    public BigInteger Version { get; private set; }
    public int? Head { get; private set; } = 0;
    public Dictionary<int, int?> Next { get; private set; } = new() { [0] = 1, [1] = 2, [2] = null };
    public List<int> Reference { get; private set; } = [0, 1, 2];
    public Worker[] Workers { get; private set; } = [new(), new()];
    public List<TraceEvent> Trace { get; private set; } = [];
    public int Attempts { get; private set; }
    public int Successes { get; private set; }
    public int Failures => Attempts - Successes;
    public int CompletedOperations => Workers.Sum(w => w.Operation);
    public bool Complete => !Enabled(0) && !Enabled(1);
    public bool Invalid => Trace.LastOrDefault()?.Violations.Length > 0;

    public bool Enabled(int worker) => Workers[worker].Operation < (worker == 0 ? 1 : 3);
    public static string Name(int? node) => node is null ? "null" : ((char)('A' + node.Value)).ToString();

    public Simulation Copy() => new()
    {
        Strategy = Strategy, Version = Version, Head = Head, Next = new(Next), Reference = [.. Reference],
        Workers = Workers.Select(w => w.Copy()).ToArray(), Trace = [.. Trace],
        Attempts = Attempts, Successes = Successes
    };

    public List<int> Reachable()
    {
        List<int> result = [];
        HashSet<int> seen = [];
        for (int? node = Head; node is int id && seen.Add(id); node = Next.GetValueOrDefault(id))
        {
            result.Add(id);
            if (!Next.ContainsKey(id)) break;
        }
        return result;
    }

    public string Stack() => string.Join(" → ", Reachable().Select(id => Name(id)).Append("null"));

    public string[] Check()
    {
        List<string> errors = [];
        HashSet<int> seen = [];
        int? node = Head;
        while (node is int id)
        {
            if (!Next.TryGetValue(id, out var next)) { errors.Add("Unknown reachable node."); break; }
            if (!seen.Add(id)) { errors.Add("Cycle in reachable chain."); break; }
            node = next;
        }
        if (Next.Values.Any(n => n is int id && !Next.ContainsKey(id))) errors.Add("Unknown next pointer.");
        if (!Reachable().SequenceEqual(Reference)) errors.Add("Reachability/order differs from the reference stack.");
        foreach (var id in Next.Keys)
        {
            int owners = Workers.Count(w => w.Owned.Contains(id));
            if (seen.Contains(id) && owners != 0) errors.Add($"Node {Name(id)} is both reachable and worker-owned.");
            if (owners > 1) errors.Add($"Node {Name(id)} has multiple owners.");
            if (!seen.Contains(id) && owners == 0) errors.Add($"Node {Name(id)} has been lost.");
        }
        return errors.ToArray();
    }

    public TraceEvent Step(int worker)
    {
        if (worker is < 0 or > 1 || !Enabled(worker) || Invalid) throw new InvalidOperationException("Worker cannot advance.");
        var w = Workers[worker];
        bool push = worker == 1 && w.Operation == 2;
        string before = Stack();
        var previousVersion = Version;
        var previousHead = Head;
        var previousLinks = Next.ToArray();
        string action;
        string observation;
        bool? cas = null;
        if (w.Phase == 0)
        {
            w.CachedHead = Head;
            w.CachedVersion = Version;
            action = "ReadHead";
            observation = $"observed ({Name(Head)}, {Version})";
            if (!push && Head is null) w.Operation++;
            else w.Phase = 1;
        }
        else if (w.Phase == 1)
        {
            if (push)
            {
                if (w.ReinsertNode is not int reused || !w.Owned.Contains(reused)) throw new InvalidOperationException("Push requires ownership of the first removed node.");
                Next[reused] = w.CachedHead;
                action = "WriteNext";
                observation = $"{Name(w.ReinsertNode)}.Next = {Name(w.CachedHead)}";
            }
            else
            {
                w.CachedNext = Next[w.CachedHead!.Value];
                action = "ReadNext";
                observation = $"cached next = {Name(w.CachedNext)}";
            }
            w.Phase = 2;
        }
        else
        {
            Attempts++;
            cas = Head == w.CachedHead && (Strategy == CasStrategy.PointerOnly || Version == w.CachedVersion);
            action = push ? "Push CAS" : "Pop CAS";
            int? desired = push ? w.ReinsertNode : w.CachedNext;
            observation = $"expected ({Name(w.CachedHead)}, {w.CachedVersion}), actual ({Name(Head)}, {Version}), desired {Name(desired)}: {(cas.Value ? "success" : "failure")}";
            if (cas.Value)
            {
                Successes++;
                Version++;
                Head = desired;
                if (push)
                {
                    Reference.Insert(0, w.ReinsertNode!.Value);
                    w.Owned.Remove(w.ReinsertNode!.Value);
                }
                else
                {
                    Reference.RemoveAt(0);
                    if (worker == 1 && w.Operation == 0) w.ReinsertNode = w.CachedHead;
                    w.Owned.Add(w.CachedHead!.Value);
                }
                w.Operation++;
            }
            w.Phase = 0;
        }
        List<string> violations = [.. Check()];
        if (Version != previousVersion + (cas == true ? 1 : 0)) violations.Add("Incorrect head version transition.");
        if (cas == false && (Head != previousHead || !Next.SequenceEqual(previousLinks))) violations.Add("Failed CAS changed shared state.");
        var entry = new TraceEvent(Trace.Count + 1, worker + 1, action, before, Stack(), observation, cas, violations.ToArray());
        Trace.Add(entry);
        return entry;
    }

    public static Simulation Demo(CasStrategy strategy = CasStrategy.PointerOnly)
    {
        var state = new Simulation { Strategy = strategy };
        foreach (int worker in new[] { 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0 }) state.Step(worker);
        while (!state.Invalid && state.Enabled(0)) state.Step(0);
        return state;
    }
}

