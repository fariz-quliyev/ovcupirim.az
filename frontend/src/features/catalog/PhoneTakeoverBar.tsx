import { useNavigate } from 'react-router'

/**
 * The title bar for a screen that owns the whole phone, standing in for the site header that
 * PublicLayout hides on those routes.
 *
 * Leaving returns to wherever the screen was opened from. A deep link has nothing behind it inside
 * the site, so that case falls back to `fallback` rather than stepping out of the site altogether.
 */
export function PhoneTakeoverBar({
  title,
  variant = 'close',
  fallback = '/',
}: {
  title: string
  variant?: 'close' | 'back'
  fallback?: string
}) {
  const navigate = useNavigate()

  function leave() {
    if (typeof window !== 'undefined' && (window.history.state as { idx?: number } | null)?.idx) {
      void navigate(-1)
    } else {
      void navigate(fallback)
    }
  }

  return (
    <div className="-mx-4 sticky top-0 z-30 flex h-14 items-center border-b border-line bg-surface px-2 sm:hidden">
      <button
        type="button"
        onClick={leave}
        aria-label={variant === 'back' ? 'Geri' : 'Bağla'}
        className="grid size-10 place-items-center rounded-full text-ink active:bg-canvas"
      >
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          className="size-5"
          aria-hidden="true"
        >
          <path d={variant === 'back' ? 'm15 5-7 7 7 7' : 'M6 6l12 12M18 6 6 18'} />
        </svg>
      </button>

      {/* Centred on the bar itself rather than on the space beside the button, so the title does
          not shift about; the side padding keeps a long category name clear of the button. */}
      <span className="pointer-events-none absolute inset-x-0 truncate px-14 text-center font-heading font-bold text-ink">
        {title}
      </span>
    </div>
  )
}
