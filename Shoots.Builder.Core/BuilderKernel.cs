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

	private static RunState Classify(RuntimeResult result)
	{
		if (result.Ok)
			return RunState.Success;

		if (result.Error is null)
			return RunState.Invalid;

		return result.Error.Code switch
		{
			"missing_file" => RunState.Blocked,
			"permission_denied" => RunState.Blocked,
			"tool_not_found" => RunState.Blocked,
			_ => RunState.Invalid
		};
	}

    public BuildRunResult Run(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("input is required", nameof(input));

        // --- Command resolution ---
        var commandId = input.Trim();

        // --- Deterministic plan + hash ---
        var planText = $"COMMAND:\n{commandId}\n";
        var hash = Sha256Hex(planText);

        // --- Artifact root (method-scoped, authoritative) ---
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

        // --- Load runtime modules ---
        var loader = new DefaultRuntimeLoader();

        IReadOnlyList<IRuntimeModule> modules =
            Directory.Exists(_modulesDir)
                ? loader.LoadFromDirectory(_modulesDir)
                : Array.Empty<IRuntimeModule>();

        Console.WriteLine($"[builder] modulesDir = {_modulesDir}");
        Console.WriteLine($"[builder] loaded modules = {modules.Count}");

        foreach (var module in modules)
        {
            Console.WriteLine($"[builder] module: {module.ModuleId} v{module.ModuleVersion}");
            foreach (var cmd in module.Describe())
                Console.WriteLine($"[builder]   command: {cmd.CommandId}");
        }

        // --- Builder-owned narrator + helper (runtime escape hatch) ---
        var narrator = new TextRuntimeNarrator(Console.WriteLine);
        var helper = new DeterministicRuntimeHelper();

        // --- Runtime is authoritative ---
        var engine = new RuntimeEngine(
            modules,
            narrator,
            helper
        );

        // --- Runtime context ---
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

        // --- Execute ---
        var result = engine.Execute(request);

        // --- Persist result ---
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

        // --- Law #2: final run state ---
        var state = Classify(result);

        return new BuildRunResult(
            hash,
            artifactsRoot,
            state,
            result.Error?.Code
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
    RunState State,
    string? Reason = null
);
