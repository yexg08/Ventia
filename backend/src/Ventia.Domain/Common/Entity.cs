namespace Ventia.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>lo llena el interceptor de timestamps (UTC)</summary>
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// dato que pertenece a un negocio - el filtro global de EF Core solo deja ver los del negocio de la sesion y el
/// interceptor de tenant llena el TenantId al crear y bloquea escrituras sobre datos de otro negocio
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
