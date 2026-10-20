/** impuestos que maneja la demo (los precios se escriben con el impuesto incluido) */
export type TaxType = 'None' | 'Iva5' | 'Iva19' | 'Inc8';

export const TAX_OPTIONS: { value: TaxType; label: string; hint: string }[] = [
  { value: 'Inc8', label: 'INC 8 %', hint: 'Impuesto al consumo: restaurantes y comidas rápidas' },
  { value: 'Iva19', label: 'IVA 19 %', hint: 'Tarifa general' },
  { value: 'Iva5', label: 'IVA 5 %', hint: 'Tarifa reducida' },
  { value: 'None', label: 'Sin impuesto', hint: 'Productos excluidos o exentos' },
];

export interface BusinessSettings {
  id: string;
  name: string;
  taxId: string | null;
  address: string | null;
  city: string | null;
  phone: string | null;
  timeZone: string;
  currency: string;
  defaultTax: TaxType;
  receiptFooter: string | null;
}

export type UpdateBusiness = Omit<BusinessSettings, 'id' | 'currency'>;

export interface Category {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
}

export type CategoryUpsert = Omit<Category, 'id'>;

export type AccountRole = 'Owner' | 'Cashier';

export interface UserAccount {
  id: string;
  fullName: string;
  email: string;
  role: AccountRole;
  isActive: boolean;
  /** aun no cambia la contrasena temporal */
  pendingFirstLogin: boolean;
  temporaryPasswordExpiresAt: string | null;
  lastLoginAt: string | null;
  createdAt: string;
}

/** la contrasena temporal solo llega una vez (en la base queda su hash) */
export interface TemporaryPassword {
  userId: string;
  email: string;
  temporaryPassword: string;
  expiresAt: string;
}

export interface Product {
  id: string;
  categoryId: string;
  categoryName: string;
  name: string;
  code: string | null;
  barcode: string | null;
  /** precio con el impuesto incluido */
  price: number;
  basePrice: number;
  taxAmount: number;
  tax: TaxType;
  /** null para el cajero (lo decide el servidor) */
  cost: number | null;
  marginPercent: number | null;
  tracksStock: boolean;
  stock: number;
  minStock: number;
  isLowStock: boolean;
  isActive: boolean;
}

export interface ProductUpsert {
  categoryId: string;
  name: string;
  code: string | null;
  barcode: string | null;
  price: number;
  cost: number | null;
  tax: TaxType;
  tracksStock: boolean;
  minStock: number;
  isActive: boolean;
  /** solo al crear */
  initialStock: number | null;
}

export type StockAdjustmentKind = 'In' | 'Out' | 'Count';

export interface StockAdjustment {
  kind: StockAdjustmentKind;
  quantity: number;
  reason: string;
}

export type StockMovementType = 'Initial' | 'In' | 'Out' | 'Count';

export interface StockMovement {
  id: string;
  createdAt: string;
  type: StockMovementType;
  quantity: number;
  stockAfter: number;
  reason: string | null;
  userName: string;
}
