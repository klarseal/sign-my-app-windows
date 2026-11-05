using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SignMyApp.App.Tests;

/// <summary>
/// Antivirus flagged run-time compiled temp DLLs before. The app must not reference PowerShell hosting,
/// Roslyn scripting, CodeDom compilers or Reflection.Emit, and must not open network connections itself.
/// </summary>
public class NoRuntimeCompileTests
{
    static readonly string[] ForbiddenAssemblies =
    [
        "System.Management.Automation", "Microsoft.CodeAnalysis", "Microsoft.CodeAnalysis.CSharp", "Microsoft.CodeAnalysis.CSharp.Scripting",
        "Microsoft.CodeAnalysis.Scripting", "System.CodeDom", "Microsoft.PowerShell.SDK",
    ];

    static readonly string[] ForbiddenNamespaces =
    [
        "System.Reflection.Emit", "System.Management.Automation", "Microsoft.CodeAnalysis", "System.CodeDom.Compiler", "Microsoft.CSharp",
        "System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets",
    ];

    static readonly string[] ForbiddenTypes = ["CSharpCodeProvider", "AssemblyBuilder", "DynamicMethod", "ILGenerator", "WebClient", "HttpClient", "TcpClient"];

    public static TheoryData<string> Assemblies => new() { "SignMyApp.dll", "SignMyApp.Core.dll" };

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Built_assembly_has_no_runtime_compilation_or_network_references(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        Assert.True(File.Exists(path), path);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
        Assert.DoesNotContain(refs, r => ForbiddenAssemblies.Contains(r, StringComparer.OrdinalIgnoreCase));

        var types = md.TypeReferences.Select(h => md.GetTypeReference(h)).Select(t => (Ns: md.GetString(t.Namespace), Name: md.GetString(t.Name)))
            // The XAML compiler marks its generated code with this attribute; it compiles nothing.
            .Where(t => !(t.Ns == "System.CodeDom.Compiler" && t.Name == "GeneratedCodeAttribute"))
            .ToList();
        Assert.DoesNotContain(types, t => ForbiddenNamespaces.Any(ns => t.Ns == ns || t.Ns.StartsWith(ns + ".", StringComparison.Ordinal)));
        Assert.DoesNotContain(types, t => ForbiddenTypes.Contains(t.Name));
    }
}
