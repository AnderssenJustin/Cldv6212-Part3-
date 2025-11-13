using Azure;
using Azure.Data.Tables;


namespace ABCRetailers.Functions.Entities;
public class CustomerEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "Customer";
    public string RowKey { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string ShippingAddress { get; set; } = "";
}

public class ProductEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "Product";
    public string RowKey { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ProductName { get; set; } = "";
    public string Description { get; set; } = "";
    public double Price { get; set; }   // stored as double in Table
    public int StockAvailable { get; set; }
    public string ImageUrl { get; set; } = "";
}

public class OrderEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "Order";
    public string RowKey { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string CustomerId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public double UnitPrice { get; set; } // stored as double
    public DateTimeOffset OrderDateUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "Submitted";
}
