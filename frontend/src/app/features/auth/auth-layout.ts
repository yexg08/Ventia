import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Logo } from '../../shared/logo';
import { ThemeToggle } from '../../shared/theme-toggle';

/** marco comun de login y registro (logo y tarjeta centrada) */
@Component({
  selector: 'app-auth-layout',
  imports: [RouterLink, Logo, ThemeToggle],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-dvh flex-col">
      <header class="mx-auto flex w-full max-w-6xl items-center justify-between px-4 py-5 sm:px-6">
        <a routerLink="/" aria-label="Ventia, inicio"><app-logo /></a>
        <app-theme-toggle />
      </header>
      <main class="flex flex-1 items-start justify-center px-4 pt-6 pb-16 sm:items-center sm:pt-0">
        <div class="card w-full p-6 shadow-sm sm:p-8" [class]="wide() ? 'max-w-xl' : 'max-w-md'">
          <h1 class="text-2xl font-extrabold tracking-tight">{{ heading() }}</h1>
          <p class="mt-1.5 text-sm text-muted">{{ subheading() }}</p>
          <div class="mt-7"><ng-content /></div>
        </div>
      </main>
    </div>
  `,
})
export class AuthLayout {
  readonly heading = input.required<string>();
  readonly subheading = input.required<string>();
  readonly wide = input(false);
}
