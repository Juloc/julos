# Host Metrics capability

`host.metrics.read` is the JulOS 1.0 read contract used by the official Host Metrics package.

## Contract

- name: `host.metrics.read`
- version: `1.0.0`
- operation: `latest`
- request payload:
  - `agentId`: optional Agent UUID
  - `maximumAgeSeconds`: optional freshness threshold from 15 to 900 seconds
- response states:
  - `live`: Agent and latest persisted observations are current
  - `stale`: Agent is current but the latest observations exceed the requested age
  - `offline`: the resolved Agent is not connected
  - `unavailable`: the Agent is connected but has no persisted observations

Unknown metric values remain JSON `null`. They must never be replaced with zero.

When no `agentId` is supplied, JulOS selects the only available Agent or the only connected Agent. More than one eligible Agent requires an explicit target.

## Invocation path

Package frontends call:

`POST /api/v1/packages/{packageId}/capabilities/{capabilityName}/{operation}`

The request body is:

```json
{
  "payload": {
    "agentId": "00000000-0000-0000-0000-000000000000",
    "maximumAgeSeconds": 90
  }
}
```

The Desktop obtains an antiforgery token and binds the package identity into `PackageCapabilityClient`. Raw authentication tokens are never exposed to package code.

## Authorization

A call is accepted only when all conditions hold:

1. the authenticated user has package-read and authorization-read permissions
2. the package is installed, enabled and worker-healthy
3. the verified signed manifest declares the capability with direction `requires`
4. the broker grants the capability only to that package identity
5. a healthy compatible provider exists

The Host Metrics package never references Agent infrastructure directly. The Core-owned provider reads persisted Agent telemetry through `IAgentControlService`.

## Freshness and retention

The provider reads a bounded recent range and returns only the latest point per supported series. Supported 1.0 metric groups are CPU, memory, load, uptime, storage and aggregate non-loopback network counters.

Cancellation and the broker deadline are propagated through the complete read path.
