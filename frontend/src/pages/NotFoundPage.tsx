import { NavLink } from 'react-router'

import { Button } from '@/components/ui/Button'

/**
 * The illustration carries the message — "404, elan tapılmadı" is lettered into it — so the page
 * adds no heading of its own that would say the same thing twice. That message reaches a screen
 * reader through the alt text instead, and the one thing a picture cannot be, the way out, is a
 * real button beneath it.
 *
 * The artwork is a wide scene, so on a narrow screen it is shown as a crop that keeps the hunter
 * and the 404 rather than shrinking the whole thing to a strip a phone cannot read.
 */
export function NotFoundPage() {
  return (
    <div className="flex flex-col items-center gap-6 py-8 sm:py-14">
      <img
        src="/404-elan-tapilmadi.webp"
        alt="404 — axtardığınız elan tapılmadı"
        width={1672}
        height={530}
        className="w-full max-w-4xl rounded-(--radius-card) max-sm:aspect-[5/3] max-sm:object-cover max-sm:object-[30%_center]"
      />

      <NavLink to="/">
        <Button>Ana səhifəyə qayıt</Button>
      </NavLink>
    </div>
  )
}
