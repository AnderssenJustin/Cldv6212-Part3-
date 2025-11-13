using ABCRetailers.Functions.Entities;
using ABCRetailers.Functions.Models;

namespace ABCRetailers.Functions.Helpers;

public static class Map
{
    // Table Entity  ->  DTOs returned to MVC

    public static CustomerDto ToDto(CustomerEntity e)
        => new(
            Id: e.RowKey,
            Name: e.Name,
            Surname: e.Surname,
            Username: e.Username,
            Email: e.Email,
            ShippingAddress: e.ShippingAddress
        );

    public static ProductDto ToDto(ProductEntity e)
        => new(
            Id: e.RowKey,
            ProductName: e.ProductName,
            Description: e.Description,
            Price: (decimal)e.Price,                 // stored as double in Table, cast to decimal for MVC
            StockAvailable: e.StockAvailable,
            ImageUrl: e.ImageUrl
        );

    public static OrderDto ToDto(OrderEntity e)
    {
        var unitPrice = (decimal)e.UnitPrice;       // double -> decimal for money in MVC
        var total = unitPrice * e.Quantity;

        return new OrderDto(
            Id: e.RowKey,
            CustomerId: e.CustomerId,
            ProductId: e.ProductId,
            ProductName: e.ProductName,
            Quantity: e.Quantity,
            UnitPrice: unitPrice,
            TotalAmount: total,
            OrderDateUtc: e.OrderDateUtc,
            Status: e.Status
        );
    }
}
