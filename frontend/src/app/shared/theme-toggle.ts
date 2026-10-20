import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { heroMoon, heroSun } from '@ng-icons/heroicons/outline';
import { ThemeService } from '../core/theme/theme.service';

@Component({
  selector: 'app-theme-toggle',
  imports: [NgIcon],
  providers: [provideIcons({ heroMoon, heroSun })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="btn-ghost group px-2.5"
      (click)="toggle($event)"
      [attr.aria-label]="theme.isDark() ? 'Cambiar a modo claro' : 'Cambiar a modo oscuro'"
      [attr.title]="theme.isDark() ? 'Modo claro' : 'Modo oscuro'"
    >
      <ng-icon [name]="theme.isDark() ? 'heroSun' : 'heroMoon'" size="20" class="transition duration-500 ease-out group-hover:rotate-[20deg]" />
    </button>
  `,
})
export class ThemeToggle {
  protected readonly theme = inject(ThemeService);

  /** el circulo sale del centro del boton (tambien cuando se activa con el teclado) */
  protected toggle(event: MouseEvent): void {
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.theme.toggle({ x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 });
  }
}
