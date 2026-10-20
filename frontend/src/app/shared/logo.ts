import { ChangeDetectionStrategy, Component, input } from '@angular/core';

let nextId = 0;

/** marca de Ventia (una V blanca sobre un cuadro con el degradado naranja de la marca) */
@Component({
  selector: 'app-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex items-center gap-2.5' },
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 32 32" aria-hidden="true" class="shrink-0 drop-shadow-[0_2px_6px_rgb(194_65_12/0.35)]">
      <defs>
        <linearGradient [attr.id]="gradientId" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#f97316" />
          <stop offset="0.55" stop-color="#c2410c" />
          <stop offset="1" stop-color="#9a3412" />
        </linearGradient>
      </defs>
      <rect width="32" height="32" rx="9" [attr.fill]="'url(#' + gradientId + ')'" />
      <rect x="0.5" y="0.5" width="31" height="31" rx="8.5" fill="none" stroke="#ffffff" stroke-opacity="0.18" />
      <path d="M9.5 10 L16 22.5 L22.5 10" fill="none" stroke="#ffffff" stroke-width="3.4" stroke-linecap="round" stroke-linejoin="round" />
    </svg>
    @if (withText()) {
      <span class="text-xl font-extrabold tracking-tight">Ventia</span>
    }
  `,
})
export class Logo {
  readonly size = input(32);
  readonly withText = input(true);

  // cada logo de la pagina necesita su propio id de degradado
  protected readonly gradientId = `ventia-logo-${nextId++}`;
}
