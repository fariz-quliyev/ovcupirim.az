import { CategoryIcon } from '@/features/home/CategoryIcon'

import { categoryImageUrl } from './categoryImage'
import type { CategoryNode } from './types'

/**
 * A category's own picture where an administrator has set one, and its glyph until then.
 *
 * Decorative in every place it is used — the category name is always beside it — so the image
 * carries an empty alt and the glyph is hidden from the accessibility tree.
 */
export function CategoryThumb({
  category,
  className = 'size-9',
  iconClassName = 'size-5',
}: {
  category: CategoryNode
  className?: string
  iconClassName?: string
}) {
  if (category.imageKey) {
    return (
      <img
        src={categoryImageUrl(category.imageKey)}
        alt=""
        loading="lazy"
        className={`${className} shrink-0 rounded-(--radius-button) object-cover`}
      />
    )
  }

  return (
    <span
      className={`${className} grid shrink-0 place-items-center rounded-(--radius-button) bg-canvas text-interactive`}
    >
      <CategoryIcon iconKey={category.iconKey} className={iconClassName} />
    </span>
  )
}
