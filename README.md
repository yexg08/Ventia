# Ventia

[![CI](https://github.com/yexg08/Ventia/actions/workflows/ci.yml/badge.svg)](https://github.com/yexg08/Ventia/actions/workflows/ci.yml)
[![Licencia: MIT](https://img.shields.io/badge/licencia-MIT-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512bd4)
![Angular 22](https://img.shields.io/badge/Angular-22-dd0031)
![PostgreSQL 17](https://img.shields.io/badge/PostgreSQL-17-336791)

**Ventas e inventario multi-negocio para comida rápida.** Ventia es una demo de AndesAiSolutions: un punto de venta
con inventario y reportes, donde cada negocio tiene su propio panel y sus datos quedan aislados de los demás.

> **Es una demo.** Los comprobantes no tienen validez fiscal ante la DIAN (no hay facturación electrónica, nómina
> electrónica ni documentos soporte) y no se procesan pagos reales: efectivo, tarjeta y transferencia solo se registran.

## Estado

| Fase | Contenido | Estado |
|---|---|---|
| 1 | Base: registro de negocios, login, roles dueño y cajero, cuentas de cajeros, configuración del negocio, categorías, superadmin, Docker y CI | Hecha |
| 2 | Catálogo e inventario: productos, movimientos de stock, stock que nunca queda negativo | Hecha |
| 3 | Clientes y proveedores (NIT con dígito de verificación) | Hecha |
| 4 | Punto de venta: carrito, descuentos, pago mixto, consecutivo, anulación, caja, tipo de pedido, notas y adiciones | Pendiente |
| 5 | Compras, cotizaciones, órdenes y remisiones (costo promedio) | Pendiente |
| 6 | Gastos y pagos a proveedores | Pendiente |
| 7 | Reportes y exportación a CSV | Pendiente |
| 8 | Pulido: datos de ejemplo, README bilingüe, capturas | Pendiente |
| 9 | Diseño final: moderno, minimalista y con animaciones suaves | Pendiente |

## Stack

| Capa | Tecnología |
|---|---|
| Backend | .NET 10, ASP.NET Core, EF Core 10, ASP.NET Identity, FluentValidation |
| Base de datos | PostgreSQL 17 |
| Frontend | Angular 22 (zoneless, standalone, signals, `httpResource`), Tailwind CSS v4, Angular CDK, Heroicons |
| Pruebas | xUnit + `WebApplicationFactory` + **Testcontainers** (PostgreSQL real) · Vitest |
| Infraestructura | Docker (imagen única: la API sirve el frontend compilado) · GitHub Actions · Dependabot |

## Lo que ya hace

- **Multi-negocio con aislamiento en tres barreras**, la misma base probada en Flow:
  - El negocio de la sesión sale de la base de datos en cada petición (`SessionValidator`), nunca del token ni del cliente.
  - Un filtro global de EF Core limita toda entidad `ITenantOwned` al negocio de la sesión. Pedir por id un dato de otro negocio da 404.
  - Un interceptor de escritura rechaza crear, modificar o borrar datos de otro negocio.
- **Roles:**
  - **Dueño:** administra todo su negocio.
  - **Cajero:** vende y ve el menú, pero no ve costos ni cambia la configuración.
  - **Superadmin:** ve todos los negocios y puede suspenderlos.
- **Cuentas de cajeros:**
  - El dueño las crea con una contraseña temporal que se ve una sola vez y vence a los 7 días.
  - En el primer ingreso es obligatorio cambiarla, y eso lo aplica la API, no solo la pantalla.
  - El dueño puede generar otra contraseña o desactivar la cuenta, y en ambos casos las sesiones del cajero se cortan al instante.
  - Las cuentas no se borran, porque las ventas quedarán a nombre de quien las hizo.
- **Configuración del negocio:**
  - NIT validado con el algoritmo de la DIAN, en el backend y en el navegador.
  - Dirección, teléfono, zona horaria y mensaje al pie del comprobante.
  - Impuesto por defecto: INC 8 % (comidas rápidas), IVA 19 %, IVA 5 % o sin impuesto.
- **Categorías del menú**, con orden, activas o inactivas y nombres únicos sin importar mayúsculas (también protegido en la base).
- **Productos (fase 2):**
  - Precio con el impuesto incluido. La base y el impuesto se separan con una sola regla de redondeo, y base más impuesto siempre da el precio cobrado (con pruebas).
  - Costo y margen sobre el precio sin impuesto, que **solo ve el dueño**. El servidor no los envía al cajero, no basta con ocultar la columna.
  - Código y código de barras opcionales, únicos por negocio (también protegidos en la base).
  - Búsqueda, filtro por categoría y filtro de stock bajo.
  - Un producto con movimientos no se borra, solo se desactiva. Una categoría con productos tampoco se borra.
- **Inventario (fase 2):**
  - Cada producto decide si lleva inventario: una gaseosa sí, una hamburguesa preparada no.
  - Entradas, salidas y conteos físicos con motivo. Cada cambio queda como movimiento con quién lo hizo y el stock que quedó.
  - **El stock nunca queda negativo, ni con salidas simultáneas:** el descuento es un solo `UPDATE` con la condición adentro, y una prueba lanza 8 salidas a la vez sobre un stock de 5 y solo entran 5. Además, una restricción `CHECK` de PostgreSQL rechaza el stock negativo aunque el código se equivocara.
  - El stock siempre es igual a la suma de sus movimientos (con prueba).
  - El mismo servicio de inventario lo usarán las ventas de la fase 4, dentro de la misma transacción.
- **Clientes y proveedores (fase 3):**
  - Documento de identidad (cédula, cédula de extranjería, NIT o pasaporte) validado y normalizado igual en el backend y en el navegador. El NIT se valida con el dígito de verificación.
  - El documento es único por negocio (también protegido en la base), pero dos negocios pueden tener el mismo cliente.
  - **Autorización de datos (Ley 1581):** es obligatoria para las personas y queda la fecha en que se dio. Las empresas con NIT no la necesitan.
  - El cliente puede pedir que se borren sus datos (derecho de supresión): el dueño lo elimina.
  - Los cajeros registran y editan clientes desde la caja. Solo el dueño los borra y gestiona los proveedores.
  - Búsqueda por nombre, documento o celular.
- **Seguridad:**
  - Denegar por defecto, con una prueba que recorre todos los endpoints con cada rol.
  - Bloqueo tras 5 intentos fallidos y límite de peticiones en login y registro.
  - Mismo mensaje para un correo inexistente que para una contraseña incorrecta.
  - JWT de 15 minutos en memoria y refresh token rotado en una cookie `httpOnly`, `Secure` y `SameSite=Strict`.
  - Cabeceras de seguridad, con una política de contenido que en el frontend autoriza solo sus propios scripts por hash.

## Probarlo con Docker

Solo necesitas [Docker Desktop](https://www.docker.com/products/docker-desktop/).

```bash
# 1. crea tu archivo .env con una clave aleatoria (no se sube a Git)
cat > .env <<EOF
JWT_KEY=$(openssl rand -hex 32)
SUPERADMIN_PASSWORD=Admin1$(openssl rand -hex 6)
EOF

# 2. construye y arranca todo
docker compose --profile app up --build
```

Abre http://localhost:8081 (usa Chrome, Edge o Firefox) y crea tu negocio. El superadmin es `admin@ventia.local`
con la contraseña de tu `.env`. Para empezar de cero: `docker compose --profile app down -v`.

## Desarrollo local

Requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js](https://nodejs.org/) 24+ y
[Docker Desktop](https://www.docker.com/products/docker-desktop/).

```bash
# 1. base de datos (PostgreSQL en el puerto 5435)
docker compose up -d

# 2. secretos de la API (sin la clave JWT la API no arranca)
cd backend/src/Ventia.Api
dotnet user-secrets set "Jwt:Key" "<texto aleatorio de 32 caracteres o más>"
dotnet user-secrets set "Seed:SuperAdminPassword" "<mínimo 10 caracteres, con letras y números>"

# 3. API (http://localhost:5180), aplica las migraciones sola al arrancar
dotnet run

# 4. en otra terminal: frontend (http://localhost:4380)
cd frontend
npm install
npm start
```

## Pruebas

```bash
# backend (necesita Docker encendido: Testcontainers levanta un PostgreSQL desechable)
cd backend
dotnet test

# frontend
cd frontend
npx ng test --watch=false
```

GitHub Actions corre en cada push:
- compilación y pruebas del backend y del frontend;
- auditoría de dependencias de NuGet y npm;
- construcción de la imagen de Docker con una prueba de humo: registro, login del superadmin y cabeceras de seguridad.

## Estructura

```
backend/
  src/
    Ventia.Domain/          Entidades (Tenant, Category, Product, StockMovement…), impuestos e ITenantOwned
    Ventia.Application/     Casos de uso, DTOs, validaciones, NIT y documentos, servicio de inventario
    Ventia.Infrastructure/  EF Core + Identity, filtro e interceptor de negocio, auth, cuentas, migraciones
    Ventia.Api/             Controladores, políticas, validación de sesión, cabeceras, límites, frontend compilado
  tests/Ventia.Tests/       Integración contra PostgreSQL real + unitarias
frontend/
  src/app/
    core/                   Sesión, interceptores, guards, tema, avisos
    shared/                 Logo, diálogo de confirmación, formatos, NIT y documentos, desglose de impuestos
    features/               landing, auth, app (panel del negocio), admin
```

## Licencia

[MIT](LICENSE)
