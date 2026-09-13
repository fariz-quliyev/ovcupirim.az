import { NavLink, Outlet } from 'react-router'

import { Button } from '@/components/ui/Button'
import { useAuth } from '@/features/auth/useAuth'
import { NotificationBell } from '@/features/notifications/NotificationBell'

const mainNav = [
  { to: '/kateqoriyalar', label: 'Kateqoriyalar' },
  { to: '/elanlar', label: 'Elanlar' },
  { to: '/magazalar', label: 'Mağazalar' },
  { to: '/beledci', label: 'Bələdçi' },
]

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
        <div className="mx-auto flex h-16 max-w-[1280px] items-center gap-5 px-4 sm:px-6">
          <NavLink to="/" className="font-heading text-lg font-extrabold tracking-wide text-white">
            OVCUPIRIM<span className="text-accent">.AZ</span>
          </NavLink>

          <nav className="hidden flex-1 items-center gap-6 md:flex">
            {mainNav.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                className={({ isActive }) =>
                  `text-[15px] font-medium transition-colors ${isActive ? 'text-accent' : 'text-white/90 hover:text-white'}`
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>

          <div className="ml-auto flex items-center gap-3">
            {isLoading ? (
              <span className="h-5 w-16 animate-pulse rounded bg-white/20" aria-hidden="true" />
            ) : user ? (
              <>
                <NotificationBell />

                <NavLink
                  to="/kabinet"
                  className="max-w-40 truncate text-[15px] font-medium text-white/90 hover:text-white"
                >
                  {user.fullName}
                </NavLink>
              </>
            ) : (
              <NavLink to="/giris" className="text-[15px] font-medium text-white/90 hover:text-white">
                Giriş
              </NavLink>
            )}

            <NavLink to="/yeni-elan">
              <Button variant="accent" size="sm">
                + Yeni elan
              </Button>
            </NavLink>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-[1280px] flex-1 px-4 pb-24 sm:px-6 md:pb-10">
        <Outlet />
      </main>

      <footer className="bg-brand-deep text-white/80">
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
