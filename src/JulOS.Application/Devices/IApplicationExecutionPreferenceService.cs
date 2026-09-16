using JulOS.Contracts.Devices;

namespace JulOS.Application.Devices;

/// <summary>
/// Reads and writes what happens to an application's surface in the background.
/// </summary>
/// <remarks>
/// The preference belongs to the authenticated user. A package cannot reach this service:
/// it is written only through an authenticated, antiforgery-protected request, which is
/// what makes "an application cannot enable keep-surface-active for itself" true rather
/// than merely intended.
/// </remarks>
public interface IApplicationExecutionPreferenceService
{
    /// <summary>Resolves the background mode for one application in one workspace class.</summary>
    /// <param name="userId">Authenticated user.</param>
    /// <param name="applicationDefinitionId">The application.</param>
    /// <param name="workspaceClass">Workspace class the preference applies to.</param>
    /// <param name="presentedKey">The raw key from the device cookie, if the request carried one.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>
    /// The stored preference, or the documented default when nothing is stored. A
    /// device-scoped preference answers ahead of the user's shared one.
    /// </returns>
    Task<ApplicationExecutionPreferenceResponse> ReadCurrentAsync(
        Guid userId,
        Guid applicationDefinitionId,
        string workspaceClass,
        string? presentedKey,
        CancellationToken cancellationToken = default);

    /// <summary>Stores the background mode the user chose.</summary>
    /// <param name="userId">Authenticated user.</param>
    /// <param name="applicationDefinitionId">The application.</param>
    /// <param name="workspaceClass">Workspace class the preference applies to.</param>
    /// <param name="presentedKey">The raw key from the device cookie, if the request carried one.</param>
    /// <param name="request">The chosen mode and the revision it is based on.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>The stored preference and whether the write created it.</returns>
    /// <exception cref="ApplicationExecutionPreferenceException">
    /// The application does not exist, or does not declare the requested mode.
    /// </exception>
    Task<ApplicationExecutionPreferenceWriteResult> WriteCurrentAsync(
        Guid userId,
        Guid applicationDefinitionId,
        string workspaceClass,
        string? presentedKey,
        UpdateApplicationExecutionPreferenceRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>A stored preference and whether the write created it.</summary>
/// <param name="Preference">The authoritative preference after the write.</param>
/// <param name="Created">True when the write created the preference rather than replacing it.</param>
public sealed record ApplicationExecutionPreferenceWriteResult(
    ApplicationExecutionPreferenceResponse Preference,
    bool Created);

/// <summary>Caller-safe execution-preference failure.</summary>
public sealed class ApplicationExecutionPreferenceException : Exception
{
    /// <summary>Creates an execution-preference failure.</summary>
    /// <param name="code">Stable error code.</param>
    /// <param name="message">Caller-safe explanation.</param>
    public ApplicationExecutionPreferenceException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable error code.</summary>
    public string Code { get; }
}
