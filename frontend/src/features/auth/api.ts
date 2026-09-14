import { api } from '@/api/client'

import type { AuthResponse, OtpPurposeValue, OtpRequestResponse, User } from './types'

export const authKeys = {
  me: ['auth', 'me'] as const,
}

export function register(phoneNumber: string, fullName: string): Promise<OtpRequestResponse> {
  return api.post<OtpRequestResponse>('/auth/register', { phoneNumber, fullName })
}

export function requestLoginCode(phoneNumber: string): Promise<OtpRequestResponse> {
  return api.post<OtpRequestResponse>('/auth/login', { phoneNumber })
}

/**
 * The administrator's only door. An Admin account is excluded from the SMS flow server-side, so
 * there is no code step here and none to fall back on.
 */
export function adminLogin(phoneNumber: string, password: string): Promise<AuthResponse> {
  return api.post<AuthResponse>('/auth/admin/login', { phoneNumber, password })
}

/** Ends every session on success, this one included — the caller signs in again afterwards. */
export function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  return api.post<void>('/users/me/password', { currentPassword, newPassword })
}

export function resendCode(phoneNumber: string, purpose: OtpPurposeValue): Promise<OtpRequestResponse> {
  return api.post<OtpRequestResponse>('/auth/otp/resend', { phoneNumber, purpose })
}

export function verifyCode(
  phoneNumber: string,
  code: string,
  purpose: OtpPurposeValue,
): Promise<AuthResponse> {
  return api.post<AuthResponse>('/auth/verify', { phoneNumber, code, purpose })
}

// Session restore deliberately lives in the API client rather than here: it has to share one
// in-flight promise with the refresh that recovers from an expired access token, because a second
// overlapping call presents an already-rotated token and the API revokes the whole session family.

export function logout(): Promise<void> {
  return api.post<void>('/auth/logout')
}

export function getMe(): Promise<User> {
  return api.get<User>('/users/me')
}

export function updateProfile(fullName: string, email: string | null): Promise<User> {
  return api.put<User>('/users/me', { fullName, email })
}
