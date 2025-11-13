/*using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;


namespace ABCRetailers.Functions.Functions;
public class QueueProcessorFunctions
{
    [Function("OrderNotifications_Processor")]
    public void OrderNotificationsProcessor(
        [QueueTrigger("%QUEUE_ORDER_NOTIFICATIONS%", Connection = "STORAGE_CONNECTION")] string message,
        FunctionContext ctx)
    {
        var log = ctx.GetLogger("OrderNotifications_Processor");
        log.LogInformation($"OrderNotifications message: {message}");
        // (Optional) write receipts, send emails, etc.
    }

    [Function("StockUpdates_Processor")]
    public void StockUpdatesProcessor(
        [QueueTrigger("%QUEUE_STOCK_UPDATES%", Connection = "STORAGE_CONNECTION")] string message,
        FunctionContext ctx)
    {
        var log = ctx.GetLogger("StockUpdates_Processor");
        log.LogInformation($"StockUpdates message: {message}");
        // (Optional) sync to reporting DB, etc.
    }
}*/
using System.Text.Json;
using ABCRetailers.Functions.Entities;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ABCRetailers.Functions.Functions;

public class QueueProcessorFunctions
{
    private readonly string _conn;
    private readonly string _ordersTable;
    private readonly string _productsTable;
    private readonly string _queueStock;

    public QueueProcessorFunctions(IConfiguration cfg)
    {
        //_conn = cfg["STORAGE_CONNECTION"] ?? throw new InvalidOperationException("STORAGE_CONNECTION missing");
        _conn = Environment.GetEnvironmentVariable("STORAGE_CONNECTION") ?? throw new InvalidOperationException("STORAGE_CONNECTION missing");
        _ordersTable = cfg["TABLE_ORDER"] ?? "Order";
        _productsTable = cfg["TABLE_PRODUCT"] ?? "Product";
        _queueStock = cfg["QUEUE_STOCK_UPDATES"] ?? "stock-updates";
    }

    [Function("OrderNotifications_Processor")]
    public async Task OrderNotificationsProcessor(
        [QueueTrigger("%QUEUE_ORDER_NOTIFICATIONS%", Connection = "STORAGE_CONNECTION")] string message,
        FunctionContext ctx)
    {
        var log = ctx.GetLogger("OrderNotifications_Processor");
        log.LogInformation($"OrderNotifications message: {message}");

        try
        {
            var msgData = JsonSerializer.Deserialize<JsonElement>(message);
            var msgType = msgData.GetProperty("Type").GetString();

            if (msgType != "CreateOrder")
                return;

            var orderId = msgData.GetProperty("OrderId").GetString()!;
            var customerId = msgData.GetProperty("CustomerId").GetString()!;
            var productId = msgData.GetProperty("ProductId").GetString()!;
            var quantity = msgData.GetProperty("Quantity").GetInt32();
            var productName = msgData.GetProperty("ProductName").GetString()!;
            var unitPrice = msgData.GetProperty("UnitPrice").GetDouble();

            var ordersTable = new TableClient(_conn, _ordersTable);
            var productsTable = new TableClient(_conn, _productsTable);
            await ordersTable.CreateIfNotExistsAsync();
            await productsTable.CreateIfNotExistsAsync();

            var order = new OrderEntity
            {
                PartitionKey = "Order",
                RowKey = orderId,
                CustomerId = customerId,
                ProductId = productId,
                ProductName = productName,
                Quantity = quantity,
                UnitPrice = unitPrice,
                OrderDateUtc = DateTimeOffset.UtcNow,
                Status = "Submitted"
            };

            await ordersTable.AddEntityAsync(order);
            log.LogInformation($"Order {order.RowKey} created from queue");

            // Update stock
            var product = (await productsTable.GetEntityAsync<ProductEntity>("Product", productId)).Value;
            product.StockAvailable -= quantity;
            await productsTable.UpdateEntityAsync(product, product.ETag, TableUpdateMode.Replace);

            // Send stock notification
            var queueStock = new QueueClient(_conn, _queueStock,
                new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
            await queueStock.CreateIfNotExistsAsync();

            var stockMsg = new
            {
                Type = "StockUpdated",
                ProductId = productId,
                ProductName = productName,
                PreviousStock = product.StockAvailable + quantity,
                NewStock = product.StockAvailable,
                UpdatedDateUtc = DateTimeOffset.UtcNow,
                UpdatedBy = "Order Queue Processor"
            };
            await queueStock.SendMessageAsync(JsonSerializer.Serialize(stockMsg));

            log.LogInformation($"Stock updated for product {productId}: {product.StockAvailable}");
        }
        catch (Exception ex)
        {
            log.LogError($"Error processing order from queue: {ex.Message}");
            throw;
        }
    }

    [Function("StockUpdates_Processor")]
    public void StockUpdatesProcessor(
        [QueueTrigger("%QUEUE_STOCK_UPDATES%", Connection = "STORAGE_CONNECTION")] string message,
        FunctionContext ctx)
    {
        var log = ctx.GetLogger("StockUpdates_Processor");
        log.LogInformation($"StockUpdates message: {message}");
    }
}
