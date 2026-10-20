using Ventia.Domain.Entities;
using Ventia.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ventia.Infrastructure.Persistence;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.Property(t => t.Name).HasMaxLength(80).IsRequired();
        b.Property(t => t.TaxId).HasMaxLength(20);
        b.Property(t => t.Address).HasMaxLength(120);
        b.Property(t => t.City).HasMaxLength(60);
        b.Property(t => t.Phone).HasMaxLength(15);
        b.Property(t => t.TimeZone).HasMaxLength(64).IsRequired();
        b.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        b.Property(t => t.DefaultTax).HasConversion<string>().HasMaxLength(10);
        b.Property(t => t.ReceiptFooter).HasMaxLength(200);
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public const string UniqueNameIndex = "ux_categories_tenant_name";

    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.Property(c => c.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(c => new { c.TenantId, c.Name }).IsUnique().HasDatabaseName(UniqueNameIndex);
        b.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public const string UniqueCodeIndex = "ux_products_tenant_code";
    public const string UniqueBarcodeIndex = "ux_products_tenant_barcode";

    public void Configure(EntityTypeBuilder<Product> b)
    {
        // ultima barrera contra el stock negativo (aunque un error de codigo se saltara la condicion del UPDATE)
        b.ToTable("products", t =>
        {
            t.HasCheckConstraint("ck_products_stock_non_negative", "\"Stock\" >= 0");
            t.HasCheckConstraint("ck_products_price_non_negative", "\"Price\" >= 0");
        });
        b.Property(p => p.Name).HasMaxLength(80).IsRequired();
        b.Property(p => p.Code).HasMaxLength(30);
        b.Property(p => p.Barcode).HasMaxLength(30);
        b.Property(p => p.Price).HasPrecision(14, 2);
        b.Property(p => p.Cost).HasPrecision(14, 2);
        b.Property(p => p.Tax).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(p => new { p.TenantId, p.Code }).IsUnique().HasFilter("\"Code\" IS NOT NULL").HasDatabaseName(UniqueCodeIndex);
        b.HasIndex(p => new { p.TenantId, p.Barcode }).IsUnique().HasFilter("\"Barcode\" IS NOT NULL").HasDatabaseName(UniqueBarcodeIndex);
        b.HasIndex(p => new { p.TenantId, p.CategoryId });
        b.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        // una categoria con productos no se puede borrar
        b.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movements", t => t.HasCheckConstraint("ck_stock_movements_stock_after", "\"StockAfter\" >= 0"));
        b.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.Reason).HasMaxLength(120);
        b.Property(m => m.UserName).HasMaxLength(80).IsRequired();
        b.HasIndex(m => new { m.ProductId, m.CreatedAt });
        b.HasIndex(m => m.TenantId);
        b.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public const string UniqueDocumentIndex = "ux_customers_tenant_document";

    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.Property(c => c.DocumentType).HasConversion<string>().HasMaxLength(5);
        b.Property(c => c.DocumentNumber).HasMaxLength(20).IsRequired();
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.Property(c => c.Phone).HasMaxLength(15);
        b.Property(c => c.Email).HasMaxLength(256);
        b.Property(c => c.Address).HasMaxLength(120);
        b.Property(c => c.Notes).HasMaxLength(500);
        b.HasIndex(c => new { c.TenantId, c.DocumentType, c.DocumentNumber }).IsUnique().HasDatabaseName(UniqueDocumentIndex);
        b.HasIndex(c => new { c.TenantId, c.Name });
        b.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public const string UniqueDocumentIndex = "ux_suppliers_tenant_document";

    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers");
        b.Property(s => s.DocumentType).HasConversion<string>().HasMaxLength(5);
        b.Property(s => s.DocumentNumber).HasMaxLength(20).IsRequired();
        b.Property(s => s.Name).HasMaxLength(100).IsRequired();
        b.Property(s => s.ContactName).HasMaxLength(80);
        b.Property(s => s.Phone).HasMaxLength(15);
        b.Property(s => s.Email).HasMaxLength(256);
        b.Property(s => s.Address).HasMaxLength(120);
        b.Property(s => s.Notes).HasMaxLength(500);
        b.HasIndex(s => new { s.TenantId, s.DocumentType, s.DocumentNumber }).IsUnique().HasDatabaseName(UniqueDocumentIndex);
        b.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public const string OneOpenPerUserIndex = "ux_cash_sessions_one_open_per_user";

    public void Configure(EntityTypeBuilder<CashSession> b)
    {
        b.ToTable("cash_sessions");
        b.Property(s => s.UserName).HasMaxLength(80).IsRequired();
        b.Property(s => s.OpeningCash).HasPrecision(14, 2);
        b.Property(s => s.ExpectedCash).HasPrecision(14, 2);
        b.Property(s => s.CountedCash).HasPrecision(14, 2);
        b.Property(s => s.CloseNotes).HasMaxLength(300);
        // una cuenta tiene como maximo un turno abierto (aunque dos aperturas lleguen al mismo tiempo)
        b.HasIndex(s => new { s.TenantId, s.UserId }).IsUnique().HasFilter("\"ClosedAt\" IS NULL").HasDatabaseName(OneOpenPerUserIndex);
        b.HasIndex(s => new { s.TenantId, s.CreatedAt });
        b.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public const string UniqueNumberIndex = "ux_sales_tenant_number";
    public const string UniqueIdempotencyIndex = "ux_sales_tenant_idempotency";

    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.ToTable("sales", t => t.HasCheckConstraint("ck_sales_total_non_negative", "\"Total\" >= 0"));
        b.Property(s => s.SellerName).HasMaxLength(80).IsRequired();
        b.Property(s => s.OrderType).HasConversion<string>().HasMaxLength(10);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(s => s.TableLabel).HasMaxLength(20);
        b.Property(s => s.DeliveryAddress).HasMaxLength(150);
        b.Property(s => s.CustomerName).HasMaxLength(100).IsRequired();
        b.Property(s => s.CustomerDocument).HasMaxLength(30);
        b.Property(s => s.VoidedBy).HasMaxLength(80);
        b.Property(s => s.VoidReason).HasMaxLength(200);
        b.Property(s => s.Notes).HasMaxLength(300);
        foreach (var money in new[] { nameof(Sale.Subtotal), nameof(Sale.TaxTotal), nameof(Sale.DiscountTotal), nameof(Sale.Total), nameof(Sale.CashTendered), nameof(Sale.Change) })
            b.Property<decimal>(money).HasPrecision(14, 2);
        // consecutivo sin repetidos y una sola venta por clave de cobro
        b.HasIndex(s => new { s.TenantId, s.Number }).IsUnique().HasDatabaseName(UniqueNumberIndex);
        b.HasIndex(s => new { s.TenantId, s.IdempotencyKey }).IsUnique().HasDatabaseName(UniqueIdempotencyIndex);
        b.HasIndex(s => new { s.TenantId, s.CreatedAt });
        b.HasIndex(s => s.CashSessionId);
        b.HasIndex(s => s.CustomerId);
        b.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashSession>().WithMany().HasForeignKey(s => s.CashSessionId).OnDelete(DeleteBehavior.Restrict);
        // un cliente con ventas no se borra (se anonimiza)
        b.HasOne<Customer>().WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(s => s.Payments).WithOne().HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> b)
    {
        b.ToTable("sale_lines", t => t.HasCheckConstraint("ck_sale_lines_quantity_positive", "\"Quantity\" > 0"));
        b.Property(l => l.ProductName).HasMaxLength(80).IsRequired();
        b.Property(l => l.Note).HasMaxLength(120);
        b.Property(l => l.Tax).HasConversion<string>().HasMaxLength(10);
        foreach (var money in new[] { nameof(SaleLine.UnitPrice), nameof(SaleLine.DiscountAmount), nameof(SaleLine.LineTotal), nameof(SaleLine.LineBase), nameof(SaleLine.LineTax) })
            b.Property<decimal>(money).HasPrecision(14, 2);
        b.Property(l => l.UnitCost).HasPrecision(14, 2);
        b.Property(l => l.DiscountPercent).HasPrecision(5, 2);
        b.HasIndex(l => l.ProductId);
        b.HasIndex(l => l.TenantId);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SaleLine>().WithMany().HasForeignKey(l => l.ParentLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SalePaymentConfiguration : IEntityTypeConfiguration<SalePayment>
{
    public void Configure(EntityTypeBuilder<SalePayment> b)
    {
        b.ToTable("sale_payments", t => t.HasCheckConstraint("ck_sale_payments_amount_positive", "\"Amount\" > 0"));
        b.Property(p => p.Method).HasConversion<string>().HasMaxLength(10);
        b.Property(p => p.Amount).HasPrecision(14, 2);
        b.HasIndex(p => p.TenantId);
        b.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> b)
    {
        // lo pagado nunca supera el total (ultima barrera aunque el codigo se saltara la condicion del UPDATE)
        b.ToTable("purchases", t =>
        {
            t.HasCheckConstraint("ck_purchases_total_non_negative", "\"Total\" >= 0");
            t.HasCheckConstraint("ck_purchases_paid_within_total", "\"PaidAmount\" >= 0 AND \"PaidAmount\" <= \"Total\"");
        });
        b.Property(p => p.PaidAmount).HasPrecision(14, 2);
        b.Property(p => p.SupplierName).HasMaxLength(100).IsRequired();
        b.Property(p => p.SupplierInvoice).HasMaxLength(30);
        b.Property(p => p.Notes).HasMaxLength(300);
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(p => p.VoidedBy).HasMaxLength(80);
        b.Property(p => p.VoidReason).HasMaxLength(200);
        b.Property(p => p.CreatedByName).HasMaxLength(80).IsRequired();
        b.Property(p => p.Total).HasPrecision(14, 2);
        b.HasIndex(p => new { p.TenantId, p.Number }).IsUnique().HasDatabaseName("ux_purchases_tenant_number");
        b.HasIndex(p => new { p.TenantId, p.Date });
        b.HasIndex(p => p.SupplierId);
        b.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        // un proveedor con compras no se borra (se desactiva)
        b.HasOne<Supplier>().WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(p => p.Lines).WithOne().HasForeignKey(l => l.PurchaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseLineConfiguration : IEntityTypeConfiguration<PurchaseLine>
{
    public void Configure(EntityTypeBuilder<PurchaseLine> b)
    {
        b.ToTable("purchase_lines", t => t.HasCheckConstraint("ck_purchase_lines_quantity_positive", "\"Quantity\" > 0"));
        b.Property(l => l.ProductName).HasMaxLength(80).IsRequired();
        b.Property(l => l.UnitCost).HasPrecision(14, 2);
        b.Property(l => l.LineTotal).HasPrecision(16, 2);
        b.HasIndex(l => l.ProductId);
        b.HasIndex(l => l.TenantId);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> b)
    {
        b.ToTable("quotes");
        b.Property(q => q.CustomerName).HasMaxLength(100).IsRequired();
        b.Property(q => q.Notes).HasMaxLength(500);
        b.Property(q => q.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(q => q.CreatedByName).HasMaxLength(80).IsRequired();
        foreach (var money in new[] { nameof(Quote.Subtotal), nameof(Quote.TaxTotal), nameof(Quote.DiscountTotal), nameof(Quote.Total) })
            b.Property<decimal>(money).HasPrecision(14, 2);
        b.HasIndex(q => new { q.TenantId, q.Number }).IsUnique().HasDatabaseName("ux_quotes_tenant_number");
        b.HasIndex(q => q.CustomerId);
        b.HasOne<Tenant>().WithMany().HasForeignKey(q => q.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Customer>().WithMany().HasForeignKey(q => q.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Sale>().WithMany().HasForeignKey(q => q.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(q => q.Lines).WithOne().HasForeignKey(l => l.QuoteId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine>
{
    public void Configure(EntityTypeBuilder<QuoteLine> b)
    {
        b.ToTable("quote_lines", t => t.HasCheckConstraint("ck_quote_lines_quantity_positive", "\"Quantity\" > 0"));
        b.Property(l => l.ProductName).HasMaxLength(80).IsRequired();
        b.Property(l => l.Tax).HasConversion<string>().HasMaxLength(10);
        foreach (var money in new[] { nameof(QuoteLine.UnitPrice), nameof(QuoteLine.DiscountAmount), nameof(QuoteLine.LineTotal), nameof(QuoteLine.LineBase), nameof(QuoteLine.LineTax) })
            b.Property<decimal>(money).HasPrecision(14, 2);
        b.Property(l => l.DiscountPercent).HasPrecision(5, 2);
        b.HasIndex(l => l.ProductId);
        b.HasIndex(l => l.TenantId);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("expenses", t => t.HasCheckConstraint("ck_expenses_amount_positive", "\"Amount\" > 0"));
        b.Property(e => e.Category).HasConversion<string>().HasMaxLength(15);
        b.Property(e => e.Method).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Description).HasMaxLength(150).IsRequired();
        b.Property(e => e.Amount).HasPrecision(14, 2);
        b.Property(e => e.CreatedByName).HasMaxLength(80).IsRequired();
        b.Property(e => e.VoidedBy).HasMaxLength(80);
        b.Property(e => e.VoidReason).HasMaxLength(200);
        b.HasIndex(e => new { e.TenantId, e.Date });
        b.HasIndex(e => e.CashSessionId);
        b.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashSession>().WithMany().HasForeignKey(e => e.CashSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> b)
    {
        b.ToTable("supplier_payments", t => t.HasCheckConstraint("ck_supplier_payments_amount_positive", "\"Amount\" > 0"));
        b.Property(p => p.Method).HasConversion<string>().HasMaxLength(10);
        b.Property(p => p.Amount).HasPrecision(14, 2);
        b.Property(p => p.Reference).HasMaxLength(40);
        b.Property(p => p.CreatedByName).HasMaxLength(80).IsRequired();
        b.Property(p => p.VoidedBy).HasMaxLength(80);
        b.Property(p => p.VoidReason).HasMaxLength(200);
        b.HasIndex(p => p.PurchaseId);
        b.HasIndex(p => p.CashSessionId);
        b.HasIndex(p => new { p.TenantId, p.Date });
        b.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Purchase>().WithMany().HasForeignKey(p => p.PurchaseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Supplier>().WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashSession>().WithMany().HasForeignKey(p => p.CashSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);
        b.HasIndex(t => t.UserId);
        b.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.Property(u => u.FullName).HasMaxLength(80).IsRequired();
        b.HasIndex(u => u.TenantId);
        b.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
