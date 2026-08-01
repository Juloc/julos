namespace JulOS.Architecture.Tests;

/// <summary>Committed platform project paths, relative to the repository root.</summary>
internal static class PlatformProjects
{
    internal const string Domain = "src/JulOS.Domain/JulOS.Domain.csproj";

    internal const string Contracts = "src/JulOS.Contracts/JulOS.Contracts.csproj";

    internal const string Application = "src/JulOS.Application/JulOS.Application.csproj";

    internal const string Infrastructure = "src/JulOS.Infrastructure/JulOS.Infrastructure.csproj";

    internal const string Server = "src/JulOS.Server/JulOS.Server.csproj";

    internal const string PackageSdk = "src/JulOS.PackageSdk/JulOS.PackageSdk.csproj";

    internal const string Agent = "src/JulOS.Agent/JulOS.Agent.csproj";

    internal const string RuntimeManager = "src/JulOS.RuntimeManager/JulOS.RuntimeManager.csproj";

    internal const string ArchitectureTests = "tests/JulOS.Architecture.Tests/JulOS.Architecture.Tests.csproj";

    internal const string DomainTests = "tests/JulOS.Domain.Tests/JulOS.Domain.Tests.csproj";

    internal const string InfrastructureTests = "tests/JulOS.Infrastructure.Tests/JulOS.Infrastructure.Tests.csproj";

    internal const string IntegrationTests = "tests/JulOS.Integration.Tests/JulOS.Integration.Tests.csproj";

    /// <summary>The directory holding optional package projects.</summary>
    internal const string PackagesRoot = "packages";

    /// <summary>Core owns platform concepts only and stays free of product-specific detail.</summary>
    internal static readonly string[] Core = [Domain, Contracts, Application, Infrastructure, Server];
}
