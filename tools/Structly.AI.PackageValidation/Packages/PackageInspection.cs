using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;

static class PackageInspection
{
    public static void Validate(string root, string packages, string version)
    {
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
            "analyzers/dotnet/cs/Structly.AI.Analyzers.dll",
        };
        foreach(var entry in package.Entries)
            Require(expected.Contains(entry.FullName) || entry.FullName.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
                && entry.FullName.EndsWith(".psmdcp", StringComparison.Ordinal), $"Unexpected package file: {entry.FullName}");

        foreach(var name in expected)
            Require(package.GetEntry(name) is { Length: > 0 }, $"Missing package file: {name}");

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

        using var hostingPackage = ZipFile.OpenRead(Path.Combine(packages, $"Structly.AI.Hosting.{version}.nupkg"));
        var hostingMetadata = ReadXml(hostingPackage, "Structly.AI.Hosting.nuspec");
        var hostingNs = hostingMetadata.Root!.Name.Namespace;
        var hostingDetails = hostingMetadata.Root.Element(hostingNs + "metadata")!;
        Require(hostingDetails.Element(hostingNs + "id")?.Value == "Structly.AI.Hosting", "Unexpected hosting package ID.");
        Require(hostingDetails.Element(hostingNs + "version")?.Value == version, "Unexpected hosting package version.");
        Require(hostingDetails.Element(hostingNs + "license")?.Value == "MIT", "Unexpected hosting license.");
        Require(hostingDetails.Descendants(hostingNs + "group").Single().Attribute("targetFramework")?.Value == "net10.0", "Unexpected hosting target framework.");
        var hostingDependencies = hostingDetails.Descendants(hostingNs + "dependency")
            .ToDictionary(x => x.Attribute("id")!.Value, x => x.Attribute("version")!.Value, StringComparer.Ordinal);
        Require(hostingDependencies.Count == 3 && hostingDependencies.ContainsKey("Structly.AI")
            && hostingDependencies.ContainsKey("Microsoft.Extensions.Http")
            && hostingDependencies.ContainsKey("Microsoft.Extensions.Options.ConfigurationExtensions"), "Unexpected hosting dependencies.");
        Require(hostingDependencies["Structly.AI"].Trim('[', ']') == version, "Hosting and core package versions disagree.");
        ValidateAssemblyVersion(hostingPackage, "lib/net10.0/Structly.AI.Hosting.dll", version);
        Require(ReadXml(hostingPackage, "lib/net10.0/Structly.AI.Hosting.xml").Descendants("member")
            .Any(x => x.Attribute("name")?.Value == "T:Structly.AI.Hosting.OpenAiHostingOptions"), "Missing hosting XML documentation.");
        Require(ReadText(hostingPackage, "README.md") == File.ReadAllText(Path.Combine(root, "README.md")), "Packed hosting README is stale.");
        using var hostingSymbols = ZipFile.OpenRead(Path.Combine(packages, $"Structly.AI.Hosting.{version}.snupkg"));
        Require(hostingSymbols.GetEntry("lib/net10.0/Structly.AI.Hosting.pdb") is { Length: > 0 }, "Missing hosting symbols.");
        Console.WriteLine("Hosting package metadata, dependencies, XML and symbols passed.");

        using var testingPackage = ZipFile.OpenRead(Path.Combine(packages, $"Structly.AI.Testing.{version}.nupkg"));
        var testingMetadata = ReadXml(testingPackage, "Structly.AI.Testing.nuspec");
        var testingNs = testingMetadata.Root!.Name.Namespace;
        var testingDetails = testingMetadata.Root.Element(testingNs + "metadata")!;
        Require(testingDetails.Element(testingNs + "id")?.Value == "Structly.AI.Testing", "Unexpected testing package ID.");
        Require(testingDetails.Element(testingNs + "version")?.Value == version, "Unexpected testing package version.");
        Require(testingDetails.Element(testingNs + "license")?.Value == "MIT", "Unexpected testing license.");
        Require(testingDetails.Descendants(testingNs + "group").Single().Attribute("targetFramework")?.Value == "net10.0", "Unexpected testing framework.");
        var testingDependency = testingDetails.Descendants(testingNs + "dependency").Single();
        Require(testingDependency.Attribute("id")?.Value == "Structly.AI" && testingDependency.Attribute("version")?.Value.Trim('[', ']') == version, "Testing must depend only on matching core.");
        ValidateAssemblyVersion(testingPackage, "lib/net10.0/Structly.AI.Testing.dll", version);
        Require(ReadXml(testingPackage, "lib/net10.0/Structly.AI.Testing.xml").Descendants("member").Any(), "Missing testing XML.");
        Require(ReadText(testingPackage, "README.md") == File.ReadAllText(Path.Combine(root, "README.md")), "Stale testing README.");
        using var testingSymbols = ZipFile.OpenRead(Path.Combine(packages, $"Structly.AI.Testing.{version}.snupkg"));
        Require(testingSymbols.GetEntry("lib/net10.0/Structly.AI.Testing.pdb") is { Length: > 0 }, "Missing testing symbols.");
        ValidateCompanionPackage(hostingPackage, hostingSymbols, "Structly.AI.Hosting", version, root);
        ValidateCompanionPackage(testingPackage, testingSymbols, "Structly.AI.Testing", version, root);
        Console.WriteLine("Testing package metadata, dependency, XML, contents, symbols and Source Link passed.");
    }

    internal static void ValidateCompanionPackage(ZipArchive package, ZipArchive symbols, string id, string version, string root)
    {
        var metadata = ReadXml(package, id + ".nuspec");
        var ns = metadata.Root!.Name.Namespace;
        var details = metadata.Root.Element(ns + "metadata")!;
        Require(details.Element(ns + "authors")?.Value == "menk-dev", "Unexpected companion package authors.");
        Require(details.Element(ns + "license")?.Attribute("type")?.Value == "expression", "Missing companion SPDX license.");
        Require(details.Element(ns + "readme")?.Value == "README.md", "Missing companion README metadata.");
        Require(details.Element(ns + "repository")?.Attribute("url")?.Value == "https://github.com/menk-dev/structly-ai", "Missing companion repository.");
        Require(ReadText(package, "LICENSE") == File.ReadAllText(Path.Combine(root, "LICENSE")), "Stale companion license.");
        var expected = new HashSet<string>(StringComparer.Ordinal) { "_rels/.rels", id + ".nuspec", "[Content_Types].xml", "README.md", "LICENSE", $"lib/net10.0/{id}.dll", $"lib/net10.0/{id}.xml" };
        foreach(var entry in package.Entries)
            Require(expected.Contains(entry.FullName) || entry.FullName.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal) && entry.FullName.EndsWith(".psmdcp", StringComparison.Ordinal), "Unexpected companion content: " + entry.FullName);

        foreach(var name in expected)
            Require(package.GetEntry(name) is { Length: > 0 }, "Missing companion content: " + name);

        ValidateAssemblyVersion(package, $"lib/net10.0/{id}.dll", version);
        using var stream = symbols.GetEntry($"lib/net10.0/{id}.pdb")!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        using var pdb = MetadataReaderProvider.FromPortablePdbStream(buffer);
        var reader = pdb.GetMetadataReader();
        var sourceLinkId = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
        var sourceLink = reader.CustomDebugInformation.Select(reader.GetCustomDebugInformation)
            .Where(x => reader.GetGuid(x.Kind) == sourceLinkId).Select(x => reader.GetBlobBytes(x.Value)).Single();
        using var json = JsonDocument.Parse(sourceLink);
        var revision = details.Element(ns + "repository")!.Attribute("commit")!.Value;
        Require(revision.Length == 40 && json.RootElement.GetProperty("documents").EnumerateObject()
            .Any(x => x.Value.GetString()?.Contains($"menk-dev/structly-ai/{revision}/", StringComparison.Ordinal) == true), "Companion Source Link revision disagrees.");
    }

    internal static void Require(bool condition, string message)
    {
        if(!condition)
            throw new InvalidOperationException(message);
    }

    internal static string ReadText(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static XDocument ReadXml(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }

    internal static void ValidateAssemblyVersion(ZipArchive archive, string name, string version)
    {
        using var stream = archive.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        using var pe = new PEReader(buffer);
        var reader = pe.GetMetadataReader();
        foreach(var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if(attribute.Constructor.Kind != HandleKind.MemberReference)
                continue;

            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if(constructor.Parent.Kind != HandleKind.TypeReference)
                continue;

            var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if(reader.GetString(type.Name) != "AssemblyInformationalVersionAttribute")
                continue;

            var blob = reader.GetBlobReader(attribute.Value);
            Require(blob.ReadUInt16() == 1, "Invalid assembly version attribute.");
            Require(blob.ReadSerializedString()?.Split('+')[0] == version, $"Packed assembly version disagrees: {name}");
            return;
        }

        throw new InvalidOperationException($"Missing assembly version: {name}");
    }
}
