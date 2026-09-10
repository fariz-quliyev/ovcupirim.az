import { NavLink } from 'react-router'

import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'

export function NotFoundPage() {
  return (
    <div className="py-16">
      <EmptyState
        title="Səhifə tapılmadı"
        description="Axtardığınız səhifə silinib və ya ünvan səhvdir."
        action={
          <NavLink to="/">
            <Button size="sm">Ana səhifəyə qayıt</Button>
          </NavLink>
        }
      />
    </div>
  )
}
