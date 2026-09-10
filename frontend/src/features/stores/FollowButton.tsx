import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'

import { Button } from '@/components/ui/Button'
import { useAuth } from '@/features/auth/useAuth'

import { followStore, unfollowStore } from './api'

interface FollowButtonProps {
  slug: string
  isFollowing: boolean
}

/**
 * "İzlə" / "İzlənilir". Both calls are idempotent on the server, so a double click cannot leave
 * the button and the follower count disagreeing.
 */
export function FollowButton({ slug, isFollowing }: FollowButtonProps) {
  const { user } = useAuth()
  const queryClient = useQueryClient()

  const toggle = useMutation({
    mutationFn: () => (isFollowing ? unfollowStore(slug) : followStore(slug)),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['stores'] }),
  })

  if (!user) {
    return (
      <Link to="/giris">
        <Button type="button" variant="secondary">
          İzlə
        </Button>
      </Link>
    )
  }

  return (
    <Button
      type="button"
      variant={isFollowing ? 'secondary' : 'primary'}
      aria-pressed={isFollowing}
      disabled={toggle.isPending}
      onClick={() => toggle.mutate()}
    >
      {isFollowing ? 'İzlənilir' : 'İzlə'}
    </Button>
  )
}
