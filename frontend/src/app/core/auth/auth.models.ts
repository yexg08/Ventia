export type Role = 'SuperAdmin' | 'Owner' | 'Cashier';

export interface TenantSummary {
  id: string;
  name: string;
}

export interface User {
  id: string;
  email: string;
  fullName: string;
  role: Role;
  /** null solo para el superadmin */
  tenant: TenantSummary | null;
  /** contrasena temporal (hasta cambiarla la cuenta solo puede ir a la pantalla de cambio) */
  mustChangePassword: boolean;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: User;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  businessName: string;
  fullName: string;
  email: string;
  password: string;
}
