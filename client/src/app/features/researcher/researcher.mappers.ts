import { ResearchRunEventItem } from '../../core/models/research-query.model';
import {
  DiscoverySourceRow,
  DiscoverySourceSummary,
  ResearchErrorGroup,
  ResearchErrorGroupRow,
  ResearchEventRow,
  ResearchPipelineStageView,
  RESEARCH_PIPELINE_STAGES
} from '../../core/models/researcher.model';
import { formatDate } from '../../core/utils/format.utils';

export function mapSeverityClass(severity: string | null | undefined): string {
  const value = (severity ?? 'info').toLowerCase();
  if (value === 'critical' || value === 'error') {
    return 'danger';
  }
  if (value === 'warning') {
    return 'warning';
  }
  return 'info';
}

export function mapSourceStatusClass(status: string | null | undefined): string {
  const value = (status ?? '').toLowerCase();
  if (value === 'active') {
    return 'success';
  }
  if (value === 'disabled' || value === 'evaluationfailed') {
    return 'danger';
  }
  if (value === 'temporarilydeprioritized') {
    return 'warning';
  }
  return 'muted';
}

export function mapEventToRow(item: ResearchRunEventItem): ResearchEventRow {
  const relatedKind = item.relatedEntityKind ?? '';
  const relatedId = item.relatedEntityId ?? '';
  const durationMs = item.durationMs;
  return {
    id: item.id,
    researchRunId: item.researchRunId ?? '',
    eventType: item.eventType ?? '',
    severity: item.severity ?? 'Info',
    severityClass: mapSeverityClass(item.severity),
    stage: item.stage ?? '',
    message: item.message ?? '',
    createdLabel: formatDate(item.createdAtUtc ?? null),
    durationLabel: durationMs == null ? '—' : `${durationMs} ms`,
    url: item.url ?? '',
    domain: item.domain ?? '',
    httpStatusLabel: item.httpStatusCode == null ? '—' : String(item.httpStatusCode),
    provider: item.provider ?? '',
    dataJson: item.dataJson ?? '',
    hasDataJson: !!item.dataJson,
    relatedEntityId: relatedId,
    relatedEntityKind: relatedKind,
    hasFetchLink: relatedKind.toLowerCase() === 'crawledpage' && !!relatedId
  };
}

export function mapErrorGroupToRow(item: ResearchErrorGroup): ResearchErrorGroupRow {
  const runId = item.researchRunId ?? '';
  return {
    category: item.category,
    sampleMessage: item.sampleMessage,
    count: item.count,
    firstLabel: formatDate(item.firstOccurredAtUtc),
    lastLabel: formatDate(item.lastOccurredAtUtc),
    researchRunId: runId,
    hasRunLink: !!runId,
    domain: item.domain ?? '',
    url: item.url ?? '',
    stage: item.stage ?? '',
    remediation: item.suggestedRemediation ?? '',
    hasRemediation: !!item.suggestedRemediation
  };
}

export function mapSourceToRow(item: DiscoverySourceSummary): DiscoverySourceRow {
  return {
    id: item.id,
    name: item.name,
    baseUrl: item.baseUrl,
    sourceType: item.sourceType,
    status: item.status,
    statusClass: mapSourceStatusClass(item.status),
    candidatesDiscovered: item.candidatesDiscovered,
    uniqueCompaniesDiscovered: item.uniqueCompaniesDiscovered,
    qualifiedProspectsYielded: item.qualifiedProspectsYielded,
    liveVerifiedCompanies: item.liveVerifiedCompanies,
    failureRateLabel:
      item.failureRate == null ? '—' : `${(item.failureRate * 100).toFixed(1)}%`,
    averageFetchLabel: item.averageFetchMs == null ? '—' : `${Math.round(item.averageFetchMs)} ms`,
    lastAttemptedLabel: formatDate(item.lastAttemptedAtUtc ?? null),
    lastError: item.lastError ?? ''
  };
}

export function buildPipelineStages(events: ResearchRunEventItem[]): ResearchPipelineStageView[] {
  const counts = new Map<string, number>();
  for (const event of events) {
    const stage = event.stage ?? '';
    if (!stage) {
      continue;
    }
    counts.set(stage, (counts.get(stage) ?? 0) + 1);
  }

  return RESEARCH_PIPELINE_STAGES.map((key) => ({
    key,
    labelKey: `researcher.stages.${key}`,
    active: (counts.get(key) ?? 0) > 0,
    count: counts.get(key) ?? 0
  }));
}

export function formatElapsed(startedAtUtc: string | null | undefined, nowMs: number): string {
  if (!startedAtUtc) {
    return '—';
  }
  const started = new Date(startedAtUtc).getTime();
  if (Number.isNaN(started)) {
    return '—';
  }
  const totalSeconds = Math.max(0, Math.floor((nowMs - started) / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (hours > 0) {
    return `${hours}h ${minutes}m ${seconds}s`;
  }
  if (minutes > 0) {
    return `${minutes}m ${seconds}s`;
  }
  return `${seconds}s`;
}
