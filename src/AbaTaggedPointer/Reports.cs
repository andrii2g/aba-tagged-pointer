using System.Text.Json;
using System.Xml.Linq;

namespace AbaTaggedPointer;

public static class Reports
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    public static void Json<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + "\n");

    public static void Timeline(string path, params Simulation[] simulations)
    {
        int height = 250 + simulations.Max(s => s.Trace.Count) * 90;
        int width = simulations.Length * 780;
        var root = new XElement(Svg + "svg", new XAttribute("viewBox", $"0 0 {width} {height}"),
            new XAttribute("width", width), new XAttribute("height", height),
            new XAttribute("role", "img"), new XAttribute("aria-labelledby", "title description"),
            new XElement(Svg + "title", new XAttribute("id", "title"), "ABA: pointer-only versus tagged CAS"),
            new XElement(Svg + "desc", new XAttribute("id", "description"), "Two worker timelines with stack snapshots after every atomic step. Pointer-only CAS resurrects an owned node; tagged CAS rejects the stale observation and retries."),
            new XElement(Svg + "rect", new XAttribute("width", "100%"), new XAttribute("height", "100%"), new XAttribute("fill", "#101827")));
        void Text(XElement parent, int x, int y, string value, int size = 14, string color = "#dce6f5") =>
            parent.Add(new XElement(Svg + "text", new XAttribute("x", x), new XAttribute("y", y),
                new XAttribute("font-family", "Consolas, monospace"), new XAttribute("font-size", size), new XAttribute("fill", color), value));
        for (int panel = 0; panel < simulations.Length; panel++)
        {
            var simulation = simulations[panel];
            var group = new XElement(Svg + "g", new XAttribute("transform", $"translate({panel * 780},0)"));
            root.Add(group);
            Text(group, 28, 40, simulation.Strategy == CasStrategy.PointerOnly ? "POINTER-ONLY CAS" : "TAGGED CAS", 24);
            Text(group, 28, 68, "Initial stack: A → B → C → null | head (A, 0)");
            Text(group, 50, 108, "Worker 1"); Text(group, 280, 108, "Worker 2"); Text(group, 510, 108, "Stack / head version");
            foreach (var x in new[] { 120, 350 })
                group.Add(new XElement(Svg + "line", new XAttribute("x1", x), new XAttribute("x2", x),
                    new XAttribute("y1", 120), new XAttribute("y2", height - 110), new XAttribute("stroke", "#34465f")));
            foreach (var entry in simulation.Trace)
            {
                int y = 135 + (entry.Step - 1) * 90;
                int x = entry.Worker == 1 ? 28 : 258;
                string color = entry.Violations.Length > 0 ? "#fb7185" : entry.CasSucceeded == false ? "#facc15" : "#65dbbc";
                group.Add(new XElement(Svg + "rect", new XAttribute("x", x), new XAttribute("y", y),
                    new XAttribute("width", 200), new XAttribute("height", 28), new XAttribute("rx", 5), new XAttribute("fill", "#203149")));
                Text(group, x + 10, y + 19, $"{entry.Step:00}  {entry.Action}", 14, color);
                Text(group, 510, y + 19, entry.After, 13);
                Text(group, 510, y + 38, $"head ({Simulation.Name(entry.AfterState.Head)}, {entry.AfterState.Version})", 12);
                Text(group, 28, y + 56, entry.Observation, 11, color);
                string owned = string.Join("; ", entry.AfterState.Workers.Select((w, i) => $"W{i + 1} owns [{string.Join(",", w.Owned.Select(n => Simulation.Name(n)))}]"));
                Text(group, 28, y + 74, owned, 11, "#a4b4cc");
            }
            Text(group, 28, height - 70, simulation.Invalid ? "INVALID: B is reachable and owned by Worker 2." : "VALID: stale CAS rejected; retry removes A.", 15,
                simulation.Invalid ? "#fb7185" : "#65dbbc");
            Text(group, 28, height - 40, $"CAS: {simulation.Attempts} attempts / {simulation.Successes} success / {simulation.Failures} failure", 13);
        }
        new XDocument(root).Save(path);
    }
}
