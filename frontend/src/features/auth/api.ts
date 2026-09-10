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
