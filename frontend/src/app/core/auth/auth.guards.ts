import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/*
 * los guards solo deciden que pantalla ver y la seguridad real esta en la API (denegar por defecto y datos filtrados
 * por negocio y cuentas con contrasena temporal bloqueadas) - quien se salte un guard igual recibe 401/403/404
 */

/** panel del negocio (duenos y cajeros que ya cambiaron su contrasena temporal) */
export const tenantGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  return auth.isTenantMember() && !auth.mustChangePassword() ? true : router.parseUrl(auth.homeUrl());
};

/** pantallas que solo usa el dueno (configuracion y usuarios) - Va detras de tenantGuard */
export const ownerGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isOwner() ? true : inject(Router).parseUrl('/app');
};

/** panel de la plataforma (solo el superadmin) */
export const superAdminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  return auth.isSuperAdmin() && !auth.mustChangePassword() ? true : router.parseUrl(auth.homeUrl());
};

/** cambio de contrasena obligatorio (solo cuentas con contrasena temporal) */
export const passwordChangeGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return router.parseUrl('/login');
  return auth.mustChangePassword() ? true : router.parseUrl(auth.homeUrl());
};

/** login y registro solo tienen sentido sin sesion */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? inject(Router).parseUrl(auth.homeUrl()) : true;
};
