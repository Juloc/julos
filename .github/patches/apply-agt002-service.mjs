import { readFile, writeFile } from 'node:fs/promises';

const path = 'src/JulOS.Infrastructure/Agents/PostgresAgentControlService.cs';
let source = (await readFile(path, 'utf8')).replace(/^\uFEFF/, '').replaceAll('\r\n', '\n');

function replaceExactlyOnce(before, after, label) {
  const first = source.indexOf(before);
  const second = first < 0 ? -1 : source.indexOf(before, first + before.length);
  if (first < 0 || second >= 0) {
    throw new Error(`${label}: expected exactly one source match`);
  }
  source = source.slice(0, first) + after + source.slice(first + before.length);
}

replaceExactlyOnce(
  '        var credentialBytes = RandomNumberGenerator.GetBytes(48);',
  '        var credentialBytes = DecodeEnrollmentCredential(request.Credential);',
  'client-generated credential',
);

replaceExactlyOnce(
  `            if (token.RedeemedAtUtc is not null)
            {
                throw Failure("agent.enrollment_token_reused", "Enrollment token was already redeemed.");
            }`,
  `            if (token.RedeemedAtUtc is not null)
            {
                var retry = await this.ResolveEnrollmentRetryAsync(
                    token,
                    request,
                    credentialBytes,
                    cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return retry;
            }`,
  'idempotent retry branch',
);

replaceExactlyOnce(
  '            return new AgentCredential(agentId, Base64Url(credentialBytes), now);',
  '            return new AgentCredential(agentId, request.Credential, now);',
  'confirmed client credential response',
);

replaceExactlyOnce(
  '    public async Task<bool> AuthenticateAsync(',
  `    private async Task<AgentCredential> ResolveEnrollmentRetryAsync(
        AgentEnrollmentTokenRow token,
        RedeemAgentEnrollmentRequest request,
        byte[] credentialBytes,
        CancellationToken cancellationToken)
    {
        if (token.RedeemedByAgentId is not Guid agentId)
        {
            throw Failure("agent.enrollment_token_reused", "Enrollment token was already redeemed.");
        }

        var agent = await this.context.Agents.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == agentId, cancellationToken)
            .ConfigureAwait(false);
        var credential = await this.context.AgentCredentials.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.AgentId == agentId, cancellationToken)
            .ConfigureAwait(false);
        var submittedHash = SHA256.HashData(credentialBytes);
        try
        {
            var matches = agent is not null
                && credential is not null
                && agent.State != AgentConnectionState.Revoked
                && credential.RevokedAtUtc is null
                && string.Equals(agent.Name, request.Name, StringComparison.Ordinal)
                && string.Equals(agent.MachineIdentity, request.MachineIdentity, StringComparison.Ordinal)
                && string.Equals(agent.OperatingSystem, request.OperatingSystem, StringComparison.Ordinal)
                && string.Equals(agent.Architecture, request.Architecture, StringComparison.Ordinal)
                && string.Equals(agent.Version, request.Version, StringComparison.Ordinal)
                && submittedHash.Length == credential.CredentialHash.Length
                && CryptographicOperations.FixedTimeEquals(submittedHash, credential.CredentialHash);
            if (!matches)
            {
                throw Failure("agent.enrollment_token_reused", "Enrollment token was already redeemed.");
            }

            return new AgentCredential(agentId, request.Credential, agent!.EnrolledAtUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(submittedHash);
        }
    }

    public async Task<bool> AuthenticateAsync(`,
  'retry resolver insertion',
);

replaceExactlyOnce(
  `        if (string.IsNullOrWhiteSpace(request.Token)
            || !SafeName().IsMatch(request.Name)`,
  `        if (string.IsNullOrWhiteSpace(request.Token)
            || string.IsNullOrWhiteSpace(request.Credential)
            || !SafeName().IsMatch(request.Name)`,
  'credential request validation',
);

replaceExactlyOnce(
  '    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)',
  `    private static byte[] DecodeEnrollmentCredential(string value)
    {
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized += (normalized.Length % 4) switch
            {
                0 => string.Empty,
                2 => "==",
                3 => "=",
                _ => throw new FormatException(),
            };
            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length != 48)
            {
                CryptographicOperations.ZeroMemory(bytes);
                throw new FormatException();
            }

            return bytes;
        }
        catch (FormatException exception)
        {
            throw Failure(
                "agent.enrollment_credential_invalid",
                "Enrollment credential is invalid.",
                exception);
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)`,
  'credential decoder insertion',
);

await writeFile(path, '\uFEFF' + source.replaceAll('\n', '\r\n'), 'utf8');
