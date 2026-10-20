using System.Net;
using System.Net.Http.Json;

namespace Ventia.Tests.Integration;

/// <summary>clientes y proveedores (documentos validados y autorizacion de datos y permisos por rol)</summary>
[Collection(ApiCollection.Name)]
public class PartiesTests(ApiFactory factory)
{
    private static object Customer(
        string documentType = "CC", string documentNumber = "1.053.123.456", string name = "Camila Ríos",
        bool dataConsent = true, string? phone = "300 123 4567", string? email = "Camila@Correo.com") =>
        new { documentType, documentNumber, name, phone, email, address = (string?)null, notes = (string?)null, dataConsent };

    private static object Supplier(string documentNumber = "800197268-4", string name = "Distribuidora Andina", bool isActive = true) =>
        new
        {
            documentType = "NIT", documentNumber, name, contactName = "Jorge", phone = "6068851234", email = (string?)null,
            address = (string?)null, notes = (string?)null, isActive
        };

    [Fact]
    public async Task A_customer_is_created_with_a_normalized_document_and_the_consent_date()
    {
        var business = await factory.RegisterBusinessAsync();

        var response = await business.Client.PostAsJsonAsync("/api/customers", Customer());
        await ApiFactory.Expect(response, HttpStatusCode.Created);
        var customer = await ApiFactory.Json(response);

        Assert.Equal("1053123456", customer.GetProperty("documentNumber").GetString());
        Assert.Equal("573001234567".Length - 2, customer.GetProperty("phone").GetString()!.Length);
        Assert.Equal("camila@correo.com", customer.GetProperty("email").GetString());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, customer.GetProperty("dataConsentAt").ValueKind);
    }

    [Fact]
    public async Task People_need_data_consent_but_companies_with_nit_do_not()
    {
        var business = await factory.RegisterBusinessAsync();

        var person = await business.Client.PostAsJsonAsync("/api/customers", Customer(dataConsent: false));
        await ApiFactory.Expect(person, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(person, "dataConsent"));

        var company = await business.Client.PostAsJsonAsync("/api/customers",
            Customer("NIT", "900123456-8", "Eventos SAS", dataConsent: false));
        await ApiFactory.Expect(company, HttpStatusCode.Created);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await ApiFactory.Json(company)).GetProperty("dataConsentAt").ValueKind);
    }

    [Theory]
    [InlineData("CC", "12")]
    [InlineData("CC", "12345678901")]
    [InlineData("CC", "12A45")]
    [InlineData("NIT", "900123456-7")]
    [InlineData("NIT", "900123456")]
    [InlineData("PP", "AB")]
    public async Task Invalid_documents_are_rejected(string type, string number)
    {
        var business = await factory.RegisterBusinessAsync();
        var response = await business.Client.PostAsJsonAsync("/api/customers", Customer(type, number));
        await ApiFactory.Expect(response, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(response, "documentNumber"));
    }

    [Fact]
    public async Task A_document_is_unique_per_business_but_can_repeat_in_another_business()
    {
        var a = await factory.RegisterBusinessAsync();
        var b = await factory.RegisterBusinessAsync();
        await ApiFactory.Expect(await a.Client.PostAsJsonAsync("/api/customers", Customer()), HttpStatusCode.Created);

        var duplicated = await a.Client.PostAsJsonAsync("/api/customers", Customer(documentNumber: "1053123456", name: "Otra"));
        await ApiFactory.Expect(duplicated, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(duplicated, "documentNumber"));

        // el mismo numero con otro tipo de documento es otro documento
        await ApiFactory.Expect(await a.Client.PostAsJsonAsync("/api/customers", Customer("CE", "1053123456")), HttpStatusCode.Created);
        await ApiFactory.Expect(await b.Client.PostAsJsonAsync("/api/customers", Customer()), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Customers_can_be_found_by_name_document_or_phone()
    {
        var business = await factory.RegisterBusinessAsync();
        await business.Client.PostAsJsonAsync("/api/customers", Customer(documentNumber: "1053123456", name: "Camila Ríos", phone: "3001112233"));
        await business.Client.PostAsJsonAsync("/api/customers", Customer(documentNumber: "75000111", name: "Daniel Marín", phone: "3104445566"));

        async Task<List<string?>> Names(string search) =>
            (await ApiFactory.Json(await business.Client.GetAsync($"/api/customers?search={Uri.EscapeDataString(search)}")))
                .EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();

        Assert.Equal(["Daniel Marín"], await Names("marín"));
        Assert.Equal(["Camila Ríos"], await Names("10531"));
        Assert.Equal(["Daniel Marín"], await Names("444 55"));
        Assert.Equal(["Camila Ríos", "Daniel Marín"], await Names(""));
    }

    [Fact]
    public async Task Cashiers_register_and_edit_customers_but_only_the_owner_deletes_them()
    {
        var business = await factory.RegisterBusinessAsync();
        var cashier = (await factory.CreateCashierAsync(business.Client)).Client;

        var created = await cashier.PostAsJsonAsync("/api/customers", Customer());
        await ApiFactory.Expect(created, HttpStatusCode.Created);
        var id = (await ApiFactory.Json(created)).GetProperty("id").GetGuid();
        await ApiFactory.Expect(await cashier.PutAsJsonAsync($"/api/customers/{id}", Customer(name: "Camila Ríos Gómez")), HttpStatusCode.OK);

        await ApiFactory.Expect(await cashier.DeleteAsync($"/api/customers/{id}"), HttpStatusCode.Forbidden);
        await ApiFactory.Expect(await business.Client.DeleteAsync($"/api/customers/{id}"), HttpStatusCode.NoContent);
        await ApiFactory.Expect(await business.Client.GetAsync($"/api/customers/{id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Once_given_the_consent_is_kept_when_editing()
    {
        var business = await factory.RegisterBusinessAsync();
        var created = await ApiFactory.Json(await business.Client.PostAsJsonAsync("/api/customers", Customer()));
        var id = created.GetProperty("id").GetGuid();

        var updated = await business.Client.PutAsJsonAsync($"/api/customers/{id}", Customer(name: "Camila R.", dataConsent: false));
        await ApiFactory.Expect(updated, HttpStatusCode.OK);
        Assert.Equal(created.GetProperty("dataConsentAt").GetString(), (await ApiFactory.Json(updated)).GetProperty("dataConsentAt").GetString());
    }

    [Fact]
    public async Task Only_the_owner_manages_suppliers()
    {
        var business = await factory.RegisterBusinessAsync();
        var cashier = (await factory.CreateCashierAsync(business.Client)).Client;

        var created = await business.Client.PostAsJsonAsync("/api/suppliers", Supplier());
        await ApiFactory.Expect(created, HttpStatusCode.Created);
        var supplier = await ApiFactory.Json(created);
        Assert.Equal("800197268-4", supplier.GetProperty("documentNumber").GetString());

        await ApiFactory.Expect(await cashier.GetAsync("/api/suppliers"), HttpStatusCode.Forbidden);

        var id = supplier.GetProperty("id").GetGuid();
        var deactivated = await business.Client.PutAsJsonAsync($"/api/suppliers/{id}", Supplier(isActive: false));
        await ApiFactory.Expect(deactivated, HttpStatusCode.OK);
        Assert.False((await ApiFactory.Json(deactivated)).GetProperty("isActive").GetBoolean());

        var duplicated = await business.Client.PostAsJsonAsync("/api/suppliers", Supplier(name: "Otro"));
        await ApiFactory.Expect(duplicated, HttpStatusCode.BadRequest);
        Assert.True(await ApiFactory.HasFieldError(duplicated, "documentNumber"));
    }

    [Fact]
    public async Task A_business_cannot_see_or_change_another_business_customers_or_suppliers()
    {
        var a = await factory.RegisterBusinessAsync();
        var b = await factory.RegisterBusinessAsync();
        var customerOfA = (await ApiFactory.Json(await a.Client.PostAsJsonAsync("/api/customers", Customer()))).GetProperty("id").GetGuid();
        var supplierOfA = (await ApiFactory.Json(await a.Client.PostAsJsonAsync("/api/suppliers", Supplier()))).GetProperty("id").GetGuid();

        Assert.Equal(0, (await ApiFactory.Json(await b.Client.GetAsync("/api/customers"))).GetArrayLength());
        Assert.Equal(0, (await ApiFactory.Json(await b.Client.GetAsync("/api/suppliers"))).GetArrayLength());
        await ApiFactory.Expect(await b.Client.GetAsync($"/api/customers/{customerOfA}"), HttpStatusCode.NotFound);
        await ApiFactory.Expect(await b.Client.PutAsJsonAsync($"/api/customers/{customerOfA}", Customer(name: "Hackeado")), HttpStatusCode.NotFound);
        await ApiFactory.Expect(await b.Client.DeleteAsync($"/api/customers/{customerOfA}"), HttpStatusCode.NotFound);
        await ApiFactory.Expect(await b.Client.GetAsync($"/api/suppliers/{supplierOfA}"), HttpStatusCode.NotFound);
        await ApiFactory.Expect(await b.Client.DeleteAsync($"/api/suppliers/{supplierOfA}"), HttpStatusCode.NotFound);
    }
}
