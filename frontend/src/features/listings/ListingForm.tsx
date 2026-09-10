import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'
import { Checkbox } from '@/components/ui/Checkbox'
import { Input } from '@/components/ui/Input'
import { Select } from '@/components/ui/Select'
import { Textarea } from '@/components/ui/Textarea'
import { orderedAttributes } from '@/features/catalog/attributeControls'
import { RegionSelect } from '@/features/catalog/RegionSelect'
import type { CategorySchema } from '@/features/catalog/types'
import { getMyStore, storeKeys } from '@/features/stores/api'

import { AttributeField } from './AttributeField'
import { createListing, publishListing, updateListing } from './api'
import { ImageUploader } from './ImageUploader'
import { formatPrice } from './format'
import type { ListingDetail, ListingMedia } from './types'

interface ListingFormProps {
  schema: CategorySchema
  /** Present when editing; absent while creating, until the draft is first saved. */
  existing?: ListingDetail
  defaultPhone: string
  onPublished: (listing: ListingDetail) => void
}

interface FormState {
  title: string
  description: string
  price: string
  condition: 'New' | 'Used'
  brand: string
  hasDelivery: boolean
  regionSlug: string
  contactPhone: string
  showPhone: boolean
  attributes: Record<string, unknown>
}

/**
 * One page, five sections — the pattern Tap.az uses — rather than a multi-step wizard. The only
 * hard boundary is the draft: images need a listing to belong to, so the draft is saved before the
 * image section becomes usable.
 */
export function ListingForm({ schema, existing, defaultPhone, onPublished }: ListingFormProps) {
  const [listingId, setListingId] = useState<string | null>(existing?.id ?? null)
  const [media, setMedia] = useState<ListingMedia[]>(existing?.media ?? [])
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [ageConfirmed, setAgeConfirmed] = useState(existing?.ageConfirmed ?? false)
  const [useStore, setUseStore] = useState(existing?.sellerType === 'Store')

  const [form, setForm] = useState<FormState>({
    title: existing?.title ?? '',
    description: existing?.description ?? '',
    price: existing?.price === null || existing?.price === undefined ? '' : String(existing.price),
    condition: existing?.condition ?? 'Used',
    brand: existing?.brand ?? '',
    hasDelivery: existing?.hasDelivery ?? false,
    regionSlug: existing?.regionSlug ?? '',
    contactPhone: existing?.contactPhone ?? defaultPhone,
    showPhone: existing?.showPhone ?? true,
    attributes: (existing?.attributes as Record<string, unknown>) ?? {},
  })

  const store = useQuery({
    queryKey: storeKeys.mine,
    queryFn: getMyStore,
    retry: false,
    // Only offered while the listing has not been filed yet: the store link is fixed at creation.
    enabled: listingId === null,
  })

  /**
   * Offered only to an owner whose storefront is actually live, and only before the listing exists:
   * the store link is fixed at creation, so once a draft is saved the choice is no longer on offer.
   * The server enforces both rules independently.
   */
  const storeAvailable = listingId === null && store.data?.status === 'Active'

  /** Editing a live listing narrows to what Tap.az allows: description, price, delivery, images. */
  const publishedEdit = existing !== undefined && existing.status === 'Active'
  const needsAgeConfirmation = schema.category.requiresAgeConfirmation && !ageConfirmed

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }))
  }

  function setAttribute(key: string, value: unknown) {
    setForm((current) => {
      const next = { ...current.attributes }

      if (value === undefined || value === '') {
        delete next[key]
      } else {
        next[key] = value
      }

      return { ...current, attributes: next }
    })
  }

  function body() {
    return {
      regionSlug: form.regionSlug,
      title: form.title.trim(),
      description: form.description.trim(),
      // An empty price field is "Razılaşma ilə", not zero.
      price: form.price.trim() === '' ? null : Number(form.price),
      condition: form.condition,
      brand: form.brand.trim() === '' ? null : form.brand.trim(),
      hasDelivery: form.hasDelivery,
      contactPhone: form.contactPhone,
      showPhone: form.showPhone,
      attributes: Object.keys(form.attributes).length === 0 ? null : form.attributes,
    }
  }

  function applyProblem(caught: unknown, fallback: string) {
    if (caught instanceof ApiError) {
      const fields = caught.fieldErrors
      const flat: Record<string, string> = {}

      for (const [key, value] of Object.entries(fields)) {
        const first = value[0]
        if (first !== undefined) {
          flat[key] = first
        }
      }

      setErrors(flat)
      setMessage(Object.keys(flat).length > 0 ? null : (caught.problem?.detail ?? fallback))
      return
    }

    setMessage(fallback)
  }

  /** Saves the draft and returns its id, creating it on the first call. */
  async function saveDraft(): Promise<string | null> {
    setErrors({})
    setMessage(null)
    setBusy(true)

    try {
      const saved = listingId
        ? await updateListing(listingId, body())
        : await createListing({
            ...body(),
            categorySlug: schema.category.slug,
            // Only sent on creation. SellerType is derived by the server from the outcome.
            ...(storeAvailable && useStore ? { useStore: true } : {}),
          })

      setListingId(saved.id)
      setMedia(saved.media.length > 0 ? saved.media : media)

      return saved.id
    } catch (caught) {
      applyProblem(caught, 'Elanı yadda saxlamaq mümkün olmadı.')
      return null
    } finally {
      setBusy(false)
    }
  }

  async function handleSaveDraft() {
    const id = await saveDraft()

    if (id) {
      setMessage('Qaralama yadda saxlanıldı. İndi şəkil əlavə edə bilərsiniz.')
    }
  }

  async function handlePublish() {
    const id = await saveDraft()

    if (!id) {
      return
    }

    if (media.length === 0) {
      setErrors({ media: 'Ən azı bir şəkil əlavə edin.' })
      return
    }

    setBusy(true)

    try {
      onPublished(await publishListing(id, ageConfirmed))
    } catch (caught) {
      applyProblem(caught, 'Elanı dərcə göndərmək mümkün olmadı.')
    } finally {
      setBusy(false)
    }
  }

  const attributes = orderedAttributes(schema.attributes)

  return (
    <form
      className="flex flex-col gap-8"
      onSubmit={(event) => {
        event.preventDefault()
        void handlePublish()
      }}
    >
      <Section title="Əsas məlumat">
        <Input
          label="Başlıq *"
          value={form.title}
          maxLength={70}
          onChange={(event) => set('title', event.target.value)}
          disabled={publishedEdit}
          hint={publishedEdit ? 'Dərc olunmuş elanda başlıq dəyişdirilə bilməz.' : undefined}
          error={errors['title']}
          required
        />

        <Textarea
          label="Təsvir *"
          value={form.description}
          maxLength={3000}
          counter
          onChange={(event) => set('description', event.target.value)}
          error={errors['description']}
          required
        />
      </Section>

      {attributes.length > 0 ? (
        <Section title="Xüsusiyyətlər">
          {attributes.map((attribute) => (
            <AttributeField
              key={attribute.key}
              attribute={attribute}
              value={form.attributes[attribute.key]}
              onChange={setAttribute}
              error={errors[`attributes.${attribute.key}`]}
            />
          ))}
        </Section>
      ) : null}

      <Section title="Qiymət və vəziyyət">
        <Input
          type="number"
          inputMode="decimal"
          min={0}
          step="0.01"
          label="Qiymət, ₼"
          value={form.price}
          onChange={(event) => set('price', event.target.value)}
          error={errors['price']}
          hint={`Boş buraxsanız "${formatPrice(null)}" göstəriləcək. 0 yazsanız "${formatPrice(0)}".`}
        />

        <Select
          label="Vəziyyət"
          value={form.condition}
          onChange={(event) => set('condition', event.target.value as 'New' | 'Used')}
          disabled={publishedEdit}
          error={errors['condition']}
        >
          <option value="Used">İşlənmiş</option>
          <option value="New">Yeni</option>
        </Select>

        <Input
          label="Marka"
          value={form.brand}
          maxLength={60}
          onChange={(event) => set('brand', event.target.value)}
          disabled={publishedEdit}
          error={errors['brand']}
        />

        <Checkbox
          label="Çatdırılma var"
          checked={form.hasDelivery}
          onChange={(event) => set('hasDelivery', event.target.checked)}
        />
      </Section>

      <Section title="Şəkillər">
        {listingId ? (
          <ImageUploader listingId={listingId} media={media} onChange={setMedia} />
        ) : (
          <p className="text-sm text-muted">
            Şəkil əlavə etmək üçün əvvəlcə qaralamanı yadda saxlayın.
          </p>
        )}

        {errors['media'] ? (
          <p role="alert" className="text-sm text-accent">
            {errors['media']}
          </p>
        ) : null}
      </Section>

      {storeAvailable && store.data ? (
        <Section title="Satıcı">
          <Checkbox
            label={`Elan "${store.data.name}" mağazasında yerləşdirilsin`}
            checked={useStore}
            onChange={(event) => setUseStore(event.target.checked)}
            error={errors['useStore']}
            hint="Mağaza bağlantısı elan yaradılarkən müəyyən olunur və sonradan dəyişmir."
          />
        </Section>
      ) : existing?.sellerType === 'Store' ? (
        <Section title="Satıcı">
          <p className="text-sm text-muted">Bu elan mağazanıza bağlıdır.</p>
        </Section>
      ) : null}

      <Section title="Yer və əlaqə">
        <RegionSelect
          value={form.regionSlug}
          onChange={(slug) => set('regionSlug', slug)}
          required
        />

        {errors['regionSlug'] ? (
          <p role="alert" className="text-sm text-accent">
            {errors['regionSlug']}
          </p>
        ) : null}

        <Input
          label="Əlaqə nömrəsi *"
          value={form.contactPhone}
          onChange={(event) => set('contactPhone', event.target.value)}
          disabled={publishedEdit}
          error={errors['contactPhone']}
          required
        />

        <Checkbox
          label="Nömrə elanda göstərilsin"
          checked={form.showPhone}
          onChange={(event) => set('showPhone', event.target.checked)}
          hint="Bağlasanız alıcılar yalnız mesaj göndərə biləcək."
        />
      </Section>

      {schema.category.requiresAgeConfirmation ? (
        <Section title="Təsdiq">
          <Checkbox
            label="18 yaşım tamam olub və bu kateqoriyanın qaydaları ilə tanışam."
            checked={ageConfirmed}
            onChange={(event) => setAgeConfirmed(event.target.checked)}
            error={errors['ageConfirmed']}
          />

          {schema.category.restrictionStatus === 'Unclassified' ? (
            <p className="text-sm text-muted">
              Bu kateqoriya üçün təsnifat gözlənilir, ona görə elan əlavə yoxlamadan keçəcək.
            </p>
          ) : null}
        </Section>
      ) : null}

      {message ? (
        <p role="status" className="text-sm text-muted">
          {message}
        </p>
      ) : null}

      <div className="flex flex-wrap gap-3">
        <Button type="submit" variant="accent" size="lg" disabled={busy || needsAgeConfirmation}>
          {busy ? 'Göndərilir…' : 'Elanı yerləşdir'}
        </Button>

        <Button type="button" variant="secondary" size="lg" disabled={busy} onClick={() => void handleSaveDraft()}>
          Qaralamanı yadda saxla
        </Button>
      </div>

      <p className="text-sm text-muted">
        Elan yerləşdirildikdən sonra moderasiyadan keçir və təsdiqlənəndən sonra saytda görünür.
      </p>
    </form>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-4 rounded-(--radius-card) border border-line bg-surface p-4 sm:p-5">
      <h2 className="text-base font-semibold text-ink">{title}</h2>
      {children}
    </section>
  )
}
