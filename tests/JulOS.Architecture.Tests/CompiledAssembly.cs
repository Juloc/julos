using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace JulOS.Architecture.Tests;

/// <summary>A type a compiled assembly refers to, read from its metadata tables.</summary>
/// <param name="Namespace">The declaring namespace, empty for a nested or global type.</param>
/// <param name="Name">The type name without namespace.</param>
internal sealed record ReferencedType(string Namespace, string Name)
{
    public override string ToString() => Namespace.Length == 0 ? Name : $"{Namespace}.{Name}";
}

/// <summary>
/// Reads assembly and type references from build output. Metadata is read directly
/// so a rule cannot be satisfied by a using directive that implicit usings removed,
/// and so no assembly is loaded into the test process.
/// </summary>
internal static class CompiledAssembly
{
    private static readonly string Configuration = RequiredMetadata("JulOS.Configuration");

    private static readonly string TargetFramework = RequiredMetadata("JulOS.TargetFramework");

    /// <summary>Returns the simple names of the assemblies the project's output refers to.</summary>
    internal static IReadOnlyList<string> AssemblyReferences(string projectFile)
    {
        return Read(
            projectFile,
            reader => reader.AssemblyReferences
                .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>Returns the types the project's output refers to.</summary>
    internal static IReadOnlyList<ReferencedType> TypeReferences(string projectFile)
    {
        return Read(
            projectFile,
            reader => reader.TypeReferences
                .Select(reader.GetTypeReference)
                .Select(type => new ReferencedType(
                    type.Namespace.IsNil ? string.Empty : reader.GetString(type.Namespace),
                    reader.GetString(type.Name)))
                .Distinct()
                .OrderBy(type => type.ToString(), StringComparer.Ordinal)
                .ToArray());
    }

    private static T Read<T>(string projectFile, Func<MetadataReader, T> read)
    {
        var assemblyFile = Path.Combine(
            Repository.DirectoryOf(projectFile),
            "bin",
            Configuration,
            TargetFramework,
            Repository.AssemblyName(projectFile) + ".dll");

        if (!File.Exists(assemblyFile))
        {
            throw new InvalidOperationException(
                $"'{assemblyFile}' does not exist. Architecture tests inspect build output, so run 'dotnet build JulOS.slnx' for the whole solution before running them.");
        }

        using var stream = File.OpenRead(assemblyFile);
        using var peReader = new PEReader(stream);

        return read(peReader.GetMetadataReader());
    }

    private static string RequiredMetadata(string key)
    {
        var value = typeof(CompiledAssembly).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)
            ?.Value;

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Assembly metadata '{key}' is missing. It is declared in JulOS.Architecture.Tests.csproj.")
            : value;
    }
}
