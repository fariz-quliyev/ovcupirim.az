import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigationType } from 'react-router'

import { Button } from '@/components/ui/Button'
import { useAuth } from '@/features/auth/useAuth'
import { NotificationBell } from '@/features/notifications/NotificationBell'

import { CatalogueMenu } from './CatalogueMenu'
import { HeaderSearch } from './HeaderSearch'
import { MobileMenu } from './MobileMenu'

/**
 * The header is one row: brand, catalogue, search, actions — the arrangement the marketplace this
 * one is modelled on uses, and the one the site was asked for.
 *
 * The four navigation links it replaces (Kateqoriyalar, Elanlar, Mağazalar, Bələdçi) are not lost:
 * "Kataloq" opens the category tree, and the footer carries all four. Trading them for the search
 * field is the point — a classifieds visitor arrives to search, and a link row cannot do that.
 */

/**
 * The phone's navigation bar. Five destinations with the posting action in the middle, raised and
 * in the brand red — the shape every classifieds app on this market uses, and the one place a
 * seller looks for it.
 */
const bottomNav = [
  { to: '/', label: 'Əsas', end: true, d: 'M4 11.5 12 4.5l8 7M6.5 10v9.5h11V10' },
  { to: '/axtaris', label: 'Axtarış', end: false, d: 'M11 4.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13Zm8.5 15-3.9-3.9' },
  { to: '/yeni-elan', label: 'Elan', end: false, d: 'M12 6v12M6 12h12', primary: true },
  {
    to: '/secilmisler',
    label: 'Seçilmişlər',
    end: false,
    d: 'M12 20s-7.5-4.6-7.5-9.6A4.4 4.4 0 0 1 12 7.6a4.4 4.4 0 0 1 7.5 2.8c0 5-7.5 9.6-7.5 9.6Z',
  },
  { to: '/kabinet', label: 'Profil', end: false, d: 'M12 12a3.75 3.75 0 1 0 0-7.5 3.75 3.75 0 0 0 0 7.5Zm-7 8a7 7 0 0 1 14 0' },
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

/**
 * Puts a new page at its top.
 *
 * Nothing did this before, so opening a page from halfway down another one left the new page at
 * that same offset — already scrolled past its own beginning. Going back is left alone: the
 * browser restores where the visitor actually was, which is the whole point of going back.
 *
 * Written out rather than using ScrollRestoration, which needs a data router and so cannot be
 * rendered by a layout that tests mount inside a memory router.
 */
function ScrollToTopOnNavigate() {
  const { pathname } = useLocation()
  const navigationType = useNavigationType()

  useEffect(() => {
    if (navigationType !== 'POP') {
      window.scrollTo(0, 0)
    }
  }, [pathname, navigationType])

  return null
}

/**
 * Rendered twice — once in the phone bar's left slot, once in the desktop action cluster — because
 * the two layouts put it in different places and only one is ever visible.
 */
function FavouritesLink({ className = '' }: { className?: string }) {
  return (
    <NavLink
      to="/secilmisler"
      aria-label="Seçilmişlər"
      className={`flex size-9 items-center justify-center rounded-full text-ink/70 transition-colors hover:bg-ink/10 hover:text-ink ${className}`}
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
  )
}

export function PublicLayout() {
  const { user, isLoading } = useAuth()
  const location = useLocation()

  /**
   * The route the catalogue panel was opened on, rather than a plain boolean. Derived this way it
   * closes itself on any navigation — a link inside it, or the browser's back button — without an
   * effect that sets state after the render it is reacting to.
   *
   * It opens on click, not on hover. The reference does both, but hover-to-open needs a
   * close-on-leave to go with it, and that pair turns a pointer merely crossing the header into a
   * panel that opens and shuts; it also leaves nothing sensible for a touch screen, which has no
   * hover at all. Click alone is unambiguous, and it is what the button's ✕ state describes.
   * Pointing at a category inside the panel still swaps the second column — that is the part of the
   * reference's behaviour worth copying.
   */
  const [openedOn, setOpenedOn] = useState<string | null>(null)
  const catalogueOpen = openedOn === location.pathname

  /** Keyed on the route for the same reason as the catalogue panel: navigation closes it. */
  const [menuOpenedOn, setMenuOpenedOn] = useState<string | null>(null)
  const menuOpen = menuOpenedOn === location.pathname

  /** Routes that own the whole phone screen and supply their own title bar. */
  const phoneTakeover = location.pathname.startsWith('/kateqoriyalar')

  function toggleCatalogue() {
    setOpenedOn(catalogueOpen ? null : location.pathname)
  }

  function closeCatalogue() {
    setOpenedOn(null)
  }

  return (
    <div className="flex min-h-dvh flex-col">
      <ScrollToTopOnNavigate />

      {/* `relative` so the catalogue panel hangs off the bar rather than off the page: sticky
          already makes this a containing block, but saying so keeps the intent on the element. */}
      {/* The catalogue takes over the phone screen, carrying its own ✕/title bar in place of this
          one, and with the footer out of the way beneath it. Only on a phone: from `sm` up it is an
          ordinary page and keeps the site chrome. The bottom bar stays either way — it is how you
          leave for anywhere that is not the catalogue. */}
      <header className={`sticky top-0 z-40 bg-brand ${phoneTakeover ? 'max-sm:hidden' : ''}`}>
        {/* Wraps below `lg`, where the search field takes a line of its own rather than being
            squeezed to nothing between the brand and the actions. */}
        {/* Above the catalogue panel's click-catching backdrop, which covers the viewport while the
            panel is open — without this the search field and the buttons beside it would be behind
            it, and a click meant for them would only close the panel. */}
        <div className="relative z-50 mx-auto flex max-w-[1280px] flex-wrap items-center gap-x-3 gap-y-2.5 px-4 py-2.5 sm:px-6 lg:h-16 lg:flex-nowrap lg:gap-4 lg:py-0">
          {/* Below `lg` the bar is three slots — an action, the brand, an action — and the two
              outer ones each take half the leftover width, which is what puts the brand on the
              centre line rather than merely between its neighbours. Above `lg` the slots collapse
              and the row runs left to right. */}
          <div className="order-1 flex flex-1 items-center lg:hidden">
            <button
              type="button"
              onClick={() => setMenuOpenedOn(menuOpen ? null : location.pathname)}
              aria-expanded={menuOpen}
              aria-label="Menyu"
              className="grid size-9 place-items-center rounded-full text-cta transition-colors hover:bg-ink/10"
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
                <path d="M4 7h16M4 12h16M4 17h16" />
              </svg>
            </button>
          </div>

          <NavLink
            to="/"
            className="order-2 font-heading text-lg font-extrabold text-ink lg:order-1 lg:tracking-wide"
          >
            {/* Both cases written out rather than set with text-transform: the document is
                lang="az", where lowercasing "I" yields the dotless "ı". The brand is ovcupirim,
                not ovcupırım. */}
            <span className="text-cta lg:hidden">ovcupirim.az</span>
            <span className="hidden text-cta lg:inline">OVCUPIRIM.AZ</span>
          </NavLink>

          {/* A button, not a link: it opens the catalogue panel in place. /kateqoriyalar is still
              the page behind it — the phone, which never sees this button, goes there instead. */}
          <button
            type="button"
            onClick={toggleCatalogue}
            aria-expanded={catalogueOpen}
            aria-controls="catalogue-menu"
            className="order-3 hidden shrink-0 items-center gap-2 rounded-(--radius-button) bg-cta px-3.5 py-2 text-[15px] font-semibold text-white transition hover:brightness-95 lg:order-2 lg:inline-flex"
          >
            {catalogueOpen ? (
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.2"
                className="size-4"
                aria-hidden="true"
              >
                <path strokeLinecap="round" d="M6 6l12 12M18 6 6 18" />
              </svg>
            ) : (
              <svg viewBox="0 0 24 24" fill="currentColor" className="size-4" aria-hidden="true">
                <rect x="3" y="3" width="7.5" height="7.5" rx="1.5" />
                <rect x="13.5" y="3" width="7.5" height="7.5" rx="1.5" />
                <rect x="3" y="13.5" width="7.5" height="7.5" rx="1.5" />
                <rect x="13.5" y="13.5" width="7.5" height="7.5" rx="1.5" />
              </svg>
            )}
            Kataloq
          </button>

          <HeaderSearch className="order-5 w-full lg:order-3 lg:w-auto lg:flex-1" />

          <div className="order-4 flex flex-1 items-center justify-end gap-1 sm:gap-2 lg:flex-none">
            {/* The phone's right slot is the posting action, as a disc. Sign-in moved into the
                menu behind the left slot. */}
            <NavLink
              to="/yeni-elan"
              aria-label="Yeni elan"
              className="grid size-9 place-items-center rounded-full bg-cta text-white transition hover:brightness-95 lg:hidden"
            >
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.4"
                strokeLinecap="round"
                className="size-5"
                aria-hidden="true"
              >
                <path d="M12 6v12M6 12h12" />
              </svg>
            </NavLink>

            <FavouritesLink className="hidden lg:flex" />

            {isLoading ? null : user ? <NotificationBell className="hidden lg:flex" /> : null}

            {/* Before the sign-in control, as on the reference. */}
            <NavLink to="/yeni-elan" className="hidden lg:block">
              <Button variant="accent" size="sm">
                + Yeni elan
              </Button>
            </NavLink>

            {isLoading ? (
              <span className="hidden h-5 w-16 animate-pulse rounded bg-ink/10 lg:block" aria-hidden="true" />
            ) : user ? (
              <NavLink
                to="/kabinet"
                className="hidden max-w-40 truncate px-1 text-[15px] font-medium text-ink/80 hover:text-ink lg:block"
              >
                {user.fullName}
              </NavLink>
            ) : (
              <NavLink
                to="/giris"
                className="hidden rounded-(--radius-button) bg-surface px-3.5 py-2 text-[15px] font-semibold text-interactive transition hover:brightness-95 lg:block"
              >
                Giriş
              </NavLink>
            )}
          </div>
        </div>

        {catalogueOpen ? <CatalogueMenu onClose={closeCatalogue} /> : null}
      </header>

      {menuOpen ? (
        <MobileMenu
          links={footerColumns.flatMap((column) => column.links)}
          onClose={() => setMenuOpenedOn(null)}
        />
      ) : null}

      <main className="mx-auto w-full max-w-[1280px] flex-1 px-4 pb-24 sm:px-6 md:pb-10">
        <Outlet />
      </main>

      {/* Not on a phone. Every link in it is in the menu behind the bar's left slot, so down here
          it was the same list a second time — and stacked into one column it ran longer than most
          of the pages above it. The bottom bar is what a phone ends on. */}
      <footer className="hidden bg-brand text-ink/75 md:block">
        <div className="mx-auto grid max-w-[1280px] gap-8 px-4 py-10 sm:px-6 md:grid-cols-4">
          <div>
            {/* Same wordmark, same band as the header's — so the same colour. */}
            <div className="font-heading text-base font-extrabold text-cta">OVCUPIRIM.AZ</div>
            <p className="mt-3 max-w-xs text-sm">
              Ov, balıqçılıq, kamp və outdoor avadanlıqları üçün ixtisaslaşmış elan platforması.
            </p>
          </div>

          {footerColumns.map((column) => (
            <div key={column.title}>
              <h4 className="text-sm font-bold text-ink">{column.title}</h4>
              <ul className="mt-3 space-y-2 text-sm">
                {column.links.map((link) => (
                  <li key={link.to}>
                    <NavLink to={link.to} className="hover:text-ink">
                      {link.label}
                    </NavLink>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="border-t border-ink/10">
          <div className="mx-auto max-w-[1280px] px-4 py-4 text-xs sm:px-6">
            © {new Date().getFullYear()} Ovcupirim.az
          </div>
        </div>
      </footer>

      <nav className="fixed inset-x-0 bottom-0 z-50 grid grid-cols-5 border-t border-line bg-surface/95 pb-[env(safe-area-inset-bottom)] backdrop-blur md:hidden">
        {bottomNav.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.end}
            className={({ isActive }) =>
              `flex flex-col items-center gap-1 py-2 text-[11px] font-medium ${
                item.primary ? 'text-ink' : isActive ? 'text-cta' : 'text-faint'
              }`
            }
          >
            {item.primary ? (
              // Lifted clear of the bar, the way the posting action is on every app this competes
              // with. The bar has no clipping of its own, so it simply overhangs.
              <span className="-mt-6 grid size-12 place-items-center rounded-full bg-cta text-white shadow-lg">
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2.4"
                  strokeLinecap="round"
                  className="size-6"
                  aria-hidden="true"
                >
                  <path d={item.d} />
                </svg>
              </span>
            ) : (
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                strokeLinecap="round"
                strokeLinejoin="round"
                className="size-5"
                aria-hidden="true"
              >
                <path d={item.d} />
              </svg>
            )}
            {item.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
