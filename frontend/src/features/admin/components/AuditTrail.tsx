import { formatDate } from '@/features/listings/format'

import type { AuditEntry, ModerationHistoryEntry } from '../types'

const actionLabels: Record<string, string> = {
  Approved: 'Təsdiqləndi',
  Rejected: 'Rədd edildi',
  Blocked: 'Bloklandı',
  Unblocked: 'Blok götürüldü',
  Edited: 'Dəyişdirildi',
}

/**
 * The decisions already taken on a listing. Read-only, like everything that renders the trail —
 * there is no edit or delete here because the records behind it are append-only.
 */
export function ModerationHistory({ history }: { history: ModerationHistoryEntry[] }) {
  if (history.length === 0) {
    return <p className="text-sm text-muted">Bu elana dair hələ qərar verilməyib.</p>
  }

  return (
    <ol className="flex flex-col gap-2">
      {history.map((entry, index) => (
        <li key={`${entry.createdAt}-${index}`} className="rounded-(--radius-input) border border-line px-3 py-2">
          <p className="text-sm font-medium text-ink">
            {actionLabels[entry.action] ?? entry.action} · {entry.moderatorName}
          </p>
          {entry.reason ? <p className="mt-0.5 text-sm text-muted">{entry.reason}</p> : null}
          <p className="mt-0.5 text-xs text-faint">{formatDate(entry.createdAt)}</p>
        </li>
      ))}
    </ol>
  )
}

/**
 * One audit row. The payload is structured JSON as of Phase 7, so it renders as fields rather than
 * as a string an operator has to decipher.
 */
export function AuditPayloadView({ payload }: { payload: Record<string, unknown> | null }) {
  if (!payload) {
    return <span className="text-faint">—</span>
  }

  const entries = Object.entries(payload).filter(([, value]) => value !== null && value !== undefined)

  if (entries.length === 0) {
    return <span className="text-faint">—</span>
  }

  return (
    <dl className="flex flex-col gap-0.5">
      {entries.map(([key, value]) => (
        <div key={key} className="flex flex-wrap gap-1.5">
          <dt className="text-xs text-faint">{key}:</dt>
          <dd className="text-xs text-ink">
            {typeof value === 'object' ? JSON.stringify(value) : String(value)}
          </dd>
        </div>
      ))}
    </dl>
  )
}

export function AuditRowSummary({ entry }: { entry: AuditEntry }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="font-medium text-ink">{entry.action}</span>
      <span className="text-xs text-muted">
        {entry.actorName ?? 'Sistem'} · {formatDate(entry.createdAt)}
      </span>
    </div>
  )
}
