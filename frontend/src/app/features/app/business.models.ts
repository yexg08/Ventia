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

export type DocumentType = 'CC' | 'CE' | 'NIT' | 'PP';

export const DOCUMENT_TYPES: { value: DocumentType; label: string }[] = [
  { value: 'CC', label: 'Cédula de ciudadanía' },
  { value: 'NIT', label: 'NIT' },
  { value: 'CE', label: 'Cédula de extranjería' },
  { value: 'PP', label: 'Pasaporte' },
];

export interface Customer {
  id: string;
  documentType: DocumentType;
  documentNumber: string;
  name: string;
  phone: string | null;
  email: string | null;
  address: string | null;
  notes: string | null;
  /** cuando autorizo el tratamiento de sus datos (Ley 1581) */
  dataConsentAt: string | null;
  createdAt: string;
}

export interface CustomerUpsert {
  documentType: DocumentType;
  documentNumber: string;
  name: string;
  phone: string | null;
  email: string | null;
  address: string | null;
  notes: string | null;
  dataConsent: boolean;
}

export interface Supplier {
  id: string;
  documentType: DocumentType;
  documentNumber: string;
  name: string;
  contactName: string | null;
  phone: string | null;
  email: string | null;
  address: string | null;
  notes: string | null;
  isActive: boolean;
}

export type SupplierUpsert = Omit<Supplier, 'id'>;

export type OrderType = 'DineIn' | 'TakeAway' | 'Delivery';
export type SaleStatus = 'Completed' | 'Voided';
export type PaymentMethod = 'Cash' | 'Card' | 'Transfer';

export const ORDER_TYPES: { value: OrderType; label: string }[] = [
  { value: 'TakeAway', label: 'Para llevar' },
  { value: 'DineIn', label: 'Mesa' },
  { value: 'Delivery', label: 'Domicilio' },
];

export const PAYMENT_METHODS: { value: PaymentMethod; label: string }[] = [
  { value: 'Cash', label: 'Efectivo' },
  { value: 'Card', label: 'Tarjeta' },
  { value: 'Transfer', label: 'Transferencia' },
];

export interface PaymentSummary {
  method: PaymentMethod;
  amount: number;
}

export interface CashSession {
  id: string;
  userName: string;
  openedAt: string;
  openingCash: number;
  closedAt: string | null;
  salesCount: number;
  voidedCount: number;
  salesTotal: number;
  byMethod: PaymentSummary[];
  /** base mas efectivo de las ventas no anuladas */
  expectedCash: number;
  countedCash: number | null;
  difference: number | null;
  closeNotes: string | null;
}

export interface SaleLineRequest {
  productId: string;
  quantity: number;
  discountPercent: number;
  note: string | null;
  parentIndex: number | null;
}

export interface CreateSaleRequest {
  idempotencyKey: string;
  orderType: OrderType;
  tableLabel: string | null;
  deliveryAddress: string | null;
  customerId: string | null;
  lines: SaleLineRequest[];
  payments: PaymentSummary[];
  cashTendered: number;
  notes: string | null;
}

export interface SaleLine {
  id: string;
  position: number;
  productId: string;
  productName: string;
  parentLineId: string | null;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  discountAmount: number;
  tax: TaxType;
  lineTotal: number;
  lineBase: number;
  lineTax: number;
  note: string | null;
}

export interface ReceiptBusiness {
  name: string;
  taxId: string | null;
  address: string | null;
  city: string | null;
  phone: string | null;
  footer: string | null;
}

export interface Sale {
  id: string;
  number: number;
  createdAt: string;
  sellerName: string;
  orderType: OrderType;
  tableLabel: string | null;
  deliveryAddress: string | null;
  customerId: string | null;
  customerName: string;
  customerDocument: string | null;
  subtotal: number;
  taxTotal: number;
  discountTotal: number;
  total: number;
  cashTendered: number;
  change: number;
  status: SaleStatus;
  voidedAt: string | null;
  voidedBy: string | null;
  voidReason: string | null;
  notes: string | null;
  lines: SaleLine[];
  payments: PaymentSummary[];
  business: ReceiptBusiness;
}

export interface SaleSummary {
  id: string;
  number: number;
  createdAt: string;
  sellerName: string;
  orderType: OrderType;
  customerName: string;
  total: number;
  status: SaleStatus;
  itemCount: number;
}

export type PurchaseStatus = 'Completed' | 'Voided';

export interface PurchaseLine {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  unitCost: number;
  lineTotal: number;
}

export interface Purchase {
  id: string;
  number: number;
  date: string;
  supplierId: string;
  supplierName: string;
  supplierInvoice: string | null;
  total: number;
  notes: string | null;
  status: PurchaseStatus;
  voidedAt: string | null;
  voidedBy: string | null;
  voidReason: string | null;
  createdByName: string;
  createdAt: string;
  lines: PurchaseLine[];
}

export interface PurchaseSummary {
  id: string;
  number: number;
  date: string;
  supplierName: string;
  supplierInvoice: string | null;
  total: number;
  status: PurchaseStatus;
  itemCount: number;
}

export interface CreatePurchaseRequest {
  supplierId: string;
  supplierInvoice: string | null;
  date: string | null;
  lines: { productId: string; quantity: number; unitCost: number }[];
  notes: string | null;
}

export type QuoteStatus = 'Open' | 'Converted' | 'Cancelled';

export interface QuoteLine {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  discountAmount: number;
  tax: TaxType;
  lineTotal: number;
  lineBase: number;
  lineTax: number;
}

export interface Quote {
  id: string;
  number: number;
  createdAt: string;
  customerId: string | null;
  customerName: string;
  validUntil: string;
  isExpired: boolean;
  subtotal: number;
  taxTotal: number;
  discountTotal: number;
  total: number;
  notes: string | null;
  status: QuoteStatus;
  saleId: string | null;
  saleNumber: number | null;
  createdByName: string;
  lines: QuoteLine[];
  business: ReceiptBusiness;
}

export interface QuoteSummary {
  id: string;
  number: number;
  createdAt: string;
  customerName: string;
  validUntil: string;
  isExpired: boolean;
  total: number;
  status: QuoteStatus;
}

export interface QuoteUpsert {
  customerId: string | null;
  customerName: string | null;
  validUntil: string;
  lines: { productId: string; quantity: number; discountPercent: number }[];
  notes: string | null;
}
