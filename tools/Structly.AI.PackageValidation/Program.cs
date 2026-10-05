using System.Xml.Linq;

var root = Directory.GetCurrentDirectory();
var packages = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/packages");
var version = File.ReadAllText(Path.Combine(root, "version.txt")).Trim();
var properties = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
PackageInspection.Require(properties.Descendants("Version").Single().Value == version, "Directory.Build.props and version.txt disagree.");
PackageInspection.Require(File.ReadAllLines(Path.Combine(root, "CHANGELOG.md")).Contains("## " + version), "Missing changelog entry for the package version.");
if(args.Length > 1)
    PackageInspection.Require(args[1] == "v" + version, "Release tag and package version disagree.");

PackageInspection.Validate(root, packages, version);
await ConsumerValidation.Validate(root, packages, version);
