/**
 * The access token lives in memory only — never localStorage — so an XSS payload cannot read a
 * long-lived credential. The refresh token is an HttpOnly cookie the browser handles for us, and
 * the session is restored on boot by calling the refresh endpoint.
 */
let accessToken: string | null = null

export function getAccessToken(): string | null {
  return accessToken
}

export function setAccessToken(token: string | null): void {
  accessToken = token
}
