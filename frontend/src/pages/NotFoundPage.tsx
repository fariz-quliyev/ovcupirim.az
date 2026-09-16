import { NavLink } from 'react-router'

import { Button } from '@/components/ui/Button'

/**
 * The illustration carries the message — "404, elan tapılmadı" is lettered into it — so the page
 * adds no heading of its own that would say the same thing twice. That message reaches a screen
 * reader through the alt text instead, and the one thing a picture cannot be, the way out, is a
 * real button beneath it.
 *
 * The artwork is a wide scene and is never cropped — a crop that fits a phone cuts a digit off the
 * 404 or a word off the line beneath it. It simply gets narrower, and the lettering inside it
 * becomes too small to read there, so the phone gets the message back as real text below.
 */
export function NotFoundPage() {
  return (
    <div className="flex flex-col items-center gap-6 py-8 sm:py-14">
      <img
        src="/404-elan-tapilmadi.webp"
        alt="404 — axtardığınız elan tapılmadı"
        width={1672}
        height={530}
        className="w-full max-w-4xl rounded-(--radius-card)"
      />

      <p className="text-center text-lg font-semibold text-ink sm:hidden">
        Axtardığını elan mövcud deyil
      </p>

      <NavLink to="/">
        <Button>Ana səhifəyə qayıt</Button>
      </NavLink>
    </div>
  )
}
