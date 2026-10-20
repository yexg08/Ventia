import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroArrowLeft } from '@ng-icons/heroicons/outline';
import { Logo } from '../shared/logo';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink, Logo, NgIcon],
  providers: [provideIcons({ heroArrowLeft })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="relative flex min-h-dvh flex-col items-center justify-center overflow-hidden px-4 text-center">
      <div class="bg-grid pointer-events-none absolute inset-0 -z-10" aria-hidden="true"></div>
      <div class="stagger flex flex-col items-center">
        <app-logo [size]="40" [withText]="false" />
        <p class="mt-8 bg-gradient-to-b from-ink to-faint bg-clip-text text-8xl font-extrabold tracking-tighter text-transparent tabular-nums">404</p>
        <h1 class="mt-4 text-2xl font-bold">Esta página no existe</h1>
        <p class="mt-2 max-w-sm text-muted">Puede que el enlace esté mal escrito o que la página ya no exista.</p>
        <a routerLink="/" class="btn-primary group mt-8">
          <ng-icon name="heroArrowLeft" size="17" class="transition duration-300 group-hover:-translate-x-0.5" />
          Volver al inicio
        </a>
      </div>
    </main>
  `,
})
export class NotFound {}
