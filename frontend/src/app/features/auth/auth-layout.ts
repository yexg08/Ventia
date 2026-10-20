import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroCheckCircle } from '@ng-icons/heroicons/outline';
import { Logo } from '../../shared/logo';
import { ThemeToggle } from '../../shared/theme-toggle';

/** marco comun de login y registro (panel de la marca a la izquierda en pantallas grandes y el formulario a la derecha) */
@Component({
  selector: 'app-auth-layout',
  imports: [RouterLink, Logo, ThemeToggle, NgIcon],
  providers: [provideIcons({ heroCheckCircle })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="min-h-dvh lg:grid lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
      <!-- panel de la marca -->
      <aside class="bg-brand-gradient relative hidden overflow-hidden p-10 text-white lg:flex lg:flex-col" aria-label="Ventia">
        <div class="pointer-events-none absolute -top-32 -left-32 size-96 rounded-full bg-white/10 blur-3xl" aria-hidden="true"></div>
        <div class="pointer-events-none absolute -right-24 -bottom-24 size-80 rounded-full bg-black/15 blur-3xl" aria-hidden="true"></div>
        <a routerLink="/" class="relative inline-flex w-fit items-center gap-2.5 text-xl font-extrabold tracking-tight" aria-label="Ventia, inicio">
          <app-logo [withText]="false" />
          Ventia
        </a>
        <div class="stagger relative mt-auto max-w-md">
          <p class="text-3xl leading-tight font-extrabold tracking-tight text-balance">La caja rápida y las cuentas claras de tu negocio.</p>
          <ul class="mt-8 space-y-3 text-sm text-white/90">
            @for (point of points; track point) {
              <li class="flex items-center gap-3"><ng-icon name="heroCheckCircle" size="19" class="shrink-0" />{{ point }}</li>
            }
          </ul>
        </div>
        <p class="relative mt-auto pt-10 text-xs text-white/75">Demo de AndesAiSolutions · sin validez fiscal</p>
      </aside>

      <!-- formulario -->
      <div class="flex min-h-dvh flex-col">
        <header class="flex items-center justify-between px-4 py-5 sm:px-8">
          <a routerLink="/" aria-label="Ventia, inicio" class="lg:invisible"><app-logo /></a>
          <app-theme-toggle />
        </header>
        <main class="flex flex-1 items-start justify-center px-4 pt-4 pb-16 sm:items-center sm:pt-0">
          <div class="animate-enter w-full" [class]="wide() ? 'max-w-xl' : 'max-w-md'">
            <h1 class="text-3xl font-extrabold tracking-tight">{{ heading() }}</h1>
            <p class="mt-2 text-sm text-muted">{{ subheading() }}</p>
            <div class="card mt-7 p-6 sm:p-7"><ng-content /></div>
          </div>
        </main>
      </div>
    </div>
  `,
})
export class AuthLayout {
  readonly heading = input.required<string>();
  readonly subheading = input.required<string>();
  readonly wide = input(false);

  protected readonly points = [
    'Vende con notas, adiciones y pago mixto',
    'Inventario que se descuenta solo',
    'Reportes con la utilidad real',
    'Cada cajero con su propia cuenta',
  ];
}
