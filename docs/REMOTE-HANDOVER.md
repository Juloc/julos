# Remote (REM-005..008) deployed validation — handover

Status as of 2026-08-08. This documents exactly what was proven, what changed, and what is still open, for whoever continues this work item next (agent or human).

## What is proven

A real Remote SSH session was created and connected through the actual, unmodified production code path: real JulOS Server, a real Runtime Manager container with a Docker-socket mount, the real signed-and-installed `de.juloc.julos.remote` package, and the real provider runtime image (`packages/JulOS.Remote/runtime/Dockerfile`) — against a real `linuxserver/openssh-server` target container. The session reached `state: "connected"` with a real `connectedAtUtc`, and a WebSocket upgrade against JulOS Server's own `/api/v1/remote/sessions/{id}/display` endpoint returned `101 Switching Protocols` with the `guacamole` subprotocol echoed back and live guacd protocol bytes streaming from the real target.

This validation found and fixed five real, previously-undiscovered defects (all committed to `main`, all with regression coverage where a unit test could exercise them):

1. `ISecretLeaseService` (the only sanctioned path to decrypt a secret) had zero callers anywhere in the repository — Remote sessions never actually received their target credential, and nothing ever exercised the Browser display-credential path it was documented as backing either. Fixed in `PostgresRemoteSessionProvisioner` (commit `bee0c37`, decision D030).
2. No runtime environment variable carried the session's expected revision, without which a provider can never report a valid `connected` event (`PostgresRemoteSessionConnectionService.ConnectAsync` requires an exact match). Fixed alongside (1).
3. The "exact provider runtime composition" open decision — resolved by building `packages/JulOS.Remote/runtime/` (guacd built from official source, the unmodified official Guacamole web application, and a new minimal `JulOS.Remote.ProviderBridge` translator using the already-published `JulOS.Remote.Transport` encoder). Commit `bee0c37`, decision D031, `docs/REMOTE-PROVIDER-RUNTIME.md`.
4. `docker container ls` always reports a bare short image ID for any digest-referenced container (verified on both a locally built and a registry-pulled image) — so Runtime Manager's own identity check rejected every digest-pinned runtime unconditionally, which is fatal since digest-pinning is mandatory policy. Fixed by storing the image reference as a container label. Commit `5192d75`, decision D032.
5. `RuntimePolicy.LooksLikeSecretName` didn't recognize `CREDENTIAL`, so the fix in (1) never passed Runtime Manager's own secret-environment validation. Fixed alongside (4).

A latent bug in the *documented* dev compose stack was also found and fixed: `deploy/compose/compose.yaml`'s `migrate` service passed only `--migrate-database` as the container command, which the image's `exec gosu "$@"` entrypoint cannot resolve to an executable on its own. Now passes the full `dotnet /application/JulOS.Server.dll --migrate-database`.

## What changed (all pushed to `main`)

- `bee0c37` — credential-lease + expected-revision wiring, the provider runtime image and its `JulOS.Remote.ProviderBridge` tool.
- `5192d75` — the two further Runtime Manager fixes, plus `deploy/compose/compose.yaml` + new `deploy/compose/compose.remote.yaml` wiring Remote into the dev stack, plus the migrate-command fix.

Read `docs/DECISIONS.md` D030–D032 and `docs/REMOTE-PROVIDER-RUNTIME.md` before touching any of this — they explain the *why* behind each piece, especially why `Remote:Providers`/`Remote:NetworkProfiles` live in a separate opt-in overlay file rather than the base compose file (`ConfiguredRemoteRuntimePolicy.Read` validates those sections eagerly at server startup whenever their keys are merely *present*, regardless of value — putting them in the base file with blank defaults would crash the stack for anyone not using Remote).

## How the validation was actually run (not committed — reproduce if needed)

All of this lived in `/tmp` and ad hoc `docker run` commands, not in the repository. To redo it:

1. Build `julos-server:local`, `julos-runtime-manager:local`, and `packages/JulOS.Remote/runtime/Dockerfile` as `julos-remote-provider:local` (`docker build -f <dockerfile> -t <tag> .` from repo root).
2. Bring up Postgres + a `linuxserver/openssh-server` target container on a shared user-defined Docker network.
3. Run `dotnet /application/JulOS.Server.dll --migrate-database` against that Postgres.
4. Generate a secret key ring (`openssl rand -base64 32 > primary.key`), a Runtime Manager API key and a provider-callback signing key (`openssl rand -hex 32` each), and an ECDSA P-256 key pair for package signing.
5. Start `runtime-manager` with the Docker socket mounted and `RuntimeManager__ApiKey`/`RuntimeManager__AllowedNetworks__0` set.
6. Start `server` with `Secrets__*`, `Packages__TrustedPublishers__0__*` (must match the manifest's own `PublisherId`, e.g. `juloc-official`), `Remote__RuntimeManager__*`, `Remote__ProviderCallback__*`, `Remote__Display__ProviderEndpointTemplate=ws://julos-{runtimeId}:8081/` (the `julos-` prefix matches how `DockerCliRuntimeBackend` names containers — this is *not* automatic, it must be written into the template by whoever configures the deployment), `Remote__Display__PublicOrigin`, and one `Remote__Providers__0__*` / `Remote__NetworkProfiles__0__*` entry — **and mount a persistent volume at `/var/lib/julos/packages`**, or package files vanish on every container recreation while the database still thinks the package is installed.
7. Build the Remote package artifact (`tools/build-package-artifact.sh packages/JulOS.Remote <output.zip>` — needs a `zip` on PATH; there is none on stock Windows/Git Bash, a tiny 7-Zip-backed shim was used), sign it (ECDSA P-256 / SHA-256, IEEE P1363 signature format) with the same key configured as trusted, and install/configure/enable it through `POST /api/v1/packages/install` → `PUT .../configuration` → `POST .../enable` (see `tests/JulOS.Integration.Tests/Packages/PackageEndpointTests.cs` for the exact multipart shape).
8. Create a secret reference (`POST /api/v1/secret-references`, `owningScopeType: "package"`, `owningScopeId: "de.juloc.julos.remote"`, `purpose: "remote.password"`, `secretValue` = the raw UTF-8 JSON credential string, *not* pre-base64-encoded — Core base64-encodes it itself when forwarding to the runtime).
9. Invoke the capability directly: `POST /api/v1/packages/de.juloc.julos.remote/capabilities/remote.session/create` with a JSON body shaped `{"payload": {...CreateRemoteSessionRequest fields...}}` (antiforgery-protected, needs a real cookie session). The Remote frontend's own session form only *accepts* an existing `secretReferenceId` — it does not create one, which is intentional, not a bug (noted in D030's write-up).

Windows/Git-Bash gotchas hit along the way: `MSYS_NO_PATHCONV=1` is needed for `-e VAR=/container/path`-style arguments but actively breaks `-v /host/path:...` volume mounts (which need the real `cygpath -w` Windows path instead) — the two requirements are contradictory within one `docker run` invocation, so volume host paths were always passed as explicit `C:\Users\...` strings while `MSYS_NO_PATHCONV=1` stayed set for everything else. `curl -F "field=@path;type=..."` silently corrupts the argument under MSYS path-mangling; drop the `;type=...` suffix entirely.

## What is still open

- **VNC and RDP were not independently re-run end to end.** They exercise the same provider runtime, credential path (`JulOS.Remote.ProviderBridge` already branches on protocol for RDP's `GuacamoleRdpOptions` and SSH's optional public-key `GuacamoleSshOptions`) and display transport as the validated SSH path, but nothing has actually connected to a real RDP or VNC target yet. Lower risk than SSH was, since the hard, previously-unknown parts of the pipeline are now proven — but not "done" until actually run.
- **The provider image is not published.** `packages/JulOS.Remote/runtime/Dockerfile` has no publish workflow yet (compare `.github/workflows/publish-browser-runtime.yml` for the established pattern: validate → lifecycle smoke test → multi-arch build → immutable GHCR tag → provenance attestation).
- **Nobody has driven this through the real Desktop UI in a browser.** Everything above was direct HTTP API calls. The Remote package's frontend (`packages/JulOS.Remote/frontend/remote.source.js`) has never been exercised against a live provider in this validation pass — only its documented, already-accepted REM-005 repository-level work (Guacamole client wiring, keyboard/pointer pipelines) predates this.
- **REM-006/007/008's backlog table rows are still "In progress"** (`docs/BACKLOG.md`) pending the above. Update them once VNC/RDP and a browser walkthrough land, or once a deliberate decision is made that SSH-only real validation plus code-path parity is sufficient to call the work item done.
- The full JulOS roadmap beyond Remote (Browser BRW-004/005, Docker/Proxmox packages, Files, Caddy, Discovery, operational hardening, 1.0.0 release) has not been started this session.

## Suggested next step

Either: (a) repeat the same manual validation for one more protocol (VNC is simplest — no NLA/certificate complexity) to raise confidence before calling REM-006..008 done, or (b) move on to a real browser-driven walkthrough of the SSH path already proven, using the Remote package's actual frontend instead of raw capability-endpoint calls, to catch any frontend-specific issues (subprotocol negotiation from a real browser `WebSocket`, `Guacamole.Client` keyboard/pointer wiring against a live target) that the direct-API test could not.
