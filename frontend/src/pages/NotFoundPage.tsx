import { NavLink } from 'react-router'

import { Button } from '@/components/ui/Button'
import { NotFoundArtwork } from '@/components/ui/NotFoundArtwork'

export function NotFoundPage() {
  return (
    <NotFoundArtwork
      action={
        <NavLink to="/">
          <Button>Ana səhifəyə qayıt</Button>
        </NavLink>
      }
    />
  )
}
