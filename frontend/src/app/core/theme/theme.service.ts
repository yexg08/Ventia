import { DOCUMENT, Injectable, computed, effect, inject, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'ventia-theme';

/**
 * tema claro/oscuro - el script de index.html aplica el tema guardado (o el del sistema) antes de cargar Angular y
 * este servicio parte de ese estado y lo mantiene sincronizado con la clase .dark de <html>
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly root = this.document.documentElement;

  readonly theme = signal<Theme>(this.root.classList.contains('dark') ? 'dark' : 'light');
  readonly isDark = computed(() => this.theme() === 'dark');

  constructor() {
    effect(() => this.root.classList.toggle('dark', this.isDark()));
  }

  /**
   * cambia el tema con un circulo que crece desde el punto del clic (si el navegador tiene View Transitions y la
   * persona no pidio menos movimiento) y si no cambia al instante
   */
  toggle(origin?: { x: number; y: number }): void {
    const next: Theme = this.isDark() ? 'light' : 'dark';
    const view = this.document.defaultView;
    const reduced = view?.matchMedia('(prefers-reduced-motion: reduce)').matches ?? true;

    if (!origin || reduced || typeof this.document.startViewTransition !== 'function' || !view) {
      this.apply(next);
      return;
    }

    // radio hasta la esquina mas lejana para que el circulo cubra toda la pantalla
    const radius = Math.hypot(Math.max(origin.x, view.innerWidth - origin.x), Math.max(origin.y, view.innerHeight - origin.y));
    this.root.style.setProperty('--theme-x', `${origin.x}px`);
    this.root.style.setProperty('--theme-y', `${origin.y}px`);
    this.root.style.setProperty('--theme-r', `${radius}px`);
    this.root.classList.add('theme-switching');

    const transition = this.document.startViewTransition(() => {
      this.apply(next);
      // el efecto que pone la clase corre despues (aqui se aplica de una vez para que la captura nueva ya tenga el tema)
      this.root.classList.toggle('dark', next === 'dark');
    });
    transition.finished.finally(() => this.root.classList.remove('theme-switching'));
  }

  private apply(next: Theme): void {
    this.theme.set(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // almacenamiento bloqueado (modo privado) (el tema funciona igual solo no se recuerda)
    }
  }
}
