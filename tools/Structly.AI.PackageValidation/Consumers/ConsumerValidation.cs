using System.Diagnostics;
using System.Xml.Linq;
using static PackageInspection;

static class ConsumerValidation
{
    public static async Task Validate(string root, string packages, string version)
    {
        // Outside the repository: no Directory.Build.props, central versions, project references,
        // prior package cache or external feeds can make consumer validation pass accidentally.
        var consumer = Path.Combine(Path.GetTempPath(), "structly-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(consumer);
        try
        {
            new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
                new XElement("add", new XAttribute("key", "local"), new XAttribute("value", packages))))).Save(Path.Combine(consumer, "NuGet.Config"));
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("TargetFramework", "net10.0"),
                    new XElement("ImplicitUsings", "enable"), new XElement("Nullable", "enable"), new XElement("TreatWarningsAsErrors", "true")),
                new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Structly.AI"), new XAttribute("Version", version)), new XElement("PackageReference", new XAttribute("Include", "Structly.AI.Testing"), new XAttribute("Version", version)))))
                .Save(Path.Combine(consumer, "Consumer.csproj"));
            var copiedSources = CopyExampleSources(Path.Combine(root, "examples/Structly.AI.Consumer"), consumer);

            var restore = await Dotnet(consumer, "restore", "Consumer.csproj", "--configfile", "NuGet.Config", "--packages", Path.Combine(consumer, "packages"), "--no-http-cache");
            Require(restore.ExitCode == 0, "Packed consumer restore failed:\n" + restore.Output);
            var run = await Dotnet(consumer, "run", "--project", "Consumer.csproj", "-c", "Release", "--no-restore");
            Require(run.ExitCode == 0 && run.Output.Contains("Offline consumer passed.", StringComparison.Ordinal), "Packed consumer failed:\n" + run.Output);
            Console.WriteLine("Fresh consumer restored solely from the local feed and ran successfully.");
            foreach(var source in copiedSources)
                File.Delete(source);

            File.WriteAllText(Path.Combine(consumer, "Program.cs"), """
                using Structly.AI;
                _ = StructuredTask.Create<InvalidOutput>(new() { Instructions = "test" });
                public sealed class InvalidOutput { public Dictionary<string, string> Values { get; set; } = []; }
                """);
            var invalid = await Dotnet(consumer, "build", "Consumer.csproj", "-c", "Release", "--no-restore");
            Require(invalid.ExitCode != 0 && invalid.Output.Contains("error STAI001", StringComparison.Ordinal)
                && invalid.Output.Contains("Collection", StringComparison.Ordinal) && !invalid.Output.Contains("CS8032", StringComparison.Ordinal)
                && !invalid.Output.Contains("AD0001", StringComparison.Ordinal), "The packed analyzer did not diagnose the invalid DTO:\n" + invalid.Output);
            Console.WriteLine("Packed analyzer rejected an invalid DTO with STAI001.");
        }
        finally
        {
            Directory.Delete(consumer, recursive: true);
        }
    }

    static IReadOnlyList<string> CopyExampleSources(string sourceRoot, string consumer)
    {
        var copied = new List<string>();
        foreach(var source in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            if(relative.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
                continue;

            var destination = Path.Combine(consumer, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
            copied.Add(destination);
        }

        return copied;
    }

    static async Task<(int ExitCode, string Output)> Dotnet(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach(var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(budget.Token);
        }
        catch(OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Consumer verification exceeded its two-minute process budget.");
        }

        return (process.ExitCode, await stdout + await stderr);
    }
}
