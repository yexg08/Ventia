import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** marca de Ventia (una V blanca sobre un cuadro naranja) provisional hasta el diseno final */
@Component({
  selector: 'app-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex items-center gap-2.5' },
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 32 32" aria-hidden="true" class="shrink-0">
      <rect width="32" height="32" rx="9" fill="#c2410c" />
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
}
