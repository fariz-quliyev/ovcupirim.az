import { useLayoutEffect } from 'react'
import { useLocation } from 'react-router'

/**
 * Keeps `--scrollbar-width` on the document matching the vertical scrollbar the browser is actually
 * drawing.
 *
 * It exists so a band can run from one edge of the page to the other. `100vw` is the obvious way to
 * ask for that and the wrong one: it includes the vertical scrollbar, the visible page excludes it,
 * and the difference is a horizontal scrollbar across the whole site. CSS cannot report the figure,
 * so it is measured here — `innerWidth` counts the scrollbar, `documentElement.clientWidth` does
 * not — and everything that needs the true viewport width subtracts it.
 *
 * Measured in a layout effect, before the frame is painted, so the band is never briefly too wide.
 * It runs again on resize and on every navigation, because a page short enough not to scroll has no
 * scrollbar at all and the figure drops to zero.
 */
export function useScrollbarWidth() {
  const { pathname } = useLocation()

  useLayoutEffect(() => {
    const measure = () => {
      const measured = window.innerWidth - document.documentElement.clientWidth

      /* A scrollbar is a scrollbar; the widest anyone draws is around twenty pixels. Anything
         outside that range is not one — jsdom reports a client width of zero, and a browser
         mid-zoom can report a negative — and the safe answer there is the default. */
      const plausible = measured > 0 && measured <= 40 ? measured : 0

      document.documentElement.style.setProperty('--scrollbar-width', `${plausible}px`)
    }

    measure()
    window.addEventListener('resize', measure)
    return () => window.removeEventListener('resize', measure)
  }, [pathname])
}
