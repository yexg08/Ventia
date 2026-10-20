import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroArrowRight, heroArchiveBox, heroChartBar, heroLockClosed, heroShoppingCart, heroUsers } from '@ng-icons/heroicons/outline';
import { AuthService } from '../../core/auth/auth.service';
import { Logo } from '../../shared/logo';
import { ThemeToggle } from '../../shared/theme-toggle';

@Component({
  selector: 'app-landing',
  imports: [RouterLink, NgIcon, Logo, ThemeToggle],
  providers: [provideIcons({ heroArrowRight, heroArchiveBox, heroChartBar, heroLockClosed, heroShoppingCart, heroUsers })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-dvh flex-col">
      <header class="mx-auto flex w-full max-w-6xl items-center justify-between px-4 py-5 sm:px-6">
        <app-logo />
        <nav class="flex items-center gap-1" aria-label="Cuenta">
          <app-theme-toggle />
          @if (auth.isAuthenticated()) {
            <a [routerLink]="auth.homeUrl()" class="btn-primary">Ir a mi panel</a>
          } @else {
            <a routerLink="/login" class="btn-ghost">Iniciar sesión</a>
            <a routerLink="/registro" class="btn-primary hidden sm:inline-flex">Crear negocio</a>
          }
        </nav>
      </header>

      <main class="flex-1">
        <section class="mx-auto max-w-3xl px-4 pt-16 pb-20 text-center sm:px-6 sm:pt-24">
          <p class="inline-flex rounded-full border border-line px-3 py-1 text-xs font-semibold text-muted">
            Demo de AndesAiSolutions · en construcción
          </p>
          <h1 class="mt-6 text-4xl font-extrabold tracking-tight text-balance sm:text-6xl">
            Vende, controla tu inventario y entiende tu negocio.
          </h1>
          <p class="mx-auto mt-5 max-w-xl text-lg text-pretty text-muted">
            Ventia es un sistema de ventas pensado para negocios de comida rápida: rápido en la caja y claro para el dueño.
          </p>
          <div class="mt-9 flex flex-col items-center justify-center gap-3 sm:flex-row">
            <a routerLink="/registro" class="btn-primary px-6 py-3">
              Probar la demo <ng-icon name="heroArrowRight" size="18" />
            </a>
            <a routerLink="/login" class="btn-secondary px-6 py-3">Ya tengo cuenta</a>
          </div>
        </section>

        <section class="mx-auto max-w-6xl px-4 pb-24 sm:px-6" aria-labelledby="features-title">
          <h2 id="features-title" class="sr-only">Qué incluye</h2>
          <ul class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            @for (feature of features; track feature.title) {
              <li class="card p-6">
                <ng-icon [name]="feature.icon" size="24" class="text-accent" />
                <h3 class="mt-4 font-bold">{{ feature.title }}</h3>
                <p class="mt-1.5 text-sm text-muted">{{ feature.text }}</p>
              </li>
            }
          </ul>
          <p class="mt-8 text-center text-xs text-faint">
            Es una demo: los comprobantes no tienen validez fiscal ante la DIAN y no se procesan pagos reales.
          </p>
        </section>
      </main>
    </div>
  `,
})
export class Landing {
  protected readonly auth = inject(AuthService);

  // lo que Ventia hace o hara al terminar sus fases (la demo esta en construccion y asi lo dice la portada)
  protected readonly features = [
    { icon: 'heroShoppingCart', title: 'Punto de venta ágil', text: 'Menú por categorías, notas por producto, pagos en efectivo, tarjeta o transferencia.' },
    { icon: 'heroArchiveBox', title: 'Inventario sin sorpresas', text: 'Cada venta descuenta el stock y el sistema nunca deja vender lo que no hay.' },
    { icon: 'heroChartBar', title: 'Reportes claros', text: 'Ventas por día, producto y medio de pago, y el cierre de caja de cada turno.' },
    { icon: 'heroUsers', title: 'Cuentas para tu equipo', text: 'Cada cajero entra con su cuenta y no ve costos ni márgenes.' },
    { icon: 'heroLockClosed', title: 'Tus datos son solo tuyos', text: 'Cada negocio ve únicamente su información, nunca la de otro.' },
  ];
}
