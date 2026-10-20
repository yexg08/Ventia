import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { API_BASE, getErrorMessage, getFieldErrors, silentErrors } from '../../../core/http/api';
import { Category, CategoryUpsert } from '../business.models';

export interface CategoryDialogData {
  category: Category | null;
  /** orden sugerido para una categoria nueva (al final de la lista) */
  nextOrder: number;
}

/** crear o editar una categoria (devuelve la categoria guardada al cerrar o nada si se cancela) */
@Component({
  selector: 'app-category-dialog',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form
      [formGroup]="form"
      (ngSubmit)="save()"
      novalidate
      class="card flex w-[min(28rem,calc(100vw-2rem))] flex-col !shadow-2xl"
      aria-labelledby="category-dialog-title"
    >
      <div class="border-b border-line px-6 py-4">
        <h2 id="category-dialog-title" class="text-lg font-bold">{{ data.category ? 'Editar categoría' : 'Nueva categoría' }}</h2>
      </div>

      <div class="space-y-5 px-6 py-5">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
        }
        <div>
          <label for="category-name" class="field-label">Nombre</label>
          <input id="category-name" formControlName="name" placeholder="Ej. Hamburguesas" class="field-input" [attr.aria-invalid]="invalid('name')" />
          @if (invalid('name')) {
            <p class="field-error">{{ serverErrors()['name'] ?? 'Escribe el nombre (máximo 50 caracteres).' }}</p>
          }
        </div>

        <div>
          <label for="category-order" class="field-label">Orden en el punto de venta</label>
          <input
            id="category-order"
            type="number"
            inputmode="numeric"
            min="0"
            max="999"
            formControlName="sortOrder"
            class="field-input"
            aria-describedby="category-order-hint"
            [attr.aria-invalid]="invalid('sortOrder')"
          />
          <p id="category-order-hint" class="mt-1.5 text-xs" [class]="invalid('sortOrder') ? 'text-danger font-medium' : 'text-faint'">
            {{ serverErrors()['sortOrder'] ?? 'Las de número menor salen primero (de 0 a 999).' }}
          </p>
        </div>

        <label class="flex cursor-pointer items-start gap-3 rounded-xl border border-line p-3.5">
          <input type="checkbox" formControlName="isActive" class="mt-0.5 size-4 accent-accent" />
          <span>
            <span class="block text-sm font-medium">Activa</span>
            <span class="block text-xs text-muted">Si la desactivas no aparece en el punto de venta, pero no se borra.</span>
          </span>
        </label>
      </div>

      <div class="flex justify-end gap-2 border-t border-line px-6 py-4">
        <button type="button" class="btn-secondary" (click)="ref.close()">Cancelar</button>
        <button type="submit" class="btn-primary" [disabled]="saving()">{{ saving() ? 'Guardando…' : 'Guardar' }}</button>
      </div>
    </form>
  `,
})
export class CategoryDialog {
  private readonly http = inject(HttpClient);
  protected readonly data = inject<CategoryDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Category>>(DialogRef);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.data.category?.name ?? '', [Validators.required, Validators.maxLength(50)]],
    sortOrder: [this.data.category?.sortOrder ?? this.data.nextOrder, [Validators.required, Validators.min(0), Validators.max(999)]],
    isActive: [this.data.category?.isActive ?? true],
  });

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});

  protected invalid(control: 'name' | 'sortOrder'): boolean {
    const c = this.form.controls[control];
    return (c.invalid && c.touched) || !!this.serverErrors()[control];
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    const body: CategoryUpsert = { ...raw, name: raw.name.trim() };
    const request = this.data.category
      ? this.http.put<Category>(`${API_BASE}/categories/${this.data.category.id}`, body, { context: silentErrors() })
      : this.http.post<Category>(`${API_BASE}/categories`, body, { context: silentErrors() });

    this.saving.set(true);
    this.error.set(null);
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e: unknown) => {
        const fields = getFieldErrors(e);
        this.serverErrors.set(fields);
        if (Object.keys(fields).length === 0) this.error.set(getErrorMessage(e));
        this.saving.set(false);
      },
    });
  }
}
