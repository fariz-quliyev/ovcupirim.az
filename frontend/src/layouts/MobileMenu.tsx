import { useEffect } from 'react'
import { NavLink } from 'react-router'

import { useAuth } from '@/features/auth/useAuth'

/**
 * The phone's slide-in menu, opened from the bar's left slot.
 *
 * It carries the site's own link inventory — the same one the footer lists — because the footer is
 * the only other place those pages are reachable from, and on a phone that means scrolling past
 * everything to find them.
 *
 * "Giriş" lives here rather than in the bar: the bar's right slot is the posting action now, and a
 * sign-in control that only exists behind a protected route is one a visitor has to stumble into.
 */
export function MobileMenu({
  links,
  onClose,
}: {
  links: { to: string; label: string }[]
  onClose: () => void
}) {
  const { user, isLoading } = useAuth()

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        onClose()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  return (
    // Above the bottom bar, which is also z-50 and sits later in the document — without this the
    // bar paints over the drawer and swallows the sign-in row at the foot of it.
    <div className="fixed inset-0 z-[60] lg:hidden">
      <button
        type="button"
        tabIndex={-1}
        aria-hidden="true"
        onClick={onClose}
        className="absolute inset-0 cursor-default bg-ink/40"
      />

      <nav
        aria-label="Sayt menyusu"
        className="absolute inset-y-0 left-0 flex w-[82%] max-w-sm flex-col overflow-y-auto bg-surface"
      >
        <div className="flex items-center justify-between px-4 py-4">
          <NavLink
            to="/"
            onClick={onClose}
            className="font-heading text-lg font-extrabold text-wordmark"
          >
            ovcupirim.az
          </NavLink>

          <button
            type="button"
            onClick={onClose}
            aria-label="Bağla"
            autoFocus
            className="grid size-10 place-items-center rounded-full text-ink active:bg-canvas"
          >
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              className="size-5"
              aria-hidden="true"
            >
              <path d="M6 6l12 12M18 6 6 18" />
            </svg>
          </button>
        </div>

        <ul className="flex-1 px-4 py-2">
          {links.map((link) => (
            <li key={link.to}>
              <NavLink
                to={link.to}
                onClick={onClose}
                className="block py-3 text-[15px] text-ink active:text-interactive"
              >
                {link.label}
              </NavLink>
            </li>
          ))}
        </ul>

        {isLoading ? null : (
          <div className="border-t border-line px-4 py-4">
            <NavLink
              to={user ? '/kabinet' : '/giris'}
              onClick={onClose}
              className="block py-2 text-[15px] font-semibold text-interactive"
            >
              {user ? user.fullName : 'Giriş'}
            </NavLink>
          </div>
        )}
      </nav>
    </div>
  )
}
