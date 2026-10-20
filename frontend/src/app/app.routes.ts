import { Routes } from '@angular/router';
import { guestGuard, ownerGuard, passwordChangeGuard, superAdminGuard, tenantGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  { path: '', title: 'Ventia · Ventas e inventario para tu negocio', loadComponent: () => import('./features/landing/landing').then((m) => m.Landing) },
  { path: 'login', title: 'Iniciar sesión · Ventia', canActivate: [guestGuard], loadComponent: () => import('./features/auth/login').then((m) => m.Login) },
  { path: 'registro', title: 'Crea tu negocio · Ventia', canActivate: [guestGuard], loadComponent: () => import('./features/auth/register').then((m) => m.Register) },
  { path: 'cambiar-contrasena', title: 'Elige tu contraseña · Ventia', canActivate: [passwordChangeGuard], loadComponent: () => import('./features/auth/change-password').then((m) => m.ChangePassword) },
  {
    path: 'app',
    canActivate: [tenantGuard],
    loadComponent: () => import('./features/app/shell').then((m) => m.Shell),
    children: [
      { path: '', title: 'Inicio · Ventia', loadComponent: () => import('./features/app/home').then((m) => m.Home) },
      { path: 'vender', title: 'Vender · Ventia', loadComponent: () => import('./features/app/pos/pos-page').then((m) => m.PosPage) },
      { path: 'ventas', title: 'Ventas · Ventia', loadComponent: () => import('./features/app/pos/sales-page').then((m) => m.SalesPage) },
      { path: 'cotizaciones', title: 'Cotizaciones · Ventia', loadComponent: () => import('./features/app/documents/quotes-page').then((m) => m.QuotesPage) },
      { path: 'gastos', title: 'Gastos · Ventia', loadComponent: () => import('./features/app/expenses/expenses-page').then((m) => m.ExpensesPage) },
      { path: 'cuentas-por-pagar', title: 'Cuentas por pagar · Ventia', canActivate: [ownerGuard], loadComponent: () => import('./features/app/documents/payables-page').then((m) => m.PayablesPage) },
      { path: 'reportes', title: 'Reportes · Ventia', canActivate: [ownerGuard], loadComponent: () => import('./features/app/reports/reports-page').then((m) => m.ReportsPage) },
      { path: 'compras', title: 'Compras · Ventia', canActivate: [ownerGuard], loadComponent: () => import('./features/app/documents/purchases-page').then((m) => m.PurchasesPage) },
      { path: 'productos', title: 'Productos · Ventia', loadComponent: () => import('./features/app/products/products-page').then((m) => m.ProductsPage) },
      { path: 'clientes', title: 'Clientes · Ventia', loadComponent: () => import('./features/app/parties/customers-page').then((m) => m.CustomersPage) },
      { path: 'proveedores', title: 'Proveedores · Ventia', canActivate: [ownerGuard], loadComponent: () => import('./features/app/parties/suppliers-page').then((m) => m.SuppliersPage) },
      { path: 'categorias', title: 'Categorías · Ventia', loadComponent: () => import('./features/app/categories/categories-page').then((m) => m.CategoriesPage) },
      { path: 'usuarios', title: 'Usuarios · Ventia', canActivate: [ownerGuard], loadComponent: () => import('./features/app/users/users-page').then((m) => m.UsersPage) },
      { path: 'configuracion', title: 'Configuración · Ventia', canActivate: [ownerGuard], loadComponent: () => import('./features/app/settings/settings').then((m) => m.Settings) },
    ],
  },
  { path: 'admin', title: 'Plataforma · Ventia', canActivate: [superAdminGuard], loadComponent: () => import('./features/admin/admin').then((m) => m.Admin) },
  { path: '**', title: 'Página no encontrada · Ventia', loadComponent: () => import('./features/not-found').then((m) => m.NotFound) },
];
