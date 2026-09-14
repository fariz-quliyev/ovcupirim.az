import { NavLink, Outlet } from 'react-router'

import { Button } from '@/components/ui/Button'
import { useAuth } from '@/features/auth/useAuth'
import { NotificationBell } from '@/features/notifications/NotificationBell'

import { HeaderSearch } from './HeaderSearch'

/**
 * The header is one row: brand, catalogue, search, actions — the arrangement the marketplace this
 * one is modelled on uses, and the one the site was asked for.
 *
 * The four navigation links it replaces (Kateqoriyalar, Elanlar, Mağazalar, Bələdçi) are not lost:
 * "Kataloq" opens the category tree, and the footer carries all four. Trading them for the search
 * field is the point — a classifieds visitor arrives to search, and a link row cannot do that.
 */

const bottomNav = [
  { to: '/', label: 'Əsas', end: true },
  { to: '/axtaris', label: 'Axtarış', end: false },
  { to: '/yeni-elan', label: 'Elan', end: false },
  { to: '/secilmisler', label: 'Seçilmişlər', end: false },
  { to: '/kabinet', label: 'Profil', end: false },
]

const footerColumns = [
  {
    title: 'Platforma',
    links: [
      { to: '/kateqoriyalar', label: 'Kateqoriyalar' },
      { to: '/elanlar', label: 'Elanlar' },
      { to: '/magazalar', label: 'Mağazalar' },
    ],
  },
  {
    title: 'Satıcılar',
    links: [
      { to: '/yeni-elan', label: 'Elan yerləşdir' },
      { to: '/melumat/qaydalar', label: 'Satış qaydaları' },
      { to: '/reklam', label: 'Reklam yerləşdirin' },
    ],
  },
  {
    title: 'Dəstək',
    links: [
      { to: '/yardim', label: 'Yardım' },
      { to: '/beledci', label: 'Bələdçi' },
      { to: '/melumat/tehlukesiz-alis-veris', label: 'Təhlükəsiz alış-veriş' },
      { to: '/melumat/yas-qaydalari', label: 'Yaş və uyğunluq qaydaları' },
    ],
  },
]

export function PublicLayout() {
  const { user, isLoading } = useAuth()

  return (
    <div className="flex min-h-dvh flex-col">
      <header className="sticky top-0 z-40 bg-brand">
        {/* Wraps below `lg`, where the search field takes a line of its own rather than being
            squeezed to nothing between the brand and the actions. */}
        <div className="mx-auto flex max-w-[1280px] flex-wrap items-center gap-x-3 gap-y-2.5 px-4 py-2.5 sm:px-6 lg:h-16 lg:flex-nowrap lg:gap-4 lg:py-0">
          <NavLink
            to="/"
            className="order-1 font-heading text-lg font-extrabold tracking-wide text-white"
          >
            OVCUPIRIM<span className="text-accent">.AZ</span>
          </NavLink>

          <NavLink
            to="/kateqoriyalar"
            className="order-2 hidden shrink-0 items-center gap-2 rounded-(--radius-button) bg-white/15 px-3.5 py-2 text-[15px] font-semibold text-white transition-colors hover:bg-white/25 sm:inline-flex"
          >
            <svg viewBox="0 0 24 24" fill="currentColor" className="size-4" aria-hidden="true">
              <rect x="3" y="3" width="7.5" height="7.5" rx="1.5" />
              <rect x="13.5" y="3" width="7.5" height="7.5" rx="1.5" />
              <rect x="3" y="13.5" width="7.5" height="7.5" rx="1.5" />
              <rect x="13.5" y="13.5" width="7.5" height="7.5" rx="1.5" />
            </svg>
            Kataloq
          </NavLink>

          <HeaderSearch className="order-4 w-full lg:order-3 lg:w-auto lg:flex-1" />

          <div className="order-3 ml-auto flex items-center gap-1 sm:gap-2 lg:order-4 lg:ml-0">
            <NavLink
              to="/secilmisler"
              aria-label="Seçilmişlər"
              className="flex size-9 items-center justify-center rounded-full text-white/90 transition-colors hover:bg-white/10 hover:text-white"
            >
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                className="size-5"
                aria-hidden="true"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M12 20s-7.5-4.6-7.5-9.6A4.4 4.4 0 0 1 12 7.6a4.4 4.4 0 0 1 7.5 2.8c0 5-7.5 9.6-7.5 9.6Z"
                />
              </svg>
            </NavLink>

            {isLoading ? null : user ? <NotificationBell /> : null}

            {/* Before the sign-in control, as on the reference. The bottom bar already carries
                "Elan" on a phone, so this is the desktop affordance. */}
            <NavLink to="/yeni-elan" className="hidden sm:block">
              <Button variant="accent" size="sm">
                + Yeni elan
              </Button>
            </NavLink>

            {isLoading ? (
              <span className="h-5 w-16 animate-pulse rounded bg-white/20" aria-hidden="true" />
            ) : user ? (
              <NavLink
                to="/kabinet"
                className="hidden max-w-40 truncate px-1 text-[15px] font-medium text-white/90 hover:text-white sm:block"
              >
                {user.fullName}
              </NavLink>
            ) : (
              <NavLink
                to="/giris"
                className="rounded-(--radius-button) bg-white/15 px-3.5 py-2 text-[15px] font-semibold text-white transition-colors hover:bg-white/25"
              >
                Giriş
              </NavLink>
            )}
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-[1280px] flex-1 px-4 pb-24 sm:px-6 md:pb-10">
        <Outlet />
      </main>

      <footer className="bg-brand text-white/80">
        <div className="mx-auto grid max-w-[1280px] gap-8 px-4 py-10 sm:px-6 md:grid-cols-4">
          <div>
            <div className="font-heading text-base font-extrabold text-white">
              OVCUPIRIM<span className="text-accent">.AZ</span>
            </div>
            <p className="mt-3 max-w-xs text-sm">
              Ov, balıqçılıq, kamp və outdoor avadanlıqları üçün ixtisaslaşmış elan platforması.
            </p>
          </div>

          {footerColumns.map((column) => (
            <div key={column.title}>
              <h4 className="text-sm font-bold text-white">{column.title}</h4>
              <ul className="mt-3 space-y-2 text-sm">
                {column.links.map((link) => (
                  <li key={link.to}>
                    <NavLink to={link.to} className="hover:text-white">
                      {link.label}
                    </NavLink>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="border-t border-white/10">
          <div className="mx-auto max-w-[1280px] px-4 py-4 text-xs sm:px-6">
            © {new Date().getFullYear()} Ovcupirim.az
          </div>
        </div>
      </footer>

      <nav className="fixed inset-x-0 bottom-0 z-50 grid grid-cols-5 border-t border-line bg-surface/95 backdrop-blur md:hidden">
        {bottomNav.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.end}
            className={({ isActive }) =>
              `py-3 text-center text-xs font-medium ${isActive ? 'text-accent' : 'text-faint'}`
            }
          >
            {item.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
