# Durable Remote provisioning

Remote session creation persists the exact-idempotent session first and returns that `requested` snapshot to the caller. Runtime allocation is deliberately not part of the initiating capability request.

`RemoteSessionProvisioningWorker` owns the asynchronous continuation. A process-local coalescing signal wakes the worker after a successful create, while the core database remains the only durable queue. On Server startup the worker reconciles persisted `requested` and interrupted `provisioning` sessions before waiting for new signals.

The worker resolves pending rows through `IRemoteSessionProvisioningReconciler` and invokes the existing idempotent `IRemoteSessionProvisioner`. Runtime identity remains `remote-{sessionId:N}`, so retrying after a Server restart cannot allocate a second provider runtime for the same session.

This separation prevents a slow first image pull or Runtime Manager create call from keeping the browser capability request open for minutes. The Remote frontend already treats non-terminal, non-connected states as pending and reads the durable session again until it becomes connected or terminal.

Provider callbacks remain authoritative for `connected` and `failed`. Provider-private bootstrap diagnostics are normalized by the authenticated internal callback endpoint before they enter the public Remote session failure contract.

## Acceptance

- `remote.session/create` persists and returns a `requested` session without waiting for Runtime Manager.
- successful create signals provisioning only after the durable row exists.
- the background worker resumes both newly requested and Server-restart-interrupted provisioning work.
- the database, not the process-local signal, carries session identity and lifecycle state.
- provisioning reuses the exact runtime identity and existing Runtime Manager idempotency behavior.
- terminal or concurrently advanced sessions are skipped rather than provisioned again.
