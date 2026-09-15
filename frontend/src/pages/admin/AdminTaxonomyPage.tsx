import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { ErrorState } from '@/components/ui/ErrorState'
import { Select } from '@/components/ui/Select'
import { Skeleton } from '@/components/ui/Skeleton'
import {
  adminKeys,
  deleteCategory,
  getAdminCategories,
  reorderCategories,
  setCategoryRestriction,
  removeCategoryImage,
  updateCategory,
  uploadCategoryImage,
} from '@/features/admin/api'
import type { CategoryEditBody } from '@/features/admin/api'
import { ConfirmDialog } from '@/features/admin/components/ActionDialog'
import { AttributeEditor } from '@/features/admin/components/AttributeEditor'
import { CategoryEditor } from '@/features/admin/components/CategoryEditor'
import { StatusBadge } from '@/features/admin/components/QueueChrome'
import type { AdminCategoryNode } from '@/features/admin/types'
import { useAdminAction } from '@/features/admin/useAdminAction'
import { DesktopOnlyNotice } from '@/layouts/AdminLayout'

type Tab = 'details' | 'attributes'

/**
 * Category administration.
 *
 * The tree comes from the admin endpoint, which unlike the public one includes deactivated
 * categories — otherwise a category can be switched off and then never seen or repaired again.
 * Restriction status is edited through its own endpoint, which audits separately, and behind an
 * explicit confirmation: it changes the age gate on the public site.
 */
export function AdminTaxonomyPage() {
  const queryClient = useQueryClient()
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [tab, setTab] = useState<Tab>('details')
  const [restrictionTarget, setRestrictionTarget] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<AdminCategoryNode | null>(null)

  const tree = useQuery({ queryKey: adminKeys.categories, queryFn: getAdminCategories })

  // Re-read from the freshly fetched tree, so an edit is reflected without extra state.
  const selected = useMemo(
    () => (selectedId === null ? null : find(tree.data ?? [], selectedId)),
    [tree.data, selectedId],
  )

  const invalidate = [adminKeys.categories]

  /**
   * After a picture changes: the admin tree so the editor's own preview and the row are current,
   * and the public catalogue queries so the homepage tiles show it without a reload.
   */
  async function refreshCatalogue() {
    await queryClient.invalidateQueries({ queryKey: adminKeys.categories })
    await queryClient.invalidateQueries({ queryKey: ['catalog'] })
  }

  const save = useAdminAction<{ id: number; body: CategoryEditBody }>({
    action: ({ id, body }) => updateCategory(id, body),
    invalidate,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['catalog'] }),
  })

  const restrict = useAdminAction<{ id: number; status: string }>({
    action: ({ id, status }) => setCategoryRestriction(id, status),
    invalidate,
    onSuccess: () => {
      setRestrictionTarget(null)
      void queryClient.invalidateQueries({ queryKey: ['catalog'] })
    },
  })

  const reorder = useAdminAction<{ id: number; sortOrder: number }[]>({
    action: (items) => reorderCategories(items),
    invalidate,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['catalog'] }),
  })

  const remove = useAdminAction<number>({
    action: (id) => deleteCategory(id),
    invalidate,
    onSuccess: () => {
      setDeleting(null)
      setSelectedId(null)
    },
  })

  /**
   * Moves a category within its own sibling group and persists the whole group. Sending the entire
   * group is what the endpoint expects and what keeps the ordering consistent when two positions
   * swap.
   */
  function move(node: AdminCategoryNode, direction: -1 | 1) {
    const siblings = siblingsOf(tree.data ?? [], node.parentId)
    const from = siblings.findIndex((c) => c.id === node.id)
    const to = from + direction

    if (from < 0 || to < 0 || to >= siblings.length) {
      return
    }

    const ordered = [...siblings]
    const [moved] = ordered.splice(from, 1)
    ordered.splice(to, 0, moved!)

    reorder.run(ordered.map((c, index) => ({ id: c.id, sortOrder: (index + 1) * 10 })))
  }

  if (tree.isPending) {
    return <Skeleton className="h-96" />
  }

  if (tree.isError || !tree.data) {
    return <ErrorState description="Kateqoriyaları yükləmək mümkün olmadı." onRetry={() => void tree.refetch()} />
  }

  const failure = [save, restrict, reorder, remove].find((a) => a.state.kind === 'error')

  return (
    <DesktopOnlyNotice>
      <div className="flex flex-col gap-4">
        <header className="flex flex-col gap-1">
          <h1 className="text-xl font-semibold text-ink">Kateqoriyalar</h1>
          <p className="text-sm text-muted">Ağac deaktiv kateqoriyaları da göstərir.</p>
        </header>

        {failure?.state.kind === 'error' ? (
          <p role="alert" className="text-sm text-accent">
            {failure.state.message}
          </p>
        ) : null}

        <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,28rem)]">
          <div className="rounded-(--radius-card) border border-line bg-surface p-2">
            <ul className="flex flex-col">
              {tree.data.map((node) => (
                <CategoryBranch
                  key={node.id}
                  node={node}
                  depth={0}
                  selectedId={selectedId}
                  busy={reorder.isPending}
                  onSelect={(c) => setSelectedId(c.id)}
                  onMove={move}
                />
              ))}
            </ul>
          </div>

          {selected ? (
            <aside className="flex flex-col gap-4 rounded-(--radius-card) border border-line bg-surface p-4">
              <div className="flex flex-col gap-1">
                <h2 className="text-base font-semibold text-ink">{selected.nameAz}</h2>
                <p className="text-xs text-muted">
                  /{selected.slug} · {selected.listingCount} elan · {selected.attributeCount} xüsusiyyət
                </p>
              </div>

              <div role="tablist" className="flex gap-1 border-b border-line">
                <TabButton active={tab === 'details'} onClick={() => setTab('details')}>
                  Məlumat
                </TabButton>
                <TabButton active={tab === 'attributes'} onClick={() => setTab('attributes')}>
                  Xüsusiyyətlər
                </TabButton>
              </div>

              {tab === 'details' ? (
                <>
                  <CategoryEditor
                    key={selected.id}
                    category={selected}
                    busy={save.isPending}
                    error={save.fieldError}
                    onSave={(body) => save.run({ id: selected.id, body })}
                    onUploadImage={async (file) => {
                      const updated = await uploadCategoryImage(selected.id, file)
                      await refreshCatalogue()

                      return updated.imageKey
                    }}
                    onRemoveImage={async () => {
                      await removeCategoryImage(selected.id)
                      await refreshCatalogue()
                    }}
                  />

                  <div className="flex flex-col gap-2 border-t border-line pt-3">
                    <Select
                      label="Təsnifat"
                      value={selected.restrictionStatus}
                      onChange={(event) => setRestrictionTarget(event.target.value)}
                    >
                      <option value="Unrestricted">Sərbəst</option>
                      <option value="Restricted">Məhdud</option>
                      <option value="Unclassified">Təsnif edilməyib</option>
                    </Select>

                    <p className="text-xs text-muted">
                      Təsnifat saytdakı yaş təsdiqini müəyyən edir və ayrıca audit edilir.
                    </p>
                  </div>

                  <div className="border-t border-line pt-3">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      disabled={selected.listingCount > 0}
                      onClick={() => setDeleting(selected)}
                    >
                      Kateqoriyanı sil
                    </Button>

                    {selected.listingCount > 0 ? (
                      <p className="mt-1 text-xs text-muted">Elanı olan kateqoriya silinmir.</p>
                    ) : null}
                  </div>
                </>
              ) : (
                <AttributeEditor categoryId={selected.id} isLeaf={selected.isLeaf} />
              )}
            </aside>
          ) : (
            <aside className="rounded-(--radius-card) border border-dashed border-line px-4 py-10 text-center text-sm text-muted">
              Redaktə üçün ağacdan kateqoriya seçin.
            </aside>
          )}
        </div>

        {restrictionTarget && selected ? (
          <ConfirmDialog
            title="Təsnifatı dəyiş"
            description={`"${selected.nameAz}" kateqoriyasının təsnifatı dəyişir. Bu, saytda yaş təsdiqi tələbinə təsir edir və ayrıca audit edilir.`}
            confirmLabel="Dəyiş"
            busy={restrict.isPending}
            onConfirm={() => restrict.run({ id: selected.id, status: restrictionTarget })}
            onCancel={() => setRestrictionTarget(null)}
          />
        ) : null}

        {deleting ? (
          <ConfirmDialog
            title="Kateqoriyanı sil"
            description={`"${deleting.nameAz}" birdəfəlik silinir. Bu əməliyyat geri qaytarılmır.`}
            confirmLabel="Sil"
            busy={remove.isPending}
            onConfirm={() => remove.run(deleting.id)}
            onCancel={() => setDeleting(null)}
          />
        ) : null}
      </div>
    </DesktopOnlyNotice>
  )
}

function TabButton({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className={`-mb-px border-b-2 px-3 py-1.5 text-sm ${
        active ? 'border-interactive font-semibold text-interactive' : 'border-transparent text-muted'
      }`}
    >
      {children}
    </button>
  )
}

interface BranchProps {
  node: AdminCategoryNode
  depth: number
  selectedId: number | null
  busy: boolean
  onSelect: (node: AdminCategoryNode) => void
  onMove: (node: AdminCategoryNode, direction: -1 | 1) => void
}

function CategoryBranch({ node, depth, selectedId, busy, onSelect, onMove }: BranchProps) {
  return (
    <li>
      <div
        className={`flex items-center gap-1 rounded-(--radius-input) ${
          selectedId === node.id ? 'bg-interactive-soft' : 'hover:bg-canvas'
        }`}
      >
        <button
          type="button"
          onClick={() => onSelect(node)}
          // The visible content is the name plus badges and a bare count, which reads poorly; the
          // move buttons beside it also carry the name, so this keeps the three distinguishable.
          aria-label={`${node.nameAz} kateqoriyasını aç`}
          aria-current={selectedId === node.id ? 'true' : undefined}
          className={`flex flex-1 items-center gap-2 px-2 py-1.5 text-left text-sm ${
            selectedId === node.id ? 'text-interactive' : 'text-ink'
          }`}
          style={{ paddingLeft: `${depth * 1.25 + 0.5}rem` }}
        >
          <span className="flex-1 truncate">{node.nameAz}</span>

          {!node.isActive ? <Badge>Deaktiv</Badge> : null}
          {node.restrictionStatus !== 'Unrestricted' ? <StatusBadge status={node.restrictionStatus} /> : null}
          <span className="text-xs text-faint">{node.listingCount}</span>
        </button>

        {/* Buttons rather than dragging: the same pattern the image uploader uses, and it works
            with a keyboard without a dependency. */}
        <span className="flex shrink-0 gap-0.5 pr-1">
          <button
            type="button"
            aria-label={`${node.nameAz} yuxarı`}
            disabled={busy}
            onClick={() => onMove(node, -1)}
            className="rounded-sm px-1 text-sm text-muted hover:text-ink disabled:opacity-40"
          >
            ↑
          </button>
          <button
            type="button"
            aria-label={`${node.nameAz} aşağı`}
            disabled={busy}
            onClick={() => onMove(node, 1)}
            className="rounded-sm px-1 text-sm text-muted hover:text-ink disabled:opacity-40"
          >
            ↓
          </button>
        </span>
      </div>

      {node.children.length > 0 ? (
        <ul className="flex flex-col">
          {node.children.map((child) => (
            <CategoryBranch
              key={child.id}
              node={child}
              depth={depth + 1}
              selectedId={selectedId}
              busy={busy}
              onSelect={onSelect}
              onMove={onMove}
            />
          ))}
        </ul>
      ) : null}
    </li>
  )
}

function find(nodes: AdminCategoryNode[], id: number): AdminCategoryNode | null {
  for (const node of nodes) {
    if (node.id === id) {
      return node
    }

    const match = find(node.children, id)

    if (match) {
      return match
    }
  }

  return null
}

/** A category's own sibling group, which is the unit the reorder endpoint takes. */
function siblingsOf(nodes: AdminCategoryNode[], parentId: number | null): AdminCategoryNode[] {
  if (parentId === null) {
    return nodes
  }

  const parent = find(nodes, parentId)

  return parent?.children ?? []
}
