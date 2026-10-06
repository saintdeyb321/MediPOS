using System.Xml.Linq;

namespace MediPOS.ArchitectureTests;

public sealed class ProjectDependencyTests
{
    [Theory]
    [InlineData("MediPOS.SharedKernel", new string[0])]
    [InlineData("MediPOS.Domain", new[] { "MediPOS.SharedKernel" })]
    [InlineData("MediPOS.Application", new[] { "MediPOS.Domain", "MediPOS.SharedKernel" })]
    [InlineData("MediPOS.Infrastructure", new[] { "MediPOS.Application", "MediPOS.Domain", "MediPOS.SharedKernel" })]
    [InlineData("MediPOS.Api", new[] { "MediPOS.Application", "MediPOS.Infrastructure" })]
    public void ProjectReferencesMatchDependencyDirection(string projectName, string[] expectedReferences)
    {
        // Empty scaffold assemblies may omit unused references, so inspect the declared project graph.
        var project = LoadProject(projectName);
        var references = project.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(
                ((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .Order(StringComparer.Ordinal);

        Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), references);
    }

    [Theory]
    [InlineData("MediPOS.SharedKernel")]
    [InlineData("MediPOS.Domain")]
    [InlineData("MediPOS.Application")]
    public void InnerProjectsDoNotDependOnPersistenceOrAspNet(string projectName)
    {
        var project = LoadProject(projectName);

        Assert.Equal("Microsoft.NET.Sdk", (string?)project.Root?.Attribute("Sdk"));
        var dependencies = project.Descendants("PackageReference")
            .Concat(project.Descendants("Reference"))
            .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty);

        Assert.DoesNotContain(dependencies, dependency =>
            dependency.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
            || dependency.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
            || dependency.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(project.Descendants("FrameworkReference"));
    }

    private static XDocument LoadProject(string projectName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediPOS.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return XDocument.Load(Path.Combine(directory.FullName, "src", projectName, $"{projectName}.csproj"));
    }
}
