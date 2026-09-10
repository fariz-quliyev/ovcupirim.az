export type UserRole = 'User' | 'Moderator' | 'Admin'

/** Mirrors OtpPurpose on the API. */
export const OtpPurpose = {
  Registration: 0,
  Login: 1,
  PhoneChange: 2,
  PasswordReset: 3,
} as const

export type OtpPurposeValue = (typeof OtpPurpose)[keyof typeof OtpPurpose]

export interface User {
  id: string
  phoneNumber: string
  fullName: string
  email: string | null
  role: UserRole
  isPhoneVerified: boolean
  createdAt: string
}

export interface AuthResponse {
  accessToken: string
  expiresInSeconds: number
  user: User
}

export interface OtpRequestResponse {
  message: string
  resendAfterSeconds: number
  codeLength: number
}
