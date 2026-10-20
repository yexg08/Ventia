namespace Ventia.Application.Common;

public static class Roles
{
    /// <summary>dueno de la plataforma (ve todos los negocios y no pertenece a ninguno)</summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>dueno de un negocio (lo administra por completo)</summary>
    public const string Owner = "Owner";

    /// <summary>cajero de un negocio (vende pero no ve costos ni administra el negocio)</summary>
    public const string Cashier = "Cashier";

    public static readonly string[] TenantMembers = [Owner, Cashier];
    public static readonly string[] All = [SuperAdmin, Owner, Cashier];
}
