import { NavLink, useParams } from 'react-router'

import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { PhoneTakeoverBar } from '@/features/catalog/PhoneTakeoverBar'
import { useCategoryTree } from '@/features/catalog/hooks'

/**
 * One category's subcategories — the catalogue's second screen on a phone.
 *
 * "Bütün elanlar" sits at the head of the list because the category itself is a destination, not
 * merely a folder: someone who wants a tent but not one particular kind of tent has to be able to
 * say so without picking a subcategory first.
 *
 * Reachable on a wide screen too, where it renders as an ordinary page — the header's catalogue
 * panel is how a desktop visitor actually gets here, so the route exists mostly for a shared link
 * and for the back button.
 */
export function CategoryBrowsePage() {
  const { categorySlug } = useParams()
  const { data, isPending, isError, refetch } = useCategoryTree()

  const category = data?.find((node) => node.slug === categorySlug)

  if (isPending) {
    return (
      <div className="sm:py-10" aria-busy="true">
        <PhoneTakeoverBar title="" variant="back" fallback="/kateqoriyalar" />
        <div className="-mx-4 divide-y divide-line sm:mx-0">
          {Array.from({ length: 8 }, (_, i) => (
            <Skeleton key={i} className="mx-4 my-3 h-6 sm:mx-0" />
          ))}
        </div>
      </div>
    )
  }

  if (isError) {
    return (
      <div className="py-10">
        <ErrorState title="Kateqoriyaları yükləmək mümkün olmadı" onRetry={() => void refetch()} />
      </div>
    )
  }

  if (!category) {
    return (
      <div className="sm:py-10">
        <PhoneTakeoverBar title="Kataloq" variant="back" fallback="/kateqoriyalar" />
        <div className="py-10">
          <EmptyState
            title="Kateqoriya tapılmadı"
            description="Bu bölmə silinmiş ola bilər. Kataloqdan yenidən seçin."
          />
        </div>
      </div>
    )
  }

  return (
    <div className="sm:py-10">
      <PhoneTakeoverBar title={category.nameAz} variant="back" fallback="/kateqoriyalar" />

      <h1 className="hidden text-2xl sm:block">{category.nameAz}</h1>

      <ul className="-mx-4 divide-y divide-line border-b border-line bg-surface sm:mx-0 sm:mt-6 sm:max-w-lg sm:rounded-(--radius-card) sm:border">
        <li>
          <NavLink
            to={`/elanlar/${category.slug}`}
            className="block px-4 py-4 text-[15px] text-ink active:bg-canvas sm:hover:text-interactive"
          >
            Bütün elanlar
          </NavLink>
        </li>

        {category.children.map((child) => (
          <li key={child.slug}>
            <NavLink
              to={`/elanlar/${category.slug}/${child.slug}`}
              className="block px-4 py-4 text-[15px] text-ink active:bg-canvas sm:hover:text-interactive"
            >
              {child.nameAz}
            </NavLink>
          </li>
        ))}
      </ul>
    </div>
  )
}
