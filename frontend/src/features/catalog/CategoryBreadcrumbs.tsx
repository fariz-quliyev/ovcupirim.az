import { NavLink } from 'react-router'

import type { CategoryPathEntry } from './types'

interface CategoryBreadcrumbsProps {
  path: CategoryPathEntry[]
  current: string
}

/** Ancestors as links, the current category as plain text. Feeds BreadcrumbList JSON-LD in Phase 8. */
export function CategoryBreadcrumbs({ path, current }: CategoryBreadcrumbsProps) {
  return (
    <nav aria-label="Naviqasiya" className="flex flex-wrap items-center gap-1.5 text-sm text-faint">
      <NavLink to="/" className="hover:text-interactive">
        Ana səhifə
      </NavLink>

      <span aria-hidden="true">/</span>

      <NavLink to="/kateqoriyalar" className="hover:text-interactive">
        Kateqoriyalar
      </NavLink>

      {path.map((entry) => (
        <span key={entry.slug} className="flex items-center gap-1.5">
          <span aria-hidden="true">/</span>
          <NavLink to={`/elanlar/${entry.slug}`} className="hover:text-interactive">
            {entry.nameAz}
          </NavLink>
        </span>
      ))}

      <span aria-hidden="true">/</span>
      <span className="font-medium text-ink">{current}</span>
    </nav>
  )
}
