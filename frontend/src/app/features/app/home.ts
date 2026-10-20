import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroCheck, heroExclamationTriangle } from '@ng-icons/heroicons/outline';
import { AuthService } from '../../core/auth/auth.service';
import { API_BASE } from '../../core/http/api';
import { BusinessSettings, Category, Product, UserAccount } from './business.models';

interface Step {
  title: string;
  text: string;
  done: boolean;
  link: string | null;
  action: string;
}

@Component({
  selector: 'app-home',
  imports: [RouterLink, NgIcon],
  providers: [provideIcons({ heroCheck, heroExclamationTriangle })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-3xl">
      <h1 class="text-2xl font-extrabold tracking-tight sm:text-3xl">Hola, {{ firstName() }}</h1>
      <p class="mt-1 text-muted">
        {{ auth.isOwner() ? 'Prepara tu negocio para empezar a vender.' : 'Este es el panel de ' + (auth.tenant()?.name ?? 'tu negocio') + '.' }}
      </p>

      <p class="mt-6 flex items-start gap-2 rounded-xl border border-line bg-surface-2 px-4 py-3 text-sm text-muted">
        <ng-icon name="heroExclamationTriangle" size="18" class="mt-0.5 shrink-0 text-accent" />
        Ventia es una demo: los comprobantes que genere no tienen validez fiscal ante la DIAN.
      </p>

      @if (auth.isOwner()) {
        <section class="mt-8" aria-labelledby="checklist-title">
          <h2 id="checklist-title" class="text-lg font-bold">Primeros pasos</h2>
          <ol class="mt-4 space-y-3">
            @for (step of steps(); track step.title) {
              <li class="card flex items-center gap-4 p-4">
                <span
                  class="flex size-8 shrink-0 items-center justify-center rounded-full border text-sm font-bold"
                  [class]="step.done ? 'bg-brand border-transparent text-white' : 'border-line text-muted'"
                >
                  @if (step.done) {
                    <ng-icon name="heroCheck" size="16" />
                    <span class="sr-only">Hecho:</span>
                  } @else {
                    {{ $index + 1 }}
                  }
                </span>
                <div class="min-w-0 flex-1">
                  <p class="font-semibold" [class.text-muted]="step.done">{{ step.title }}</p>
                  <p class="text-sm text-muted">{{ step.text }}</p>
                </div>
                @if (step.link && !step.done) {
                  <a [routerLink]="step.link" class="btn-secondary shrink-0">{{ step.action }}</a>
                }
              </li>
            }
          </ol>
        </section>
      }
    </div>
  `,
})
export class Home {
  protected readonly auth = inject(AuthService);

  // solo el dueno ve los primeros pasos (los cajeros no pueden leer cuentas ni cambiar ajustes)
  private readonly business = httpResource<BusinessSettings>(() => (this.auth.isOwner() ? `${API_BASE}/business` : undefined));
  private readonly categories = httpResource<Category[]>(() => (this.auth.isOwner() ? `${API_BASE}/categories` : undefined));
  private readonly products = httpResource<Product[]>(() => (this.auth.isOwner() ? `${API_BASE}/products` : undefined));
  private readonly users = httpResource<UserAccount[]>(() => (this.auth.isOwner() ? `${API_BASE}/users` : undefined));

  protected readonly firstName = computed(() => this.auth.user()?.fullName.split(' ')[0] ?? '');

  protected readonly steps = computed<Step[]>(() => {
    const business = this.business.hasValue() ? this.business.value() : null;
    const categoryCount = this.categories.hasValue() ? this.categories.value().length : 0;
    const productCount = this.products.hasValue() ? this.products.value().length : 0;
    const cashierCount = this.users.hasValue() ? this.users.value().filter((u) => u.role === 'Cashier').length : 0;
    return [
      { title: 'Crea tu negocio', text: 'Ya tienes tu cuenta de dueño.', done: true, link: null, action: '' },
      {
        title: 'Completa los datos del negocio',
        text: 'NIT, dirección y teléfono: salen en los comprobantes.',
        done: !!business?.taxId && !!business?.address,
        link: '/app/configuracion',
        action: 'Completar',
      },
      {
        title: 'Organiza tu menú en categorías',
        text: categoryCount > 0 ? `Tienes ${categoryCount} categoría(s).` : 'Por ejemplo: hamburguesas, perros, bebidas y adiciones.',
        done: categoryCount > 0,
        link: '/app/categorias',
        action: 'Crear',
      },
      {
        title: 'Agrega tus productos',
        text: productCount > 0 ? `Tienes ${productCount} producto(s).` : 'Precio con impuesto incluido y, si quieres, el inventario.',
        done: productCount > 0,
        link: '/app/productos',
        action: 'Agregar',
      },
      {
        title: 'Crea las cuentas de tus cajeros',
        text: cashierCount > 0 ? `Tienes ${cashierCount} cajero(s).` : 'Cada cajero entra con su propia cuenta.',
        done: cashierCount > 0,
        link: '/app/usuarios',
        action: 'Agregar',
      },
    ];
  });
}
