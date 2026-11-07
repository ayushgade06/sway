using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Sway.Core.Tests;

/// <summary>D-16: Sway makes no network calls. No shipped assembly may reference networking assemblies.</summary>
public class PrivacyTests
{
    static readonly string[] Forbidden = ["System.Net.Http", "System.Net.Sockets", "System.Net.WebClient", "System.Net.Requests", "System.Net.WebSockets"];

    [Theory]
    [InlineData("src/Sway.Core", "Sway.Core.dll")]
    [InlineData("src/Sway", "Sway.dll")]
    [InlineData("tools/Sway.Probe", "Sway.Probe.dll")]
    public void AssemblyReferencesNoNetworking(string project, string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Sway.slnx"))) root = root.Parent!;
        var built = new DirectoryInfo(Path.Combine(root.FullName, project, "bin")).EnumerateFiles(file, SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        Assert.True(built is not null, $"{project} must be built before running tests");

        using var pe = new PEReader(File.OpenRead(built.FullName));
        var md = pe.GetMetadataReader();
        var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
        Assert.Contains("System.Runtime", refs); // sanity: we really read the references
        Assert.Empty(refs.Intersect(Forbidden));
    }
}
