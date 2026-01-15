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

    public BuildRunResult Run(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("text is required", nameof(text));

        // Stage 1 plan: input text only, no interpretation
        var planText = $"INPUT:\n{text.Trim()}\n";
        var hash = Sha256Hex(planText);

        // Deterministic artifact root
        var root = Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            hash
        );

        Directory.CreateDirectory(root);

        File.WriteAllText(
            Path.Combine(root, "plan.txt"),
            planText,
            Encoding.UTF8
        );

        // Load external runtime modules (optional, safe if empty)
        var loader = new DefaultRuntimeLoader();
        var modules = Directory.Exists(_modulesDir)
            ? loader.LoadFromDirectory(_modulesDir)
            : Array.Empty<IRuntimeModule>();

        // Runtime owns execution
        var engine = new RuntimeEngine(modules);

        // Explicit runtime context — no invented fields
        var context = new RuntimeContext(
            SessionId: hash,
            CorrelationId: Guid.NewGuid().ToString("n"),
            Env: new Dictionary<string, string>(),
            Services: engine
        );

        // Stage 1 proof: invoke a core command
        var request = new RuntimeRequest(
            CommandId: "core.ping",
            Args: new Dictionary<string, object?>(),
            Context: context
        );

        var result = engine.Execute(request);

        // Builder observes, never interprets runtime internals
        var resultPayload = new
        {
            ok = result.Ok,
            error = result.Error?.ToString(),
            output = result.Output?.ToString()
        };

        File.WriteAllText(
            Path.Combine(root, "result.json"),
            JsonSerializer.Serialize(
                resultPayload,
                new JsonSerializerOptions { WriteIndented = true }
            ),
            Encoding.UTF8
        );

        return new BuildRunResult(
            Hash: hash,
            Folder: root,
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
