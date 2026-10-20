import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  LOCALE_ID,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling, withViewTransitions } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { errorInterceptor } from './core/http/error.interceptor';

registerLocaleData(localeEsCo);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // withViewTransitions hace un fundido suave entre paginas (los navegadores sin el API cambian al instante)
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
      withViewTransitions({ skipInitialTransition: true }),
    ),
    // authInterceptor va primero - asi errorInterceptor ve el resultado final (despues del reintento con token nuevo)
    provideHttpClient(withInterceptors([errorInterceptor, authInterceptor])),
    { provide: LOCALE_ID, useValue: 'es-CO' },
    // si hay cookie de refresh valida la sesion se recupera antes de decidir la primera ruta
    provideAppInitializer(() => inject(AuthService).restoreSession()),
  ],
};
