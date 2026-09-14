/**
 * Where a category picture lives once an administrator sets one.
 *
 * The API hands back the storage key rather than a URL — unlike a listing, which is resolved
 * server-side — so the public path is composed here. It matches Storage:Local:PublicBaseUrl, which
 * is a relative `/uploads` in every environment. An absolute URL is passed through untouched, so a
 * key that already points somewhere else keeps working.
 */
export function categoryImageUrl(imageKey: string): string {
  return /^https?:\/\//i.test(imageKey) ? imageKey : `/uploads/${imageKey.replace(/^\/+/, '')}`
}
