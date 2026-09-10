import { useState } from 'react'
import { useSearchParams } from 'react-router'

import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'

import { CataloguePage } from './CataloguePage'

/**
 * Search is the catalogue with a term in the URL, so this page only owns the box. Everything below
 * it — filters, facets, sorting, paging — is the same screen a category browse renders.
 */
export function SearchPage() {
  const [params, setParams] = useSearchParams()
  const [term, setTerm] = useState(params.get('q') ?? '')

  return (
    <div className="flex flex-col gap-5">
      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault()
          const merged = new URLSearchParams(params)

          if (term.trim() === '') {
            merged.delete('q')
          } else {
            merged.set('q', term.trim())
          }

          merged.delete('page')
          setParams(merged)
        }}
      >
        <div className="min-w-64 flex-1">
          <Input
            label="Axtarış"
            value={term}
            placeholder="Məsələn: çadır, spinning, durbin"
            onChange={(event) => setTerm(event.target.value)}
          />
        </div>

        <Button type="submit">Axtar</Button>
      </form>

      <CataloguePage />
    </div>
  )
}
