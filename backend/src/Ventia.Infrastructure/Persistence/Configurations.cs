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
