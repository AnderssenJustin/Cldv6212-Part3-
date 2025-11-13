
namespace ABCRetailers.Functions.Models;

public record CustomerDto(string Id, string Name, string Surname, string Username, string Email, string ShippingAddress);
public record ProductDto(string Id, string ProductName, string Description, decimal Price, int StockAvailable, string ImageUrl);
public record OrderDto(
    string Id, string CustomerId, string ProductId, string ProductName,
    int Quantity, decimal UnitPrice, decimal TotalAmount, DateTimeOffset OrderDateUtc, string Status);
