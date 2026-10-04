using System.Reflection;

namespace Structly.AI.Tests;

public sealed class PackageScaffoldTests
{
    [Fact]
    public void LibraryAssemblyHasTheIntendedIdentityAndNoPublicApi()
    {
        var assembly = Assembly.Load("Structly.AI");

        Assert.Equal("Structly.AI", assembly.GetName().Name);
        Assert.Empty(assembly.GetExportedTypes());
    }
}
