import { JulOsApiClient } from './api-client.js';

export interface AgentView {
  readonly agentId: string;
  readonly name: string;
  readonly machineIdentity: string;
  readonly operatingSystem: string;
  readonly architecture: string;
  readonly version: string;
  readonly state: string;
  readonly enrolledAtUtc: string;
  readonly lastSeenAtUtc: string | null;
  readonly revokedAtUtc: string | null;
  readonly revision: number;
}

export interface AgentMetricPoint {
  readonly observedAtUtc: string;
  readonly value: number | null;
}

export interface AgentMetricSeries {
  readonly agentId: string;
  readonly name: string;
  readonly unit: string;
  readonly labels: Readonly<Record<string, string>>;
  readonly points: readonly AgentMetricPoint[];
}

export interface AgentDashboardEntry {
  readonly agent: AgentView;
  readonly connectivity: 'online' | 'stale' | 'offline' | 'revoked' | 'unknown';
  readonly observedAtUtc: string | null;
}

export class AgentDashboardStore {
  readonly #api: JulOsApiClient;
  readonly #now: () => Date;
  #agents: AgentView[] = [];

  public constructor(
    fetchImplementation: typeof fetch = globalThis.fetch.bind(globalThis),
    now: () => Date = () => new Date(),
  ) {
    this.#api = new JulOsApiClient(fetchImplementation);
    this.#now = now;
  }

  public async refresh(): Promise<readonly AgentDashboardEntry[]> {
    this.#agents = await this.#api.get<AgentView[]>('/api/v1/agents/');
    return this.snapshot();
  }

  public snapshot(): readonly AgentDashboardEntry[] {
    const now = this.#now().getTime();
    return this.#agents.map((agent) => ({
      agent,
      connectivity: connectivity(agent, now),
      observedAtUtc: agent.lastSeenAtUtc,
    }));
  }

  public readMetrics(
    agentId: string,
    fromUtc: Date,
    toUtc: Date,
  ): Promise<readonly AgentMetricSeries[]> {
    validateAgentId(agentId);
    if (!Number.isFinite(fromUtc.getTime())
      || !Number.isFinite(toUtc.getTime())
      || fromUtc >= toUtc
      || toUtc.getTime() - fromUtc.getTime() > 31 * 24 * 60 * 60 * 1000) {
      throw new RangeError('Agent metric range must be positive and at most 31 days.');
    }

    const query = new URLSearchParams({
      fromUtc: fromUtc.toISOString(),
      toUtc: toUtc.toISOString(),
    });
    return this.#api.get<AgentMetricSeries[]>(
      `/api/v1/agents/${encodeURIComponent(agentId)}/metrics?${query.toString()}`,
    );
  }
}

function connectivity(agent: AgentView, now: number): AgentDashboardEntry['connectivity'] {
  if (agent.revokedAtUtc !== null || agent.state.toLowerCase() === 'revoked') {
    return 'revoked';
  }
  if (agent.lastSeenAtUtc === null) {
    return 'unknown';
  }

  const observed = Date.parse(agent.lastSeenAtUtc);
  if (!Number.isFinite(observed)) {
    return 'unknown';
  }
  const age = now - observed;
  if (age <= 90_000) {
    return 'online';
  }
  if (age <= 5 * 60_000) {
    return 'stale';
  }
  return 'offline';
}

function validateAgentId(value: string): void {
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(value)) {
    throw new TypeError('Agent identifier is invalid.');
  }
}
