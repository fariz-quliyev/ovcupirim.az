import { useState } from 'react'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Textarea } from '@/components/ui/Textarea'

import { applyForStore, updateMyStore } from './api'
import type { StoreOwner } from './types'

interface StoreFormProps {
  /** Absent while applying; present when an owner edits an existing storefront. */
  existing?: StoreOwner
  onSaved: (store: StoreOwner) => void
}

/**
 * Applying and editing are the same four fields. The slug is not among them: it is derived from
 * the name once, at creation, and never moves afterwards — so a rename keeps the storefront's URL.
 */
export function StoreForm({ existing, onSaved }: StoreFormProps) {
  const [name, setName] = useState(existing?.name ?? '')
  const [description, setDescription] = useState(existing?.description ?? '')
  const [address, setAddress] = useState(existing?.address ?? '')
  const [phone, setPhone] = useState(existing?.phone ?? '')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  function trimmed(value: string): string | null {
    return value.trim() === '' ? null : value.trim()
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    setErrors({})
    setMessage(null)
    setBusy(true)

    const body = {
      name: name.trim(),
      description: trimmed(description),
      address: trimmed(address),
      phone: trimmed(phone),
    }

    try {
      onSaved(existing ? await updateMyStore(body) : await applyForStore(body))
      setMessage(existing ? 'Məlumatlar yadda saxlanıldı.' : null)
    } catch (caught) {
      if (caught instanceof ApiError) {
        const flat: Record<string, string> = {}

        for (const [key, value] of Object.entries(caught.fieldErrors)) {
          const first = value[0]
          if (first !== undefined) {
            flat[key] = first
          }
        }

        setErrors(flat)
        setMessage(
          Object.keys(flat).length > 0
            ? null
            : (caught.problem?.detail ?? 'Məlumatları yadda saxlamaq mümkün olmadı.'),
        )
      } else {
        setMessage('Şəbəkə xətası. Yenidən cəhd edin.')
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
      <Input
        label="Mağazanın adı *"
        value={name}
        maxLength={100}
        onChange={(event) => setName(event.target.value)}
        error={errors['name']}
        hint={
          existing
            ? `Ünvan dəyişmir: ovcupirim.az/magaza/${existing.slug}`
            : 'Mağazanın ünvanı bu addan yaradılır və sonradan dəyişmir.'
        }
        required
      />

      <Textarea
        label="Haqqında"
        value={description}
        maxLength={2000}
        counter
        onChange={(event) => setDescription(event.target.value)}
        error={errors['description']}
      />

      <Input
        label="Ünvan"
        value={address}
        maxLength={200}
        onChange={(event) => setAddress(event.target.value)}
        error={errors['address']}
      />

      <Input
        label="Mağaza nömrəsi"
        value={phone}
        onChange={(event) => setPhone(event.target.value)}
        error={errors['phone']}
        hint="Alıcılara yalnız bu nömrə göstərilir, şəxsi nömrəniz yox."
      />

      {message ? (
        <p role="status" className="text-sm text-muted">
          {message}
        </p>
      ) : null}

      <div>
        <Button type="submit" disabled={busy || name.trim() === ''}>
          {busy ? 'Göndərilir…' : existing ? 'Yadda saxla' : 'Müraciət göndər'}
        </Button>
      </div>
    </form>
  )
}
