import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { NavLink, Outlet } from 'react-router'

import { Button } from '@/components/ui/Button'
import { adminKeys, getOverview } from '@/features/admin/api'
import { useAuth } from '@/features/auth/useAuth'
import type { UserRole } from '@/features/auth/types'

interface AdminSection {
  to: string
  label: string
  /** Which roles may use this section. Mirrors the server policy exactly. */
  roles: UserRole[]
  /** Which overview counter, if any, belongs on this section's badge. */
  badge?: 'pendingListings' | 'openReports' | 'pendingStores' | 'lateCaptures'
}

/**
 * The navigation, and the single place the client's view of RBAC is written down.
 *
 * It mirrors the server policies rather than inventing its own: content decisions are open to
 * moderators, structural and business ones are Admin-only. Hiding a link is a courtesy, not a
 * control — every route behind these is enforced server-side and answers 403 to a moderator who
 * types the URL directly.
 */
const sections: AdminSection[] = [
  { to: '/admin', label: 'İcmal', roles: ['Admin', 'Moderator'] },
  { to: '/admin/moderation', label: 'Elan moderasiyası', roles: ['Admin', 'Moderator'], badge: 'pendingListings' },
  { to: '/admin/reports', label: 'Şikayətlər', roles: ['Admin', 'Moderator'], badge: 'openReports' },
  { to: '/admin/stores', label: 'Mağaza müraciətləri', roles: ['Admin'], badge: 'pendingStores' },
  { to: '/admin/payments', label: 'Ödənişlər', roles: ['Admin'], badge: 'lateCaptures' },
  { to: '/admin/packages', label: 'Paketlər', roles: ['Admin'] },
  { to: '/admin/taxonomy', label: 'Kateqoriyalar', roles: ['Admin'] },
  { to: '/admin/regions', label: 'Regionlar', roles: ['Admin'] },
  { to: '/admin/users', label: 'İstifadəçilər', roles: ['Admin'] },
  { to: '/admin/audit', label: 'Audit jurnalı', roles: ['Admin'] },
]

export function AdminLayout() {
  const { user } = useAuth()
  const [navOpen, setNavOpen] = useState(false)

  // Drives the sidebar badges. Moderators may read it too, so it never 403s for someone who is here.
  const overview = useQuery({
    queryKey: adminKeys.overview,
    queryFn: getOverview,
    staleTime: 30_000,
    enabled: Boolean(user),
  })

  const visible = sections.filter((section) => user && section.roles.includes(user.role))

  function badgeFor(section: AdminSection): number | null {
    if (!section.badge || !overview.data) {
      return null
    }

    const count = overview.data[section.badge].count

    return count > 0 ? count : null
  }

  return (
    <div className="flex min-h-dvh flex-col bg-canvas">
      <header className="sticky top-0 z-40 border-b border-line bg-surface">
        <div className="flex h-14 items-center gap-3 px-4">
          <button
            type="button"
            aria-label="Menyu"
            aria-expanded={navOpen}
            onClick={() => setNavOpen((open) => !open)}
            className="rounded-(--radius-input) border border-line px-2.5 py-1.5 text-sm lg:hidden"
          >
            ☰
          </button>

          <NavLink to="/admin" className="font-heading text-sm font-extrabold tracking-wide text-ink">
            OVCUPIRIM <span className="text-muted">İdarəetmə</span>
          </NavLink>

          <div className="ml-auto flex items-center gap-3 text-sm">
            <span className="hidden text-muted sm:inline">
              {user?.fullName} · {user?.role}
            </span>

            <NavLink to="/">
              <Button type="button" variant="secondary" size="sm">
                Sayta qayıt
              </Button>
            </NavLink>
          </div>
        </div>
      </header>

      <div className="flex flex-1 flex-col lg:flex-row">
        <nav
          aria-label="İdarəetmə bölmələri"
          className={`border-b border-line bg-surface lg:w-60 lg:shrink-0 lg:border-b-0 lg:border-r ${
            navOpen ? 'block' : 'hidden lg:block'
          }`}
        >
          <ul className="flex flex-col gap-0.5 p-2">
            {visible.map((section) => {
              const badge = badgeFor(section)

              return (
                <li key={section.to}>
                  <NavLink
                    to={section.to}
                    end={section.to === '/admin'}
                    onClick={() => setNavOpen(false)}
                    className={({ isActive }) =>
                      `flex items-center justify-between gap-2 rounded-(--radius-input) px-3 py-2 text-sm ${
                        isActive ? 'bg-interactive-soft font-semibold text-interactive' : 'text-ink hover:bg-canvas'
                      }`
                    }
                  >
                    <span>{section.label}</span>
                    {badge !== null ? (
                      <span className="rounded-full bg-accent px-1.5 py-0.5 text-xs font-semibold text-white">
                        {badge}
                      </span>
                    ) : null}
                  </NavLink>
                </li>
              )
            })}
          </ul>
        </nav>

        <main className="min-w-0 flex-1 p-4 sm:p-5">
          <Outlet />
        </main>
      </div>
    </div>
  )
}

/**
 * Shown where a screen is genuinely unusable on a small display — dense tables an operator cannot
 * work with on a phone. The queues that matter in the field stay available.
 */
export function DesktopOnlyNotice({ children }: { children: React.ReactNode }) {
  return (
    <>
      <div className="rounded-(--radius-card) border border-line bg-surface px-4 py-6 text-center text-sm text-muted lg:hidden">
        Bu bölmə daha geniş ekranda istifadə üçün nəzərdə tutulub.
      </div>

      <div className="hidden lg:block">{children}</div>
    </>
  )
}
