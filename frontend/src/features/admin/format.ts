/**
 * How long something has been waiting, which is the number an operator actually reads off a queue.
 * Built by hand rather than through Intl, like every other date on this site.
 */
export function waitingFor(since: string | null | undefined, now: Date = new Date()): string {
  if (!since) {
    return '—'
  }

  const minutes = Math.max(0, Math.round((now.getTime() - new Date(since).getTime()) / 60000))

  if (minutes < 60) {
    return `${minutes} dəq`
  }

  if (minutes < 60 * 24) {
    return `${Math.floor(minutes / 60)} saat`
  }

  return `${Math.floor(minutes / (60 * 24))} gün`
}
