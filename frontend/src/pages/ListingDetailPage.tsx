import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'
import { ErrorState } from '@/components/ui/ErrorState'
import { NotFoundArtwork } from '@/components/ui/NotFoundArtwork'
import { Select } from '@/components/ui/Select'
import { Skeleton } from '@/components/ui/Skeleton'
import { Textarea } from '@/components/ui/Textarea'
import { useAuth } from '@/features/auth/useAuth'
import {
  addFavorite,
  catalogueKeys,
  getPublicListing,
  getPublicListingPhone,
  getSimilarListings,
  listingKeys,
  removeFavorite,
  reportListing,
} from '@/features/listings/api'
import { ListingCard } from '@/features/listings/ListingCard'
import { formatDate, formatPrice, shortIdFromParam } from '@/features/listings/format'
import type { ReportReason } from '@/features/listings/types'
import { StoreLogo } from '@/features/stores/StoreCard'
import { StoreListingGrid } from '@/features/stores/StoreListingGrid'
import { storePath } from '@/features/stores/types'

const reportReasons: { value: ReportReason; label: string }[] = [
  { value: 'Prohibited', label: 'Qadağan olunmuş məhsul' },
  { value: 'Fraud', label: 'Fırıldaqçılıq' },
  { value: 'WrongCategory', label: 'Yanlış kateqoriya' },
  { value: 'Duplicate', label: 'Təkrar elan' },
  { value: 'MisleadingPrice', label: 'Yanlış qiymət' },
  { value: 'ForeignPhotos', label: 'Özgə şəkilləri' },
  { value: 'Other', label: 'Digər' },
]

/**
 * The buyer-facing page. Laid out the way a classifieds detail page is expected to read: gallery,
 * price, title, a location-first attribute table, then the description and the seller.
 */
export function ListingDetailPage() {
  const { slug } = useParams()
  const shortId = shortIdFromParam(slug)
  const [active, setActive] = useState(0)
  const [phoneShown, setPhoneShown] = useState(false)
  const [ageConfirmed, setAgeConfirmed] = useState(false)
  const [reportOpen, setReportOpen] = useState(false)
  const { user } = useAuth()
  const queryClient = useQueryClient()

  const listing = useQuery({
    queryKey: [...listingKeys.public(shortId ?? 0), ageConfirmed],
    queryFn: () => getPublicListing(shortId!, ageConfirmed),
    enabled: shortId !== null,
    retry: false,
  })

  const similar = useQuery({
    queryKey: catalogueKeys.similar(shortId ?? 0),
    queryFn: () => getSimilarListings(shortId!),
    enabled: shortId !== null && listing.isSuccess,
  })

  const report = useMutation({
    mutationFn: ({ reason, comment }: { reason: ReportReason; comment: string }) =>
      reportListing(shortId!, reason, comment.trim() === '' ? null : comment),
    onSuccess: () => setReportOpen(false),
  })

  const favorite = useMutation({
    mutationFn: () => (listing.data?.isFavorited ? removeFavorite(shortId!) : addFavorite(shortId!)),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['listings'] }),
  })

  const phone = useQuery({
    queryKey: [...listingKeys.public(shortId ?? 0), 'phone'],
    queryFn: () => getPublicListingPhone(shortId!),
    enabled: phoneShown && shortId !== null,
  })

  if (shortId === null) {
    return <ListingNotFound />
  }

  if (listing.isPending) {
    return <Skeleton className="h-96" />
  }

  // The server withholds a restricted listing until the visitor acknowledges the age requirement.
  // It reports that as a field error naming the category, which is what this screen renders.
  const gatedCategory =
    listing.error instanceof ApiError ? listing.error.fieldError('ageConfirmation') : undefined

  if (gatedCategory) {
    return (
      <section className="mx-auto flex max-w-md flex-col items-center gap-4 rounded-(--radius-card) border border-line bg-surface px-6 py-12 text-center">
        <h1 className="text-lg font-semibold text-ink">Yaş təsdiqi</h1>
        <p className="text-sm text-muted">
          “{gatedCategory}” bölməsindəki elanlar 18 yaşdan yuxarı istifadəçilər üçündür. Davam
          etmək üçün yaşınızı təsdiqləyin.
        </p>
        <Button type="button" onClick={() => setAgeConfirmed(true)}>
          18 yaşım tamam olub
        </Button>
        <Link to="/elanlar" className="text-sm text-interactive hover:underline">
          Geri qayıt
        </Link>
      </section>
    )
  }

  // A listing that is gone and a request that failed are different things and must not look alike.
  // The first is an ordinary end to a shared link — sold, expired, withdrawn — and there is nothing
  // to retry; the second is worth trying again, and saying "does not exist" about it would be a
  // guess.
  if (listing.error instanceof ApiError && listing.error.status === 404) {
    return <ListingNotFound />
  }

  if (listing.isError) {
    return (
      <ErrorState
        description="Elanı yükləmək mümkün olmadı. Bir azdan yenidən cəhd edin."
        onRetry={() => void listing.refetch()}
      />
    )
  }

  if (!listing.data) {
    return <ListingNotFound />
  }

  const data = listing.data
  const images = data.media
  const current = images[active] ?? images[0]

  return (
    <article className="flex flex-col gap-6">
      <nav aria-label="Kateqoriya yolu" className="flex flex-wrap gap-1.5 text-sm text-muted">
        <Link to="/kateqoriyalar" className="hover:underline">
          Bütün kateqoriyalar
        </Link>
        {data.categoryPath.map((entry) => (
          <span key={entry.slug}>· {entry.nameAz}</span>
        ))}
        <span>· {data.categoryNameAz}</span>
      </nav>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
        <div className="flex flex-col gap-3">
          {current ? (
            <>
              <img
                src={current.variants['detail'] ?? current.url}
                alt={data.title}
                width={current.width}
                height={current.height}
                className="w-full rounded-(--radius-card) border border-line bg-surface object-contain"
              />

              {images.length > 1 ? (
                <ul className="flex flex-wrap gap-2">
                  {images.map((image, index) => (
                    <li key={image.id}>
                      <button
                        type="button"
                        aria-label={`Şəkil ${index + 1}`}
                        aria-current={index === active ? 'true' : undefined}
                        onClick={() => setActive(index)}
                        className={`overflow-hidden rounded-(--radius-input) border ${
                          index === active ? 'border-interactive' : 'border-line'
                        }`}
                      >
                        <img src={image.variants['thumb'] ?? image.url} alt="" className="size-16 object-cover" />
                      </button>
                    </li>
                  ))}
                </ul>
              ) : null}
            </>
          ) : null}
        </div>

        <div className="flex flex-col gap-4">
          <p className="text-3xl font-semibold text-ink">{formatPrice(data.price, data.currency)}</p>
          <h1 className="text-xl font-semibold text-ink">{data.title}</h1>

          <dl className="divide-y divide-line rounded-(--radius-card) border border-line bg-surface">
            <Row label="Şəhər" value={data.regionNameAz} />
            <Row label="Kateqoriya" value={data.categoryNameAz} />
            <Row label="Vəziyyət" value={data.condition === 'New' ? 'Yeni' : 'İşlənmiş'} />
            {data.brand ? <Row label="Marka" value={data.brand} /> : null}
            <Row label="Çatdırılma" value={data.hasDelivery ? 'Bəli' : 'Xeyr'} />
            {data.attributes.map((attribute) => (
              <Row key={attribute.key} label={attribute.labelAz} value={attribute.displayValue} />
            ))}
          </dl>

          <div className="flex flex-col gap-2 rounded-(--radius-card) border border-line bg-surface p-4">
            {data.store ? (
              <Link
                to={storePath(data.store.slug)}
                className="flex items-center gap-3 hover:underline"
              >
                <StoreLogo name={data.store.name} url={data.store.logoUrl} className="size-11" />

                <span className="flex flex-col">
                  <span className="font-medium text-ink">{data.store.name}</span>
                  <span className="text-sm text-muted">
                    Mağaza{data.store.isVerified ? ' · Təsdiqlənmiş' : ''}
                  </span>
                </span>
              </Link>
            ) : (
              <p className="font-medium text-ink">{data.sellerName}</p>
            )}

            {data.showPhone ? (
              phoneShown && phone.data ? (
                <a href={`tel:${phone.data.contactPhone}`} className="text-lg font-semibold text-interactive">
                  {phone.data.contactPhone}
                </a>
              ) : (
                <Button type="button" onClick={() => setPhoneShown(true)} disabled={phone.isFetching}>
                  {phone.isFetching ? 'Yüklənir…' : `Nömrəni göstər · ${data.contactPhoneMasked ?? ''}`}
                </Button>
              )
            ) : (
              <p className="text-sm text-muted">Satıcı nömrəsini gizlədib.</p>
            )}
          </div>

          <div className="flex flex-wrap gap-2">
            {user ? (
              <Button
                type="button"
                variant="secondary"
                size="sm"
                disabled={favorite.isPending}
                onClick={() => favorite.mutate()}
              >
                {listing.data?.isFavorited ? 'Seçilmişlərdən çıxar' : 'Seçilmişlərə əlavə et'}
              </Button>
            ) : (
              <Link to="/giris">
                <Button type="button" variant="secondary" size="sm">
                  Seçilmişlərə əlavə et
                </Button>
              </Link>
            )}

            <Button type="button" variant="ghost" size="sm" onClick={() => setReportOpen((open) => !open)}>
              Şikayət et
            </Button>
          </div>

          {reportOpen ? (
            <form
              className="flex flex-col gap-3 rounded-(--radius-card) border border-line p-4"
              onSubmit={(event) => {
                event.preventDefault()
                const form = new FormData(event.currentTarget)
                report.mutate({
                  reason: String(form.get('reason')) as ReportReason,
                  comment: String(form.get('comment') ?? ''),
                })
              }}
            >
              <Select label="Səbəb" name="reason" defaultValue="Prohibited">
                {reportReasons.map((reason) => (
                  <option key={reason.value} value={reason.value}>
                    {reason.label}
                  </option>
                ))}
              </Select>

              <Textarea label="Şərh (istəyə bağlı)" name="comment" maxLength={1000} />

              <Button type="submit" size="sm" disabled={report.isPending}>
                Göndər
              </Button>
            </form>
          ) : null}

          {report.isSuccess ? (
            <p role="status" className="text-sm text-muted">
              Şikayətiniz qeydə alındı. Moderatorlar yoxlayacaq.
            </p>
          ) : null}

          <p className="text-sm text-muted">
            № {data.shortId} · {formatDate(data.publishedAt)} · Baxış sayı: {data.viewCount}
          </p>
        </div>
      </div>

      <section className="flex flex-col gap-2">
        <h2 className="text-base font-semibold text-ink">Təsvir</h2>
        {/* Plain text, rendered as text nodes — listings never carry markup. */}
        <p className="whitespace-pre-line text-[15px] leading-relaxed text-ink">{data.description}</p>
      </section>

      {data.store ? (
        <StoreListingGrid slug={data.store.slug} />
      ) : similar.data && similar.data.length > 0 ? (
        <section className="flex flex-col gap-3">
          <h2 className="text-base font-semibold text-ink">Bənzər elanlar</h2>

          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
            {similar.data.map((item) => (
              <ListingCard key={item.shortId} listing={item} />
            ))}
          </ul>
        </section>
      ) : null}
    </article>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4 px-4 py-2.5">
      <dt className="text-sm text-muted">{label}</dt>
      <dd className="text-right text-[15px] text-ink">{value}</dd>
    </div>
  )
}

/** Someone followed a link to a listing that is sold, expired, withdrawn — or never existed. */
function ListingNotFound() {
  return (
    <NotFoundArtwork
      message="Axtardığınız elan mövcud deyil"
      action={
        <Link to="/elanlar">
          <Button>Bütün elanlara bax</Button>
        </Link>
      }
    />
  )
}
