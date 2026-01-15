#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shoots.Runtime.Abstractions;
using Shoots.Runtime.Core;
using Shoots.Runtime.Loader;

namespace Shoots.Builder.Core;

public sealed class BuilderKernel
{
    private readonly string _modulesDir;

    public BuilderKernel(string modulesDir)
    {
        _modulesDir = modulesDir ?? throw new ArgumentNullException(nameof(modulesDir));
    }

    public BuildRunResult Run(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("input is required", nameof(input));

        // Stage 1: direct command execution (no planning yet)
        var commandId = input.Trim();

        // Deterministic plan text + hash
        var planText = $"COMMAND:\n{commandId}\n";
        var hash = Sha256Hex(planText);

        var artifactsRoot = Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            hash
        );

        Directory.CreateDirectory(artifactsRoot);

        File.WriteAllText(
            Path.Combine(artifactsRoot, "plan.txt"),
            planText,
            Encoding.UTF8
        );

        // Load runtime modules
        var loader = new DefaultRuntimeLoader();

        IReadOnlyList<IRuntimeModule> modules =
            Directory.Exists(_modulesDir)
                ? loader.LoadFromDirectory(_modulesDir)
                : Array.Empty<IRuntimeModule>();

        Console.WriteLine($"[builder] modulesDir = {_modulesDir}");
        Console.WriteLine($"[builder] loaded modules = {modules.Count}");

        foreach (var m in modules)
        {
            Console.WriteLine($"[builder] module: {m.ModuleId} v{m.ModuleVersion}");
            foreach (var c in m.Describe())
                Console.WriteLine($"[builder]   command: {c.CommandId}");
        }

        // Narrator escapes runtime here
        var narrator = new TextRuntimeNarrator(Console.WriteLine);

        // Structural default helper (runtime law)
        var helper = new DeterministicRuntimeHelper();

        // Runtime is authoritative
        var engine = new RuntimeEngine(
            modules,
            narrator,
            helper
        );

        // Runtime context
        var context = new RuntimeContext(
            SessionId: hash,
            CorrelationId: Guid.NewGuid().ToString("n"),
            Env: new Dictionary<string, string>(),
            Services: engine
        );

        var request = new RuntimeRequest(
            CommandId: commandId,
            Args: new Dictionary<string, object?>(),
            Context: context
        );

        var result = engine.Execute(request);

        var resultPayload = new
        {
            ok = result.Ok,
            error = result.Error?.ToString(),
            output = result.Output
        };

        File.WriteAllText(
            Path.Combine(artifactsRoot, "result.json"),
            JsonSerializer.Serialize(
                resultPayload,
                new JsonSerializerOptions { WriteIndented = true }
            ),
            Encoding.UTF8
        );

        return new BuildRunResult(
            Hash: hash,
            Folder: artifactsRoot,
            Ok: result.Ok
        );
    }

    private static string Sha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record BuildRunResult(
    string Hash,
    string Folder,
    bool Ok
);
