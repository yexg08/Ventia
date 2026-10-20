using Ventia.Application.Business;
using Ventia.Application.Catalog;
using Ventia.Application.Inventory;
using Ventia.Application.Parties;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Ventia.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IBusinessService, BusinessService>();
        services.AddScoped<ICategoriesService, CategoriesService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IProductsService, ProductsService>();
        services.AddScoped<ICustomersService, CustomersService>();
        services.AddScoped<ISuppliersService, SuppliersService>();
        return services;
    }
}
