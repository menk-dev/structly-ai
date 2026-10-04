using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;

var root = Directory.GetCurrentDirectory();
var packages = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/packages");
var version = File.ReadAllText(Path.Combine(root, "version.txt")).Trim();
var properties = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
Require(properties.Descendants("Version").Single().Value == version, "Directory.Build.props and version.txt disagree.");
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".release-please-manifest.json")));
using var releaseConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "release-please-config.json")));
if (manifest.RootElement.TryGetProperty(".", out var releasedVersion))
    Require(releasedVersion.GetString() == version, "Release manifest and package version disagree.");
else
    Require(releaseConfig.RootElement.GetProperty("initial-version").GetString() == version, "Initial release version disagrees.");
if (args.Length > 1) Require(args[1] == "v" + version, "Release tag and package version disagree.");

using var package = ZipFile.OpenRead(Path.Combine(packages, $"Structly.AI.{version}.nupkg"));
var metadata = ReadXml(package, "Structly.AI.nuspec");
var ns = metadata.Root!.Name.Namespace;
var details = metadata.Root.Element(ns + "metadata")!;
Require(details.Element(ns + "id")?.Value == "Structly.AI", "Unexpected package ID.");
Require(details.Element(ns + "version")?.Value == version, "Unexpected package version.");
Require(details.Element(ns + "authors")?.Value == "menk-dev", "Unexpected authors.");
Require(!String.IsNullOrWhiteSpace(details.Element(ns + "description")?.Value), "Missing description.");
Require(details.Element(ns + "readme")?.Value == "README.md", "Missing package README.");
Require(details.Element(ns + "license")?.Attribute("type")?.Value == "expression", "Missing SPDX license expression.");
Require(details.Element(ns + "license")?.Value == "MIT", "Unexpected license expression.");
Require(details.Element(ns + "repository")?.Attribute("url")?.Value == "https://github.com/menk-dev/structly-ai", "Missing repository URL.");
Require(details.Element(ns + "repository")?.Attribute("commit")?.Value?.Length == 40, "Missing repository commit.");
Require(!details.Descendants(ns + "dependency").Any(), "The runtime package must have no NuGet dependencies.");
Require(details.Descendants(ns + "group").Single().Attribute("targetFramework")?.Value == "net10.0", "Unexpected target framework.");
var expected = new HashSet<string>(StringComparer.Ordinal)
{
    "_rels/.rels", "Structly.AI.nuspec", "[Content_Types].xml", "README.md", "LICENSE",
    "lib/net10.0/Structly.AI.dll", "lib/net10.0/Structly.AI.xml",
    "analyzers/dotnet/cs/Structly.AI.Analyzers.dll"
};
foreach (var entry in package.Entries)
    Require(expected.Contains(entry.FullName) || entry.FullName.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
        && entry.FullName.EndsWith(".psmdcp", StringComparison.Ordinal), $"Unexpected package file: {entry.FullName}");
foreach (var name in expected) Require(package.GetEntry(name) is { Length: > 0 }, $"Missing package file: {name}");
ValidateAssemblyVersion(package, "lib/net10.0/Structly.AI.dll", version);
ValidateAssemblyVersion(package, "analyzers/dotnet/cs/Structly.AI.Analyzers.dll", version);
Require(ReadXml(package, "lib/net10.0/Structly.AI.xml").Descendants("member").Count() > 100, "Missing public XML documentation.");
Require(ReadText(package, "README.md") == File.ReadAllText(Path.Combine(root, "README.md")), "Packed README is stale.");
Require(ReadText(package, "LICENSE") == File.ReadAllText(Path.Combine(root, "LICENSE")), "Packed license is stale.");
using var symbols = ZipFile.OpenRead(Path.Combine(packages, $"Structly.AI.{version}.snupkg"));
Require(symbols.Entries.All(x => x.FullName.EndsWith(".pdb", StringComparison.Ordinal) || x.FullName == "Structly.AI.nuspec"
    || x.FullName == "_rels/.rels" || x.FullName == "[Content_Types].xml"
    || x.FullName.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)), "Unexpected symbol package contents.");
using var pdbStream = symbols.GetEntry("lib/net10.0/Structly.AI.pdb")?.Open() ?? throw new InvalidOperationException("Missing portable PDB.");
using var pdbBuffer = new MemoryStream();
pdbStream.CopyTo(pdbBuffer);
pdbBuffer.Position = 0;
using var pdb = MetadataReaderProvider.FromPortablePdbStream(pdbBuffer);
var reader = pdb.GetMetadataReader();
var sourceLinkId = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
var sourceLink = reader.CustomDebugInformation.Select(reader.GetCustomDebugInformation)
    .Where(x => reader.GetGuid(x.Kind) == sourceLinkId).Select(x => reader.GetBlobBytes(x.Value)).Single();
using var sourceJson = JsonDocument.Parse(sourceLink);
var revision = details.Element(ns + "repository")!.Attribute("commit")!.Value;
Require(sourceJson.RootElement.GetProperty("documents").EnumerateObject()
    .Any(x => x.Value.GetString()?.Contains($"menk-dev/structly-ai/{revision}/", StringComparison.Ordinal) == true), "Source Link and package revision disagree.");
Require(reader.Documents.All(x => !reader.GetString(reader.GetDocument(x).Name).Contains("/ref", StringComparison.Ordinal)), "Reference source in symbols.");
Console.WriteLine($"Package metadata, contents, XML, symbols and source information passed ({version}).");

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
        new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Structly.AI"), new XAttribute("Version", version)))))
        .Save(Path.Combine(consumer, "Consumer.csproj"));
    File.Copy(Path.Combine(root, "examples/Structly.AI.Consumer/Program.cs"), Path.Combine(consumer, "Program.cs"));
    var restore = await Dotnet(consumer, "restore", "Consumer.csproj", "--configfile", "NuGet.Config", "--packages", Path.Combine(consumer, "packages"), "--no-http-cache");
    Require(restore.ExitCode == 0, "Packed consumer restore failed:\n" + restore.Output);
    var run = await Dotnet(consumer, "run", "--project", "Consumer.csproj", "-c", "Release", "--no-restore");
    Require(run.ExitCode == 0 && run.Output.Contains("Offline consumer passed.", StringComparison.Ordinal), "Packed consumer failed:\n" + run.Output);
    Console.WriteLine("Fresh consumer restored solely from the local feed and ran successfully.");
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
finally { Directory.Delete(consumer, recursive: true); }

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string ReadText(ZipArchive archive, string name)
{
    using var stream = archive.GetEntry(name)!.Open();
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static XDocument ReadXml(ZipArchive archive, string name)
{
    using var stream = archive.GetEntry(name)!.Open();
    return XDocument.Load(stream);
}

static void ValidateAssemblyVersion(ZipArchive archive, string name, string version)
{
    using var stream = archive.GetEntry(name)!.Open();
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    buffer.Position = 0;
    using var pe = new PEReader(buffer);
    var reader = pe.GetMetadataReader();
    foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
    {
        var attribute = reader.GetCustomAttribute(handle);
        if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
        var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
        var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
        if (reader.GetString(type.Name) != "AssemblyInformationalVersionAttribute") continue;
        var blob = reader.GetBlobReader(attribute.Value);
        Require(blob.ReadUInt16() == 1, "Invalid assembly version attribute.");
        Require(blob.ReadSerializedString()?.Split('+')[0] == version, $"Packed assembly version disagrees: {name}");
        return;
    }
    throw new InvalidOperationException($"Missing assembly version: {name}");
}

static async Task<(int ExitCode, string Output)> Dotnet(string directory, params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = directory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    try { await process.WaitForExitAsync(budget.Token); }
    catch (OperationCanceledException)
    {
        process.Kill(entireProcessTree: true);
        throw new InvalidOperationException("Consumer verification exceeded its two-minute process budget.");
    }
    return (process.ExitCode, await stdout + await stderr);
}
