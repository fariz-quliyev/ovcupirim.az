import { NavLink } from 'react-router'

import { Button } from '@/components/ui/Button'
import { NotFoundArtwork } from '@/components/ui/NotFoundArtwork'

export function NotFoundPage() {
  return (
    <NotFoundArtwork
      message="Axtardığınız səhifə mövcud deyil"
      action={
        <NavLink to="/">
          <Button>Ana səhifəyə qayıt</Button>
        </NavLink>
      }
    />
  )
}
