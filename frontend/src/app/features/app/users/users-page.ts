import { Dialog } from '@angular/cdk/dialog';
import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroKey, heroNoSymbol, heroPlus, heroUserPlus } from '@ng-icons/heroicons/outline';
import { filter, switchMap } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE } from '../../../core/http/api';
import { ToastService } from '../../../core/notifications/toast.service';
import { ConfirmService, DIALOG_DEFAULTS } from '../../../shared/confirm';
import { formatDate } from '../../../shared/format';
import { TemporaryPassword, UserAccount } from '../business.models';
import { NewCashierDialog, TemporaryPasswordData, TemporaryPasswordDialog } from './user-dialogs';

@Component({
  selector: 'app-users-page',
  imports: [NgIcon],
  providers: [provideIcons({ heroKey, heroNoSymbol, heroPlus, heroUserPlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-extrabold tracking-tight sm:text-3xl">Usuarios</h1>
          <p class="mt-1 text-muted">Cada cajero entra con su propia cuenta. Las ventas quedarán a su nombre.</p>
        </div>
        <button type="button" class="btn-primary" (click)="create()">
          <ng-icon name="heroPlus" size="18" /> Nuevo cajero
        </button>
      </div>

      @if (users.isLoading() && users.value().length === 0) {
        <div class="card mt-8 h-48 animate-pulse bg-surface-2" aria-label="Cargando usuarios"></div>
      } @else if (users.error()) {
        <div class="card mt-8 p-8 text-center">
          <p class="font-semibold">No se pudieron cargar los usuarios.</p>
          <button type="button" class="btn-secondary mt-4" (click)="users.reload()">Reintentar</button>
        </div>
      } @else {
        <ul class="card mt-8 divide-y divide-line">
          @for (user of users.value(); track user.id) {
            <li class="flex flex-wrap items-center gap-4 px-5 py-4" [class.opacity-70]="!user.isActive">
              <div class="flex size-10 shrink-0 items-center justify-center rounded-full bg-surface-2 text-sm font-bold" aria-hidden="true">
                {{ user.fullName.charAt(0) }}
              </div>
              <div class="min-w-0 flex-1">
                <p class="flex flex-wrap items-center gap-2 font-semibold">
                  <span class="truncate">{{ user.fullName }}</span>
                  <span class="rounded-full border border-line px-2 py-0.5 text-xs font-medium text-muted">
                    {{ user.role === 'Owner' ? 'Dueño' : 'Cajero' }}
                  </span>
                  @if (!user.isActive) {
                    <span class="rounded-full bg-danger/15 px-2 py-0.5 text-xs font-semibold text-danger">Desactivada</span>
                  } @else if (user.pendingFirstLogin) {
                    <span class="rounded-full bg-surface-2 px-2 py-0.5 text-xs font-semibold text-accent">Sin primer ingreso</span>
                  }
                </p>
                <p class="truncate text-sm text-muted">{{ user.email }}</p>
                <p class="text-xs text-faint">
                  @if (user.pendingFirstLogin && user.temporaryPasswordExpiresAt) {
                    La contraseña temporal vence el {{ formatDate(user.temporaryPasswordExpiresAt) }}
                  } @else {
                    Último ingreso: {{ formatDate(user.lastLoginAt) }}
                  }
                </p>
              </div>
              @if (user.role === 'Cashier') {
                <div class="flex shrink-0 gap-1">
                  <button type="button" class="btn-ghost" [disabled]="busyId() === user.id" (click)="resetPassword(user)">
                    <ng-icon name="heroKey" size="17" /> <span class="hidden sm:inline">Nueva contraseña</span>
                    <span class="sr-only sm:hidden">Nueva contraseña para {{ user.fullName }}</span>
                  </button>
                  <button
                    type="button"
                    [class]="user.isActive ? 'btn-ghost hover:text-danger' : 'btn-secondary'"
                    [disabled]="busyId() === user.id"
                    (click)="toggle(user)"
                  >
                    @if (user.isActive) {
                      <ng-icon name="heroNoSymbol" size="17" /> Desactivar
                    } @else {
                      Reactivar
                    }
                  </button>
                </div>
              }
            </li>
          }
        </ul>

        @if (users.value().length === 1) {
          <div class="mt-4 flex items-center gap-3 rounded-xl border border-dashed border-line px-5 py-4 text-sm text-muted">
            <ng-icon name="heroUserPlus" size="20" class="shrink-0 text-faint" />
            Aún no tienes cajeros. Si atiendes tú solo, no necesitas crear ninguno.
          </div>
        }
      }
    </div>
  `,
})
export class UsersPage {
  private readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  protected readonly users = httpResource<UserAccount[]>(() => `${API_BASE}/users`, { defaultValue: [] });
  protected readonly busyId = signal<string | null>(null);
  protected readonly formatDate = formatDate;

  protected create(): void {
    this.dialog
      .open<{ name: string; result: TemporaryPassword }>(NewCashierDialog, DIALOG_DEFAULTS)
      .closed.pipe(filter((created) => !!created))
      .subscribe((created) => {
        this.users.reload();
        this.showPassword(created!.name, created!.result);
      });
  }

  protected resetPassword(user: UserAccount): void {
    this.confirm
      .ask({
        title: `¿Nueva contraseña para ${user.fullName}?`,
        message: 'La contraseña actual dejará de servir y sus sesiones abiertas se cerrarán de inmediato.',
        confirmText: 'Generar contraseña',
      })
      .pipe(
        filter(Boolean),
        switchMap(() => {
          this.busyId.set(user.id);
          return this.http.post<TemporaryPassword>(`${API_BASE}/users/${user.id}/reset-password`, null);
        }),
      )
      .subscribe({
        next: (result) => {
          this.busyId.set(null);
          this.users.reload();
          this.showPassword(user.fullName, result);
        },
        error: () => this.busyId.set(null),
      });
  }

  protected toggle(user: UserAccount): void {
    const deactivating = user.isActive;
    this.confirm
      .ask(
        deactivating
          ? {
              title: `¿Desactivar la cuenta de ${user.fullName}?`,
              message: 'Sus sesiones se cerrarán de inmediato y no podrá entrar. No se borra nada: puedes reactivarla cuando quieras.',
              confirmText: 'Desactivar',
              danger: true,
            }
          : { title: `¿Reactivar la cuenta de ${user.fullName}?`, message: 'Podrá volver a entrar con su contraseña.', confirmText: 'Reactivar' },
      )
      .pipe(
        filter(Boolean),
        switchMap(() => {
          this.busyId.set(user.id);
          return this.http.patch<UserAccount>(`${API_BASE}/users/${user.id}/status`, { isActive: !deactivating });
        }),
      )
      .subscribe({
        next: () => {
          this.toasts.success(deactivating ? `Cuenta de ${user.fullName} desactivada.` : `Cuenta de ${user.fullName} reactivada.`);
          this.busyId.set(null);
          this.users.reload();
        },
        error: () => this.busyId.set(null),
      });
  }

  private showPassword(name: string, result: TemporaryPassword): void {
    const data: TemporaryPasswordData = { name, business: this.auth.tenant()?.name ?? 'tu negocio', result };
    // sin cerrar al hacer clic afuera para no perder la contrasena por accidente
    this.dialog.open(TemporaryPasswordDialog, { data, ...DIALOG_DEFAULTS, disableClose: true });
  }
}
