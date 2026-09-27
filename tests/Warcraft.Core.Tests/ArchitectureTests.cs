using System.Xml.Linq;

namespace Warcraft.Core.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void SourceProjectsDoNotReferenceOtherFeatureModules()
    {
        var root = FindRepositoryRoot();
        var projects = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "Warcraft.*.csproj", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(projects);

        foreach (var project in projects)
        {
            var document = XDocument.Load(project);
            var references = document
                .Descendants("ProjectReference")
                .Select(x => (string?)x.Attribute("Include"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Path.GetFileName(x!))
                .ToArray();

            if (Path.GetFileName(project).Equals("Warcraft.Api.csproj", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Empty(references);
                continue;
            }

            Assert.All(
                references,
                reference => Assert.Equal("Warcraft.Api.csproj", reference));
        }
    }

    [Fact]
    public void ApiProjectHasNoCounterStrikeSharpDependency()
    {
        var root = FindRepositoryRoot();
        var apiProject = Path.Combine(root, "src", "Warcraft.Api", "Warcraft.Api.csproj");
        var document = XDocument.Load(apiProject);

        var packages = document
            .Descendants("PackageReference")
            .Select(x => (string?)x.Attribute("Include"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        Assert.DoesNotContain(
            packages,
            package => package!.Contains("CounterStrikeSharp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SharedContractAssemblyHasTheConfiguredProductVersion()
    {
        var root = FindRepositoryRoot();
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var configured = props
            .Descendants("VersionPrefix")
            .Select(x => x.Value.Trim())
            .First(x => !string.IsNullOrWhiteSpace(x));

        Assert.Equal(configured, Warcraft.Api.WarcraftVersion.Current);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CS2Warcraft.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find CS2Warcraft.slnx from the test output directory.");
    }
}
