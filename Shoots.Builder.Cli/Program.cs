using Shoots.Builder.Core;

var text = args.Length > 0 ? string.Join(" ", args) : "ping";

var modulesDir = Path.Combine(Environment.CurrentDirectory, "modules");
var kernel = new BuilderKernel(modulesDir);

var run = kernel.Run(text);

Console.WriteLine($"ok={run.Ok}");
Console.WriteLine($"hash={run.Hash}");
Console.WriteLine($"folder={run.Folder}");
