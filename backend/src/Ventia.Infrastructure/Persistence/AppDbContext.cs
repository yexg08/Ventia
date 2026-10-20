using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Npgsql;
using Ventia.Domain.Common;
using Ventia.Domain.Entities;
using Ventia.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ventia.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IAppDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// indices unicos cuyo choque se responde como error del campo (dos peticiones al mismo tiempo pueden pasar la
    /// validacion de la app y entonces la base es la que decide)
    /// </summary>
    private static readonly Dictionary<string, (string Field, string Message)> UniqueFields = new()
    {
        [CategoryConfiguration.UniqueNameIndex] = ("Name", "Ya tienes una categoría con ese nombre."),
        [ProductConfiguration.UniqueCodeIndex] = ("Code", "Ya tienes un producto con ese código."),
        [ProductConfiguration.UniqueBarcodeIndex] = ("Barcode", "Ya tienes un producto con ese código de barras."),
        [CustomerConfiguration.UniqueDocumentIndex] = ("DocumentNumber", "Ya tienes un cliente con ese documento."),
        [SupplierConfiguration.UniqueDocumentIndex] = ("DocumentNumber", "Ya tienes un proveedor con ese documento.")
    };

    /// <summary>indices unicos cuyo choque es una regla de negocio (409)</summary>
    private static readonly Dictionary<string, string> UniqueRules = new()
    {
        [CashSessionConfiguration.OneOpenPerUserIndex] = "Ya tienes una caja abierta. Ciérrala antes de abrir otra."
    };

    /// <summary>
    /// lo lee el filtro global en cada consulta (EF Core lo toma de ESTA instancia del contexto y no lo fija al construir
    /// el modelo) y null significa sin negocio (el filtro no deja pasar ninguna fila)
    /// </summary>
    private Guid? CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // filtro de negocio en TODAS las entidades ITenantOwned (incluidas las que se agreguen en el futuro)
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType)) continue;
            typeof(AppDbContext)
                .GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [builder]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder builder) where T : class, ITenantOwned =>
        builder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException e) when (Translate(e) is { } translated)
        {
            throw translated;
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        }
        catch (DbUpdateException e) when (Translate(e) is { } translated)
        {
            throw translated;
        }
    }

    // choques de indices unicos que tienen un significado para el usuario
    private static Exception? Translate(DbUpdateException e)
    {
        if (e.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name })
            return null;
        if (name == SaleConfiguration.UniqueIdempotencyIndex) return new DuplicateRequestException();
        if (UniqueRules.TryGetValue(name, out var rule)) return new BusinessRuleException(rule);
        return UniqueField(e) is { } field ? new FieldValidationException(field.Field, field.Message) : null;
    }

    private static (string Field, string Message)? UniqueField(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name }
        && UniqueFields.TryGetValue(name, out var field)
            ? field
            : null;
}
