import { useState } from 'react'
import { useNavigate } from 'react-router'

/**
 * The search field in the header, present on every page.
 *
 * It carries no region filter of its own. The homepage band it replaces had one, but a marketplace
 * header is a place to start a search, not to refine it — the catalogue and search pages already
 * own the filters, and a select wedged into the header narrows the field it sits next to on exactly
 * the screens where that field has least room.
 *
 * An empty submit goes to the full catalogue rather than to an empty result page: someone who
 * presses the button with nothing typed is asking to browse.
 */
export function HeaderSearch({ className = '' }: { className?: string }) {
  const navigate = useNavigate()
  const [term, setTerm] = useState('')

  function submit(event: React.FormEvent) {
    event.preventDefault()

    const query = term.trim()
    void navigate(query ? `/axtaris?q=${encodeURIComponent(query)}` : '/elanlar')
  }

  return (
    <form
      onSubmit={submit}
      role="search"
      className={`flex h-11 min-w-0 overflow-hidden rounded-(--radius-input) bg-surface ${className}`}
    >
      <span className="grid w-10 shrink-0 place-items-center text-faint" aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" className="size-[18px]">
          <circle cx="11" cy="11" r="7" />
          <path strokeLinecap="round" d="m20 20-3.5-3.5" />
        </svg>
      </span>

      <input
        value={term}
        onChange={(event) => setTerm(event.target.value)}
        aria-label="Avadanlıq və ya marka axtarışı"
        placeholder="Avadanlıq və ya marka axtarışı"
        className="min-w-0 flex-1 bg-transparent pe-3 text-[15px] text-ink outline-none placeholder:text-faint"
      />

      <button
        type="submit"
        className="shrink-0 bg-cta px-5 font-semibold text-white transition hover:brightness-95 sm:px-7"
      >
        Axtar
      </button>
    </form>
  )
}
