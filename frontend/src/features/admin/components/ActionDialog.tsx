import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'

import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Textarea'

interface DialogShellProps {
  title: string
  children: ReactNode
  onClose: () => void
}

/**
 * A modal built on the native <dialog> element: focus trapping, Escape and the backdrop come from
 * the platform rather than from a dependency.
 */
function DialogShell({ title, children, onClose }: DialogShellProps) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current

    if (dialog && !dialog.open) {
      dialog.showModal()
    }
  }, [])

  return (
    <dialog
      ref={ref}
      aria-label={title}
      onClose={onClose}
      onCancel={onClose}
      className="w-[min(28rem,calc(100vw-2rem))] rounded-(--radius-card) border border-line bg-surface p-0 text-ink backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-4 p-5">
        <h2 className="text-base font-semibold text-ink">{title}</h2>
        {children}
      </div>
    </dialog>
  )
}

interface ConfirmDialogProps {
  title: string
  /** State the consequence in words. An operator should not have to remember what this does. */
  description: string
  confirmLabel: string
  busy?: boolean
  onConfirm: () => void
  onCancel: () => void
}

/** Every irreversible or outward-facing admin action goes through this first. */
export function ConfirmDialog({
  title,
  description,
  confirmLabel,
  busy = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  return (
    <DialogShell title={title} onClose={onCancel}>
      <p className="text-sm leading-relaxed text-muted">{description}</p>

      <div className="flex flex-wrap justify-end gap-2">
        <Button type="button" variant="secondary" size="sm" onClick={onCancel} disabled={busy}>
          İmtina
        </Button>
        <Button type="button" size="sm" onClick={onConfirm} disabled={busy}>
          {busy ? 'Göndərilir…' : confirmLabel}
        </Button>
      </div>
    </DialogShell>
  )
}

interface ReasonDialogProps {
  title: string
  description: string
  confirmLabel: string
  /** The server's field error for `reason`, rendered where the operator is typing. */
  error?: string | undefined
  busy?: boolean
  onConfirm: (reason: string) => void
  onCancel: () => void
}

/**
 * Rejection, blocking and suspension all take a reason the seller will be shown and the audit trail
 * will keep. The field is required here and validated again on the server; the server's message
 * lands on this field rather than in a toast.
 */
export function ReasonDialog({
  title,
  description,
  confirmLabel,
  error,
  busy = false,
  onConfirm,
  onCancel,
}: ReasonDialogProps) {
  const [reason, setReason] = useState('')

  return (
    <DialogShell title={title} onClose={onCancel}>
      <p className="text-sm leading-relaxed text-muted">{description}</p>

      <Textarea
        label="Səbəb *"
        value={reason}
        maxLength={500}
        counter
        onChange={(event) => setReason(event.target.value)}
        error={error}
        required
      />

      <div className="flex flex-wrap justify-end gap-2">
        <Button type="button" variant="secondary" size="sm" onClick={onCancel} disabled={busy}>
          İmtina
        </Button>
        <Button
          type="button"
          size="sm"
          onClick={() => onConfirm(reason.trim())}
          disabled={busy || reason.trim().length === 0}
        >
          {busy ? 'Göndərilir…' : confirmLabel}
        </Button>
      </div>
    </DialogShell>
  )
}
