namespace Ventia.Application.Common;

/// <summary>el recurso no existe o es de otro negocio (no se distingue a proposito) y responde 404</summary>
public class NotFoundException(string resource) : Exception($"{resource} no existe.");

/// <summary>se viola una regla de negocio y responde 409</summary>
public class BusinessRuleException(string message) : Exception(message);

/// <summary>dato invalido que solo se puede comprobar contra la base de datos (400 asociado al campo)</summary>
public class FieldValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>
/// llego otra vez una operacion con una clave de idempotencia ya usada (por ejemplo el mismo cobro dos veces)
/// quien la lanza devuelve el resultado de la primera
/// </summary>
public class DuplicateRequestException() : Exception("La operación ya se había registrado.");

/// <summary>credenciales o sesion invalidas y responde 401</summary>
public class AuthenticationFailedException(string message) : Exception(message);
