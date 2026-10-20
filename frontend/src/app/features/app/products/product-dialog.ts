import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { API_BASE, getErrorMessage, getFieldErrors, silentErrors } from '../../../core/http/api';
import { formatPrice } from '../../../shared/format';
import { splitTax } from '../../../shared/taxes';
import { Category, Product, ProductUpsert, TAX_OPTIONS, TaxType } from '../business.models';

export interface ProductDialogData {
  product: Product | null;
  categories: Category[];
  defaultTax: TaxType;
}

type Field = 'categoryId' | 'name' | 'code' | 'barcode' | 'price' | 'cost' | 'minStock' | 'initialStock';

/** crear o editar un producto (devuelve el producto guardado al cerrar o nada si se cancela) */
@Component({
  selector: 'app-product-dialog',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form
      [formGroup]="form"
      (ngSubmit)="save()"
      novalidate
      class="card flex max-h-[calc(100dvh-2rem)] w-[min(36rem,calc(100vw-2rem))] flex-col shadow-2xl"
      aria-labelledby="product-dialog-title"
    >
      <div class="border-b border-line px-6 py-4">
        <h2 id="product-dialog-title" class="text-lg font-bold">{{ data.product ? 'Editar producto' : 'Nuevo producto' }}</h2>
      </div>

      <div class="space-y-5 overflow-y-auto px-6 py-5">
        @if (error()) {
          <p class="rounded-xl border border-danger/40 bg-danger/10 px-3.5 py-2.5 text-sm text-danger" role="alert">{{ error() }}</p>
        }

        <div class="grid gap-5 sm:grid-cols-[1fr_12rem]">
          <div>
            <label for="product-name" class="field-label">Nombre</label>
            <input id="product-name" formControlName="name" placeholder="Ej. Hamburguesa doble" class="field-input" [attr.aria-invalid]="invalid('name')" />
            @if (invalid('name')) {
              <p class="field-error">{{ serverErrors()['name'] ?? 'Escribe el nombre (máximo 80 caracteres).' }}</p>
            }
          </div>
          <div>
            <label for="product-category" class="field-label">Categoría</label>
            <select id="product-category" formControlName="categoryId" class="field-input" [attr.aria-invalid]="invalid('categoryId')">
              <option value="" disabled>Elige una</option>
              @for (category of data.categories; track category.id) {
                <option [value]="category.id">{{ category.name }}</option>
              }
            </select>
            @if (invalid('categoryId')) {
              <p class="field-error">{{ serverErrors()['categoryId'] ?? 'Elige una categoría.' }}</p>
            }
          </div>
        </div>

        <div class="grid gap-5 sm:grid-cols-2">
          <div>
            <label for="product-price" class="field-label">Precio de venta (con impuesto)</label>
            <input id="product-price" type="number" inputmode="decimal" min="0" step="100" formControlName="price" class="field-input" [attr.aria-invalid]="invalid('price')" />
            @if (invalid('price')) {
              <p class="field-error">{{ serverErrors()['price'] ?? 'Escribe un precio válido (máximo 2 decimales).' }}</p>
            } @else {
              <p class="mt-1.5 text-xs text-faint">Base {{ money(breakdown().base) }} + impuesto {{ money(breakdown().tax) }}</p>
            }
          </div>
          <div>
            <label for="product-tax" class="field-label">Impuesto</label>
            <select id="product-tax" formControlName="tax" class="field-input">
              @for (option of taxOptions; track option.value) {
                <option [value]="option.value">{{ option.label }}</option>
              }
            </select>
          </div>
        </div>

        <div>
          <label for="product-cost" class="field-label">Costo <span class="font-normal text-faint">(opcional, solo lo ves tú)</span></label>
          <input id="product-cost" type="number" inputmode="decimal" min="0" step="100" formControlName="cost" class="field-input" [attr.aria-invalid]="invalid('cost')" />
          @if (invalid('cost')) {
            <p class="field-error">{{ serverErrors()['cost'] ?? 'Escribe un costo válido.' }}</p>
          } @else if (margin() !== null) {
            <p class="mt-1.5 text-xs text-faint">Margen sobre el precio sin impuesto: {{ margin() }} %</p>
          }
        </div>

        <div class="grid gap-5 sm:grid-cols-2">
          <div>
            <label for="product-code" class="field-label">Código <span class="font-normal text-faint">(opcional)</span></label>
            <input id="product-code" formControlName="code" autocomplete="off" class="field-input uppercase" [attr.aria-invalid]="invalid('code')" />
            @if (invalid('code')) {
              <p class="field-error">{{ serverErrors()['code'] ?? 'Hasta 30 letras, números o guiones.' }}</p>
            }
          </div>
          <div>
            <label for="product-barcode" class="field-label">Código de barras <span class="font-normal text-faint">(opcional)</span></label>
            <input id="product-barcode" formControlName="barcode" autocomplete="off" inputmode="numeric" class="field-input" [attr.aria-invalid]="invalid('barcode')" />
            @if (invalid('barcode')) {
              <p class="field-error">{{ serverErrors()['barcode'] ?? 'Hasta 30 letras, números o guiones.' }}</p>
            }
          </div>
        </div>

        <fieldset class="space-y-4 rounded-xl border border-line p-4">
          <legend class="px-1 text-sm font-semibold">Inventario</legend>
          <label class="flex cursor-pointer items-start gap-3">
            <input type="checkbox" formControlName="tracksStock" class="mt-0.5 size-4 accent-accent" />
            <span>
              <span class="block text-sm font-medium">Llevar inventario de este producto</span>
              <span class="block text-xs text-muted">Para lo que se compra hecho (gaseosas, paquetes). Una hamburguesa preparada normalmente no lo lleva.</span>
            </span>
          </label>

          @if (values().tracksStock) {
            <div class="grid gap-4 sm:grid-cols-2">
              @if (!data.product) {
                <div>
                  <label for="product-initial" class="field-label">Stock inicial</label>
                  <input id="product-initial" type="number" inputmode="numeric" min="0" step="1" formControlName="initialStock" class="field-input" [attr.aria-invalid]="invalid('initialStock')" />
                  @if (invalid('initialStock')) {
                    <p class="field-error">Entre 0 y 1.000.000 unidades.</p>
                  }
                </div>
              } @else {
                <div>
                  <p class="field-label">Stock actual</p>
                  <p class="py-2.5 text-sm"><strong>{{ data.product.stock }}</strong> <span class="text-muted">unidades (se cambia con un ajuste)</span></p>
                </div>
              }
              <div>
                <label for="product-min" class="field-label">Avisar con stock bajo en</label>
                <input id="product-min" type="number" inputmode="numeric" min="0" step="1" formControlName="minStock" class="field-input" [attr.aria-invalid]="invalid('minStock')" />
                @if (invalid('minStock')) {
                  <p class="field-error">Entre 0 y 1.000.000 unidades.</p>
                }
              </div>
            </div>
          }
        </fieldset>

        <label class="flex cursor-pointer items-start gap-3 rounded-xl border border-line p-3.5">
          <input type="checkbox" formControlName="isActive" class="mt-0.5 size-4 accent-accent" />
          <span>
            <span class="block text-sm font-medium">Activo</span>
            <span class="block text-xs text-muted">Si lo desactivas no sale en el punto de venta, pero no se borra.</span>
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
export class ProductDialog {
  private readonly http = inject(HttpClient);
  protected readonly data = inject<ProductDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Product>>(DialogRef);
  protected readonly taxOptions = TAX_OPTIONS;

  private readonly decimals = Validators.pattern(/^\d+(\.\d{1,2})?$/);
  private readonly p = this.data.product;

  protected readonly form = inject(NonNullableFormBuilder).group({
    categoryId: [this.p?.categoryId ?? '', [Validators.required]],
    name: [this.p?.name ?? '', [Validators.required, Validators.maxLength(80)]],
    code: [this.p?.code ?? '', [Validators.pattern(/^[A-Za-z0-9-]{0,30}$/)]],
    barcode: [this.p?.barcode ?? '', [Validators.pattern(/^[A-Za-z0-9-]{0,30}$/)]],
    price: [this.p?.price ?? 0, [Validators.required, Validators.min(0), Validators.max(100_000_000), this.decimals]],
    cost: [this.p?.cost ?? (null as number | null), [Validators.min(0), Validators.max(100_000_000), this.decimals]],
    tax: [this.p?.tax ?? this.data.defaultTax, [Validators.required]],
    tracksStock: [this.p?.tracksStock ?? false],
    minStock: [this.p?.minStock ?? 0, [Validators.required, Validators.min(0), Validators.max(1_000_000)]],
    initialStock: [0, [Validators.min(0), Validators.max(1_000_000)]],
    isActive: [this.p?.isActive ?? true],
  });

  protected readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly breakdown = computed(() => splitTax(Number(this.values().price) || 0, this.values().tax ?? this.data.defaultTax));
  protected readonly margin = computed(() => {
    const cost = this.values().cost;
    const base = this.breakdown().base;
    if (cost === null || cost === undefined || String(cost) === '' || base <= 0) return null;
    return Math.round(((base - Number(cost)) / base) * 1000) / 10;
  });

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});
  protected readonly money = formatPrice;

  protected invalid(control: Field): boolean {
    const c = this.form.controls[control];
    return (c.invalid && c.touched) || !!this.serverErrors()[control];
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    const costText = raw.cost === null ? '' : String(raw.cost).trim();
    const body: ProductUpsert = {
      categoryId: raw.categoryId,
      name: raw.name.trim(),
      code: raw.code.trim() || null,
      barcode: raw.barcode.trim() || null,
      price: Number(raw.price),
      cost: costText === '' ? null : Number(costText),
      tax: raw.tax,
      tracksStock: raw.tracksStock,
      minStock: raw.tracksStock ? Number(raw.minStock) : 0,
      isActive: raw.isActive,
      initialStock: !this.p && raw.tracksStock ? Number(raw.initialStock) : null,
    };
    const request = this.p
      ? this.http.put<Product>(`${API_BASE}/products/${this.p.id}`, body, { context: silentErrors() })
      : this.http.post<Product>(`${API_BASE}/products`, body, { context: silentErrors() });

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
