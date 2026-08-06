using JulOS.Contracts.Authorization;

namespace JulOS.Server.Authorization;

/// <summary>Named backend policies used by Core endpoints.</summary>
internal static class JulOsAuthorizationPolicies
{
    internal const string SystemVersionRead = "permission:" + AuthorizationPermissionNames.SystemVersionRead;
    internal const string AuthorizationRead = "permission:" + AuthorizationPermissionNames.AuthorizationRead;
    internal const string AuthorizationManage = "permission:" + AuthorizationPermissionNames.AuthorizationManage;
    internal const string OperationCreate = "permission:" + AuthorizationPermissionNames.OperationCreate;
    internal const string OperationRead = "permission:" + AuthorizationPermissionNames.OperationRead;
    internal const string OperationCancel = "permission:" + AuthorizationPermissionNames.OperationCancel;
    internal const string SecretRead = "permission:" + AuthorizationPermissionNames.SecretRead;
    internal const string SecretManage = "permission:" + AuthorizationPermissionNames.SecretManage;
    internal const string PackageRead = "permission:" + AuthorizationPermissionNames.PackageRead;
    internal const string PackageManage = "permission:" + AuthorizationPermissionNames.PackageManage;
}
