#nullable enable
using Shoots.Runtime.Abstractions;

namespace Shoots.Builder.Core;

/// <summary>
/// Universal, metadata-only runtime helper.
/// Produces deterministic help output derived strictly from runtime state.
/// No user intent. No interpretation. No AI.
/// </summary>
public sealed class DeterministicRuntimeHelper : IRuntimeHelper
{
    public RuntimeResult Help(RuntimeRequest request)
    {
        if (request is null)
            return new RuntimeResult { Ok = false, Error = new RuntimeError("internal_error", "Null request") };

        var services = request.Context.Services;
        if (services is null)
            return new RuntimeResult { Ok = false, Error = new RuntimeError("internal_error", "Runtime services unavailable") };

        // If a command id was provided, describe that command.
        if (!string.IsNullOrWhiteSpace(request.CommandId))
        {
            var spec = services.GetCommand(request.CommandId);
            if (spec is not null)
                return new RuntimeResult { Ok = true, Output = new RuntimeValue { Kind = RuntimeValueKind.String, String = DescribeCommand(spec).ToString() } };
        }

        // Otherwise, list all known commands.
        var all = services.GetAllCommands()
                          .OrderBy(c => c.CommandId, StringComparer.OrdinalIgnoreCase)
                          .ToArray();

        return new RuntimeResult { Ok = true, Output = new RuntimeValue { Kind = RuntimeValueKind.String, String = string.Join(", ", all.Select(c => c.CommandId)) } };
    }

    private static object DescribeCommand(RuntimeCommandSpec spec)
    {
        return new
        {
            commandId = spec.CommandId,
            description = spec.Description,
            args = spec.Args.Select(a => new
            {
                name = a.Name,
                type = a.Type,
                required = a.Required,
                description = a.Description
            }).ToArray(),
            usage = BuildUsage(spec),
            examples = BuildExamples(spec)
        };
    }

    private static string BuildUsage(RuntimeCommandSpec spec)
    {
        var parts = new List<string> { spec.CommandId };

        foreach (var arg in spec.Args)
        {
            var token = $"{arg.Name}=<{arg.Type}>";
            parts.Add(arg.Required ? token : $"[{token}]");
        }

        return string.Join(" ", parts);
    }

    private static IReadOnlyList<string> BuildExamples(RuntimeCommandSpec spec)
    {
        if (spec.Args.Count == 0)
            return new[] { spec.CommandId };

        var example = spec.CommandId + " " +
                      string.Join(" ",
                          spec.Args.Select(a =>
                              $"{a.Name}={ExampleValue(a.Type)}"));

        return new[] { example };
    }

    private static string ExampleValue(string type) =>
        type switch
        {
            "string" => "value",
            "int" => "123",
            "path" => "/path/to/file",
            "json" => "{...}",
            _ => "value"
        };
}


