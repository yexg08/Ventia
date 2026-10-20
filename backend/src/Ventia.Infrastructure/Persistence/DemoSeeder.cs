using Ventia.Application.Abstractions;
using Ventia.Application.Common;
using Ventia.Domain.Common;
using Ventia.Domain.Entities;
using Ventia.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ventia.Infrastructure.Persistence;

/// <summary>
/// datos de ejemplo para probar Ventia en local (una hamburgueseria de Manizales con 30 dias de ventas y compras y
/// gastos y cierres de caja)
/// solo se cargan si se activa la opcion DemoData de Seed y no se repiten (si la cuenta del dueno ya existe se salta)
/// todo se arma con las mismas reglas de la aplicacion (stock igual a la suma de sus movimientos y consecutivos sin
/// saltos y costo promedio y efectivo esperado de cada caja) y las fechas se cuentan desde hoy en la hora del negocio
/// </summary>
public static class DemoSeeder
{
    public const string OwnerEmail = "dueno@ventia.demo";
    public const string CashierEmail = "cajera@ventia.demo";
    public const string BusinessName = "Hamburguesas La Cumbre";

    private const int PastDays = 30;
    private const string TimeZoneId = "America/Bogota";

    private sealed record ProductSpec(
        string Code, string Name, int Category, decimal Price, decimal Cost, int Weight, bool Tracks = false, int Stock = 0, int MinStock = 0);

    private static readonly string[] Categories = ["Hamburguesas", "Perros y salchipapas", "Bebidas", "Adiciones"];

    // el peso es que tan seguido se pide (las bebidas se agregan aparte)
    private static readonly ProductSpec[] Products =
    [
        new("H01", "Hamburguesa clásica", 0, 16000, 6200, 30),
        new("H02", "Hamburguesa doble carne", 0, 22000, 9500, 16),
        new("H03", "Hamburguesa de pollo apanado", 0, 18000, 7000, 12),
        new("H04", "Hamburguesa La Cumbre", 0, 25000, 10500, 10),
        new("P01", "Perro sencillo", 1, 10000, 3800, 12),
        new("P02", "Perro americano", 1, 14000, 5600, 10),
        new("S01", "Salchipapa personal", 1, 13000, 4900, 10),
        new("S02", "Salchipapa para compartir", 1, 28000, 11000, 5),
        new("S03", "Papas a la francesa", 1, 6000, 2000, 8),
        new("B01", "Gaseosa 400 ml", 2, 4000, 2100, 40, Tracks: true, Stock: 96, MinStock: 24),
        new("B02", "Gaseosa 1,5 L", 2, 8000, 4500, 8, Tracks: true, Stock: 24, MinStock: 6),
        new("B03", "Agua 600 ml", 2, 3000, 1300, 10, Tracks: true, Stock: 36, MinStock: 12),
        new("B04", "Cerveza nacional", 2, 6000, 3200, 10, Tracks: true, Stock: 48, MinStock: 12),
        new("B05", "Jugo natural en agua", 2, 6000, 2000, 14),
        new("B06", "Limonada de coco", 2, 9000, 3500, 8),
        new("A01", "Queso extra", 3, 3000, 900, 5),
        new("A02", "Tocineta extra", 3, 4000, 1600, 4),
        new("A03", "Huevo frito", 3, 2000, 600, 3)
    ];

    private static readonly (string Name, string Document, string Phone, string Address)[] Customers =
    [
        ("Juan Pablo Cardona", "1053778412", "3104567821", "Calle 65 # 23-40"),
        ("Valentina Ríos", "1053812345", "3126549870", "Carrera 23 # 70-15 apto 302"),
        ("Andrés Felipe Osorio", "75102938", "3001239876", "Barrio La Francia, casa 12"),
        ("Laura Jaramillo", "1053790011", "3157894561", "Calle 48 # 25-10"),
        ("Santiago Henao", "1002456789", "3216547898", "Carrera 27 # 65-32"),
        ("Daniela Castaño", "1053866002", "3009871234", "Avenida Santander # 55-80"),
        ("Camilo Betancur", "16076543", "3117893214", "Barrio Palermo, calle 60 # 21-14"),
        ("Mariana Quintero", "1053899112", "3168529637", "Carrera 24 # 52-08")
    ];

    private static readonly string[] Addresses =
    [
        "Calle 66 # 23-50", "Carrera 22 # 58-11", "Barrio Chipre, calle 12 # 9-30", "Avenida Paralela # 61-20",
        "Barrio Los Rosales, casa 4", "Carrera 25 # 48-17 apto 501", "Calle 70 # 24-05"
    ];

    // de las 11 de la manana a las 9 de la noche con picos al almuerzo y en la noche
    private static readonly (int Hour, int Weight)[] Hours =
        [(11, 2), (12, 6), (13, 7), (14, 3), (15, 1), (16, 1), (17, 3), (18, 6), (19, 9), (20, 7), (21, 3)];

    private sealed record Seller(Guid Id, string Name);

    public static async Task SeedAsync(IServiceProvider services, string password, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DemoSeeder));
        var clock = sp.GetRequiredService<TimeProvider>();

        if (await users.FindByEmailAsync(OwnerEmail) is not null)
        {
            logger.LogInformation("Datos de ejemplo: ya existen, se omiten.");
            return;
        }

        var zone = BusinessTime.Zone(TimeZoneId);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var today = BusinessTime.Today(zone, clock);
        var random = new Random(2026);
        var stamps = new List<(Entity Entity, DateTime At)>();
        DateTime At(DateOnly day, int hour, int minute = 0) =>
            BusinessTime.TryToUtc(day.ToDateTime(new TimeOnly(hour, minute)), zone, out var utc) ? utc : BusinessTime.StartOfDayUtc(day, zone);
        var firstDay = today.AddDays(-PastDays);
        var start = At(firstDay, 9);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var tenant = new Tenant
        {
            Name = BusinessName,
            TaxId = "900123456-8",
            Address = "Carrera 23 # 62-15",
            City = "Manizales",
            Phone = "3001112233",
            TimeZone = TimeZoneId,
            ReceiptFooter = "¡Gracias por tu compra! Vuelve pronto."
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
        sp.GetRequiredService<ITenantContext>().Set(tenant.Id);
        stamps.Add((tenant, start));

        var owner = await CreateUserAsync(users, OwnerEmail, "Andrés Ospina", tenant.Id, Roles.Owner, password, start);
        var cashier = await CreateUserAsync(users, CashierEmail, "Laura Cardona", tenant.Id, Roles.Cashier, password, start);

        // catalogo
        var categories = Categories.Select((name, i) => new Category { Name = name, SortOrder = i, IsActive = true }).ToList();
        db.Categories.AddRange(categories);
        var products = Products.Select(p => new Product
        {
            CategoryId = categories[p.Category].Id,
            Name = p.Name,
            Code = p.Code,
            Price = p.Price,
            Cost = p.Cost,
            Tax = TaxType.Inc8,
            TracksStock = p.Tracks,
            Stock = p.Stock,
            MinStock = p.MinStock,
            IsActive = true
        }).ToList();
        db.Products.AddRange(products);
        stamps.AddRange(categories.Select(c => ((Entity)c, start)));
        stamps.AddRange(products.Select(p => ((Entity)p, start)));
        var spec = products.Zip(Products).ToDictionary(x => x.First.Id, x => x.Second);
        var byCode = products.ToDictionary(p => p.Code!);

        foreach (var product in products.Where(p => p.TracksStock))
            AddMovement(db, stamps, product, StockMovementType.Initial, product.Stock, "Inventario inicial", owner, start);

        // proveedores y clientes
        var drinks = new Supplier
        {
            DocumentType = DocumentType.NIT, DocumentNumber = "800197268-4", Name = "Distribuidora de Bebidas del Eje S.A.S.",
            ContactName = "Carolina Mejía", Phone = "3205551234", Email = "pedidos@bebidasdeleje.demo", IsActive = true
        };
        var meats = new Supplier
        {
            DocumentType = DocumentType.NIT, DocumentNumber = $"900456789-{Nit.CheckDigit("900456789")}", Name = "Carnes La Pradera",
            ContactName = "Jorge Salazar", Phone = "3135559876", IsActive = true, Notes = "Entrega martes y viernes."
        };
        var bakery = new Supplier
        {
            DocumentType = DocumentType.NIT, DocumentNumber = "860002964-4", Name = "Panadería El Trigal",
            ContactName = "Óscar Duque", Phone = "3145557788", IsActive = true, Notes = "Pan de hamburguesa y de perro cada mañana."
        };
        db.Suppliers.AddRange(drinks, meats, bakery);
        stamps.AddRange([(drinks, start), (meats, start), (bakery, start)]);

        var customers = Customers.Select((c, i) => new Customer
        {
            DocumentType = DocumentType.CC, DocumentNumber = c.Document, Name = c.Name, Phone = c.Phone, Address = c.Address,
            DataConsentAt = At(firstDay.AddDays(i * 3), 13).ToMicroseconds()
        }).ToList();
        var company = new Customer
        {
            DocumentType = DocumentType.NIT, DocumentNumber = $"901234567-{Nit.CheckDigit("901234567")}", Name = "Constructora Andina S.A.S.",
            Phone = "6068851200", Email = "compras@constructoraandina.demo", Address = "Carrera 21 # 30-05, oficina 402",
            Notes = "Pide para eventos de la obra."
        };
        db.Customers.AddRange(customers);
        db.Customers.Add(company);
        stamps.AddRange(customers.Select(c => ((Entity)c, c.DataConsentAt!.Value)));
        stamps.Add((company, At(firstDay.AddDays(5), 10)));
        await db.SaveChangesAsync(ct);

        // dias de historia (cada dia una compra si toca y los gastos y un turno de caja con sus ventas)
        var saleNumber = 0;
        var purchaseNumber = 0;
        for (var day = firstDay.AddDays(1); day <= today; day = day.AddDays(1))
        {
            var isToday = day == today;
            // la cajera descansa los lunes y ese dia vende el dueno (hoy siempre el dueno para que la demo abra lista)
            var seller = isToday || day.DayOfWeek == DayOfWeek.Monday ? new Seller(owner.Id, owner.FullName) : new Seller(cashier.Id, cashier.FullName);

            if ((today.DayNumber - day.DayNumber) % 7 == 2)
                purchaseNumber = AddPurchase(db, stamps, ++purchaseNumber, day, today, drinks, byCode, owner, random, At);

            AddExpenses(db, stamps, day, firstDay, owner, At);

            var session = new CashSession { UserId = seller.Id, UserName = seller.Name, OpeningCash = 150000 };
            db.CashSessions.Add(session);
            stamps.Add((session, At(day, 10, 45)));

            var cashSales = 0m;
            foreach (var at in SaleTimes(day, random, At).Where(t => t < nowUtc))
            {
                var sale = BuildSale(++saleNumber, session, seller, at, products, spec, customers, random);
                foreach (var line in sale.Lines.Where(l => byCode.Values.First(p => p.Id == l.ProductId).TracksStock))
                {
                    var product = products.First(p => p.Id == line.ProductId);
                    AddMovement(db, stamps, product, StockMovementType.Sale, -line.Quantity, $"Venta {sale.Number}", seller, at, sale.Id);
                }
                db.Sales.Add(sale);
                stamps.Add((sale, at));

                // algunas ventas se anulan (devuelven lo que descontaron y no cuentan en la caja)
                if (random.Next(100) < 2)
                {
                    sale.Status = SaleStatus.Voided;
                    sale.VoidedAt = at.AddMinutes(4).ToMicroseconds();
                    sale.VoidedBy = owner.FullName;
                    sale.VoidReason = random.Next(2) == 0 ? "Pedido registrado dos veces" : "El cliente canceló el pedido";
                    foreach (var line in sale.Lines.Where(l => products.First(p => p.Id == l.ProductId).TracksStock))
                    {
                        var product = products.First(p => p.Id == line.ProductId);
                        AddMovement(db, stamps, product, StockMovementType.SaleVoid, line.Quantity, $"Anulación venta {sale.Number}", owner, at.AddMinutes(4), sale.Id);
                    }
                    continue;
                }
                cashSales += sale.Payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
            }

            // salidas de efectivo de la caja (hielo o un domicilio extra)
            var cashOut = 0m;
            if (day.DayNumber % 3 == 0)
            {
                var expense = new Expense
                {
                    Date = day, Category = ExpenseCategory.Supplies, Description = day.DayNumber % 2 == 0 ? "Hielo y servilletas" : "Domiciliario de apoyo",
                    Amount = day.DayNumber % 2 == 0 ? 12000 : 25000, Method = PaymentMethod.Cash, CashSessionId = session.Id,
                    CreatedById = seller.Id, CreatedByName = seller.Name
                };
                if (!isToday || At(day, 16) < nowUtc)
                {
                    db.Expenses.Add(expense);
                    stamps.Add((expense, At(day, 16)));
                    cashOut = expense.Amount;
                }
            }

            if (!isToday)
            {
                session.ExpectedCash = session.OpeningCash + cashSales - cashOut;
                session.CountedCash = session.ExpectedCash + (random.Next(10) switch { 0 => -2000, 1 => 1000, 2 => -500, _ => 0 });
                session.ClosedAt = At(day, 22, 10).ToMicroseconds();
                if (session.CountedCash < session.ExpectedCash) session.CloseNotes = "Faltó cambio en monedas.";
            }

            await db.SaveChangesAsync(ct);
        }

        AddQuotes(db, stamps, today, company, customers, products, owner, At);
        tenant.LastSaleNumber = saleNumber;
        tenant.LastPurchaseNumber = purchaseNumber;
        tenant.LastQuoteNumber = 3;
        await db.SaveChangesAsync(ct);

        await BackdateAsync(db, stamps, ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Datos de ejemplo cargados: {Sales} ventas en {Days} días.", saleNumber, PastDays);
    }

    private static async Task<AppUser> CreateUserAsync(
        UserManager<AppUser> users, string email, string name, Guid tenantId, string role, string password, DateTime createdAt)
    {
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = name, TenantId = tenantId, CreatedAt = createdAt };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException($"No se pudo crear {email}: {string.Join(" ", created.Errors.Select(e => e.Description))}");
        await users.AddToRoleAsync(user, role);
        return user;
    }

    private static void AddMovement(
        AppDbContext db, List<(Entity, DateTime)> stamps, Product product, StockMovementType type, int quantity, string reason,
        AppUser user, DateTime at, Guid? saleId = null, Guid? purchaseId = null) =>
        AddMovement(db, stamps, product, type, quantity, reason, new Seller(user.Id, user.FullName), at, saleId, purchaseId);

    private static void AddMovement(
        AppDbContext db, List<(Entity, DateTime)> stamps, Product product, StockMovementType type, int quantity, string reason,
        Seller user, DateTime at, Guid? saleId = null, Guid? purchaseId = null)
    {
        // el inicial ya viene en el stock del producto
        if (type != StockMovementType.Initial) product.Stock += quantity;
        var movement = new StockMovement
        {
            ProductId = product.Id, Type = type, Quantity = quantity, StockAfter = product.Stock, Reason = reason,
            SaleId = saleId, PurchaseId = purchaseId, UserId = user.Id, UserName = user.Name
        };
        db.StockMovements.Add(movement);
        stamps.Add((movement, at));
    }

    /// <summary>horas de las ventas del dia (los viernes y sabados se vende mas)</summary>
    private static IEnumerable<DateTime> SaleTimes(DateOnly day, Random random, Func<DateOnly, int, int, DateTime> at)
    {
        var count = day.DayOfWeek switch
        {
            DayOfWeek.Friday or DayOfWeek.Saturday => 34,
            DayOfWeek.Sunday => 28,
            DayOfWeek.Monday => 16,
            _ => 22
        } + random.Next(-4, 5);
        var totalWeight = Hours.Sum(h => h.Weight);
        var times = new List<DateTime>();
        for (var i = 0; i < count; i++)
        {
            var pick = random.Next(totalWeight);
            var hour = Hours.First(h => (pick -= h.Weight) < 0).Hour;
            times.Add(at(day, hour, random.Next(60)).AddSeconds(random.Next(60)));
        }
        return times.Order();
    }

    private static T Pick<T>(IReadOnlyList<T> items, Func<T, int> weight, Random random)
    {
        var pick = random.Next(items.Sum(weight));
        return items.First(i => (pick -= weight(i)) < 0);
    }

    private static Sale BuildSale(
        int number, CashSession session, Seller seller, DateTime at, List<Product> products, Dictionary<Guid, ProductSpec> spec,
        List<Customer> customers, Random random)
    {
        var food = products.Where(p => spec[p.Id].Category is 0 or 1).ToList();
        var drinks = products.Where(p => spec[p.Id].Category == 2).ToList();
        var addOns = products.Where(p => spec[p.Id].Category == 3).ToList();
        var lines = new List<(Product Product, int Quantity, int? Parent)>();

        for (var i = 0; i < (random.Next(10) < 7 ? 1 : 2); i++)
        {
            var main = Pick(food, p => spec[p.Id].Weight, random);
            lines.Add((main, random.Next(8) == 0 ? 2 : 1, null));
            if (spec[main.Id].Category == 0 && random.Next(5) == 0)
                lines.Add((addOns[random.Next(addOns.Count)], 1, lines.Count - 1));
        }
        for (var i = 0; i < (random.Next(10) < 7 ? 1 : 0) + (random.Next(10) < 2 ? 1 : 0); i++)
        {
            var drink = Pick(drinks, p => spec[p.Id].Weight, random);
            var quantity = random.Next(4) == 0 ? 2 : 1;
            // si la bebida no alcanza se cambia por un jugo (que no lleva inventario)
            if (drink.TracksStock && drink.Stock - lines.Where(l => l.Product.Id == drink.Id).Sum(l => l.Quantity) < quantity)
                drink = drinks.First(d => !d.TracksStock);
            lines.Add((drink, quantity, null));
        }

        var orderType = random.Next(100) switch { < 55 => OrderType.TakeAway, < 85 => OrderType.DineIn, _ => OrderType.Delivery };
        var customer = orderType == OrderType.Delivery || random.Next(100) < 10 ? customers[random.Next(customers.Count)] : null;
        var sale = new Sale
        {
            Number = number,
            CashSessionId = session.Id,
            SellerId = seller.Id,
            SellerName = seller.Name,
            OrderType = orderType,
            TableLabel = orderType == OrderType.DineIn ? $"Mesa {1 + random.Next(8)}" : null,
            DeliveryAddress = orderType == OrderType.Delivery ? customer?.Address ?? Addresses[random.Next(Addresses.Length)] : null,
            CustomerId = customer?.Id,
            CustomerName = customer?.Name ?? "Consumidor final",
            CustomerDocument = customer is null ? null : $"{customer.DocumentType} {customer.DocumentNumber}",
            IdempotencyKey = Guid.NewGuid()
        };

        var discountPercent = random.Next(100) < 4 ? 10m : 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            var (product, quantity, _) = lines[i];
            var gross = product.Price * quantity;
            var discount = Math.Round(gross * discountPercent / 100, 2, MidpointRounding.AwayFromZero);
            var split = Taxes.Split(gross - discount, product.Tax);
            sale.Lines.Add(new SaleLine
            {
                SaleId = sale.Id, Position = i, ProductId = product.Id, ProductName = product.Name, Quantity = quantity,
                UnitPrice = product.Price, DiscountPercent = discountPercent, DiscountAmount = discount, Tax = product.Tax,
                LineTotal = gross - discount, LineBase = split.Base, LineTax = split.Tax, UnitCost = product.Cost,
                Note = spec[product.Id].Category == 0 && random.Next(8) == 0 ? "Sin cebolla" : null
            });
        }
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Parent is { } parent) sale.Lines[i].ParentLineId = sale.Lines[parent].Id;

        sale.Subtotal = sale.Lines.Sum(l => l.LineBase);
        sale.TaxTotal = sale.Lines.Sum(l => l.LineTax);
        sale.DiscountTotal = sale.Lines.Sum(l => l.DiscountAmount);
        sale.Total = sale.Lines.Sum(l => l.LineTotal);

        var method = random.Next(100) switch { < 50 => PaymentMethod.Cash, < 75 => PaymentMethod.Card, _ => PaymentMethod.Transfer };
        sale.Payments.Add(new SalePayment { SaleId = sale.Id, Method = method, Amount = sale.Total });
        if (method == PaymentMethod.Cash)
        {
            // el cliente paga con un billete redondo
            var bill = new[] { 10000m, 20000m, 50000m, 100000m }.First(b => b >= sale.Total || b == 100000m);
            sale.CashTendered = Math.Max(sale.Total, Math.Ceiling(sale.Total / bill) * bill);
            sale.Change = sale.CashTendered - sale.Total;
        }
        return sale;
    }

    /// <summary>compra semanal de bebidas (las viejas pagadas y las dos ultimas a credito con un abono en la penultima)</summary>
    private static int AddPurchase(
        AppDbContext db, List<(Entity, DateTime)> stamps, int number, DateOnly day, DateOnly today, Supplier supplier,
        Dictionary<string, Product> byCode, AppUser owner, Random random, Func<DateOnly, int, int, DateTime> at)
    {
        var purchase = new Purchase
        {
            Number = number, SupplierId = supplier.Id, SupplierName = supplier.Name, Date = day,
            SupplierInvoice = $"FV-{4100 + number * 7}", CreatedByName = owner.FullName
        };
        var items = new (string Code, int Quantity, decimal UnitCost)[]
        {
            ("B01", 72, 2100 + random.Next(0, 3) * 50), ("B02", 12, 4500), ("B03", 12, 1300), ("B04", 24, 3200 + random.Next(0, 2) * 100)
        };
        var when = at(day, 9, 30);
        for (var i = 0; i < items.Length; i++)
        {
            var (code, quantity, unitCost) = items[i];
            var product = byCode[code];
            purchase.Lines.Add(new PurchaseLine
            {
                PurchaseId = purchase.Id, Position = i, ProductId = product.Id, ProductName = product.Name, Quantity = quantity,
                UnitCost = unitCost, LineTotal = quantity * unitCost
            });
            // costo promedio ponderado igual que PurchasesService
            product.Cost = product.Stock <= 0 || product.Cost is null
                ? unitCost
                : Math.Round((product.Stock * product.Cost.Value + quantity * unitCost) / (product.Stock + quantity), 2, MidpointRounding.AwayFromZero);
            AddMovement(db, stamps, product, StockMovementType.Purchase, quantity, $"Compra {number}", owner, when, purchaseId: purchase.Id);
        }
        purchase.Total = purchase.Lines.Sum(l => l.LineTotal);
        db.Purchases.Add(purchase);
        stamps.Add((purchase, when));

        var daysAgo = today.DayNumber - day.DayNumber;
        var paid = daysAgo switch { <= 2 => 0m, <= 9 => Math.Round(purchase.Total * 0.4m, 0), _ => purchase.Total };
        if (paid > 0)
        {
            var payment = new SupplierPayment
            {
                PurchaseId = purchase.Id, SupplierId = supplier.Id, Date = daysAgo <= 9 ? day.AddDays(1) : day, Amount = paid,
                Method = PaymentMethod.Transfer, Reference = $"TRF-{88000 + number * 13}", CreatedByName = owner.FullName
            };
            purchase.PaidAmount = paid;
            db.SupplierPayments.Add(payment);
            stamps.Add((payment, at(payment.Date, 10, 15)));
        }
        return number;
    }

    /// <summary>gastos fijos del mes y de cada semana (los insumos de cocina ya van en el costo de cada producto)</summary>
    private static void AddExpenses(
        AppDbContext db, List<(Entity, DateTime)> stamps, DateOnly day, DateOnly firstDay, AppUser owner, Func<DateOnly, int, int, DateTime> at)
    {
        var offset = day.DayNumber - firstDay.DayNumber;
        var items = new List<(ExpenseCategory Category, string Description, decimal Amount, PaymentMethod Method)>();
        if (offset == 1) items.Add((ExpenseCategory.Rent, "Arriendo del local", 2800000, PaymentMethod.Transfer));
        if (offset == 4) items.Add((ExpenseCategory.Utilities, "Energía", 420000, PaymentMethod.Transfer));
        if (offset == 5) items.Add((ExpenseCategory.Utilities, "Agua y alcantarillado", 160000, PaymentMethod.Transfer));
        if (offset == 6) items.Add((ExpenseCategory.Utilities, "Internet y teléfono", 95000, PaymentMethod.Card));
        if (offset is 14 or 29) items.Add((ExpenseCategory.Payroll, "Quincena auxiliar de cocina", 750000, PaymentMethod.Transfer));
        if (offset is 14 or 29) items.Add((ExpenseCategory.Payroll, "Quincena cajera", 750000, PaymentMethod.Transfer));
        if (offset % 7 == 3) items.Add((ExpenseCategory.Supplies, "Empaques, vasos y servilletas", 185000, PaymentMethod.Card));
        if (offset % 10 == 8) items.Add((ExpenseCategory.Utilities, "Pipeta de gas", 210000, PaymentMethod.Transfer));
        if (offset == 18) items.Add((ExpenseCategory.Marketing, "Publicidad en redes sociales", 150000, PaymentMethod.Card));
        if (offset == 22) items.Add((ExpenseCategory.Maintenance, "Mantenimiento de la freidora", 120000, PaymentMethod.Transfer));

        foreach (var (category, description, amount, method) in items)
        {
            var expense = new Expense
            {
                Date = day, Category = category, Description = description, Amount = amount, Method = method,
                CreatedById = owner.Id, CreatedByName = owner.FullName
            };
            db.Expenses.Add(expense);
            stamps.Add((expense, at(day, 9, 0)));
        }
    }

    private static void AddQuotes(
        AppDbContext db, List<(Entity, DateTime)> stamps, DateOnly today, Customer company, List<Customer> customers,
        List<Product> products, AppUser owner, Func<DateOnly, int, int, DateTime> at)
    {
        Product P(string name) => products.First(p => p.Name == name);
        var specs = new (int Number, Customer? Customer, string Name, int DaysAgo, int ValidDays, QuoteStatus Status, (Product, int)[] Lines, string? Notes)[]
        {
            (1, company, company.Name, 12, 10, QuoteStatus.Cancelled, [(P("Hamburguesa clásica"), 30), (P("Gaseosa 400 ml"), 30)], "Almuerzo de la obra (se aplazó)."),
            (2, company, company.Name, 2, 15, QuoteStatus.Open, [(P("Hamburguesa La Cumbre"), 25), (P("Papas a la francesa"), 25), (P("Gaseosa 400 ml"), 25)], "Entrega en la obra a las 12:30."),
            (3, customers[1], customers[1].Name, 1, 7, QuoteStatus.Open, [(P("Salchipapa para compartir"), 4), (P("Perro americano"), 10), (P("Limonada de coco"), 10)], "Cumpleaños, recogen en el local.")
        };
        foreach (var s in specs)
        {
            var quote = new Quote
            {
                Number = s.Number, CustomerId = s.Customer?.Id, CustomerName = s.Name, ValidUntil = today.AddDays(s.ValidDays - s.DaysAgo),
                Status = s.Status, Notes = s.Notes, CreatedByName = owner.FullName
            };
            for (var i = 0; i < s.Lines.Length; i++)
            {
                var (product, quantity) = s.Lines[i];
                var total = product.Price * quantity;
                var split = Taxes.Split(total, product.Tax);
                quote.Lines.Add(new QuoteLine
                {
                    QuoteId = quote.Id, Position = i, ProductId = product.Id, ProductName = product.Name, Quantity = quantity,
                    UnitPrice = product.Price, Tax = product.Tax, LineTotal = total, LineBase = split.Base, LineTax = split.Tax
                });
            }
            quote.Subtotal = quote.Lines.Sum(l => l.LineBase);
            quote.TaxTotal = quote.Lines.Sum(l => l.LineTax);
            quote.Total = quote.Lines.Sum(l => l.LineTotal);
            db.Quotes.Add(quote);
            stamps.Add((quote, at(today.AddDays(-s.DaysAgo), 15, 20)));
        }
    }

    /// <summary>
    /// el interceptor pone CreatedAt en la hora actual al insertar y solo toca UpdatedAt al modificar
    /// asi que se cambia CreatedAt ya guardado y despues se iguala UpdatedAt en cada tabla
    /// </summary>
    private static async Task BackdateAsync(AppDbContext db, List<(Entity Entity, DateTime At)> stamps, CancellationToken ct)
    {
        foreach (var (entity, at) in stamps) entity.CreatedAt = at.ToMicroseconds();
        await db.SaveChangesAsync(ct);

        await db.Tenants.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.CreatedAt), ct);
        await db.Categories.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.CreatedAt), ct);
        await db.Products.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.CreatedAt), ct);
        await db.StockMovements.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.CreatedAt), ct);
        await db.Suppliers.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.CreatedAt), ct);
        await db.Customers.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.CreatedAt), ct);
        await db.CashSessions.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.ClosedAt ?? e.CreatedAt), ct);
        await db.Sales.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.VoidedAt ?? e.CreatedAt), ct);
        await db.Purchases.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.CreatedAt), ct);
        await db.SupplierPayments.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.CreatedAt), ct);
        await db.Expenses.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.CreatedAt), ct);
        await db.Quotes.ExecuteUpdateAsync(s => s.SetProperty(e => e.UpdatedAt, e => e.CreatedAt), ct);
    }
}
