using System.Text.Json;
using ABCRetailers.Functions.Entities;   // ← REQUIRED
using ABCRetailers.Functions.Helpers;    // ← REQUIRED
using ABCRetailers.Functions.Models;     // ← REQUIRED
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;


namespace ABCRetailers.Functions.Functions;
public class OrdersFunctions
{
    private readonly string _conn;
    private readonly string _ordersTable;
    private readonly string _productsTable;
    private readonly string _customersTable;
    private readonly string _queueOrder;
    private readonly string _queueStock;

    public OrdersFunctions(IConfiguration cfg)
    {
        _conn = cfg["STORAGE_CONNECTION"] ?? throw new InvalidOperationException("STORAGE_CONNECTION missing");
        _ordersTable = cfg["TABLE_ORDER"] ?? "Order";
        _productsTable = cfg["TABLE_PRODUCT"] ?? "Product";
        _customersTable = cfg["TABLE_CUSTOMER"] ?? "Customer";
        _queueOrder = cfg["QUEUE_ORDER_NOTIFICATIONS"] ?? "order-notifications";
        _queueStock = cfg["QUEUE_STOCK_UPDATES"] ?? "stock-updates";
    }

    [Function("Orders_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders")] HttpRequestData req)
    {
        var table = new TableClient(_conn, _ordersTable);
        await table.CreateIfNotExistsAsync();

        var items = new List<OrderDto>();
        await foreach (var e in table.QueryAsync<OrderEntity>(x => x.PartitionKey == "Order"))
            items.Add(Map.ToDto(e));

        // newest first
        var ordered = items.OrderByDescending(o => o.OrderDateUtc).ToList();
        return HttpJson.Ok(req, ordered);
    }

    [Function("Orders_Get")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{id}")] HttpRequestData req, string id)
    {
        var table = new TableClient(_conn, _ordersTable);
        try
        {
            var e = await table.GetEntityAsync<OrderEntity>("Order", id);
            return HttpJson.Ok(req, Map.ToDto(e.Value));
        }
        catch
        {
            return HttpJson.NotFound(req, "Order not found");
        }
    }

    public record OrderCreate(string CustomerId, string ProductId, int Quantity);
    [Function("Orders_Create")]
    public async Task<HttpResponseData> Create(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders")] HttpRequestData req)
    {
        var input = await HttpJson.ReadAsync<OrderCreate>(req);
        if (input is null || string.IsNullOrWhiteSpace(input.CustomerId) ||
            string.IsNullOrWhiteSpace(input.ProductId) || input.Quantity < 1)
            return HttpJson.Bad(req, "CustomerId, ProductId, Quantity >= 1 required");

        var products = new TableClient(_conn, _productsTable);
        var customers = new TableClient(_conn, _customersTable);
        await products.CreateIfNotExistsAsync();
        await customers.CreateIfNotExistsAsync();

        ProductEntity product;
        CustomerEntity customer;

        try
        {
            product = (await products.GetEntityAsync<ProductEntity>("Product", input.ProductId)).Value;
        }
        catch { return HttpJson.Bad(req, "Invalid ProductId"); }

        try
        {
            customer = (await customers.GetEntityAsync<CustomerEntity>("Customer", input.CustomerId)).Value;
        }
        catch { return HttpJson.Bad(req, "Invalid CustomerId"); }

        if (product.StockAvailable < input.Quantity)
            return HttpJson.Bad(req, $"Insufficient stock. Available: {product.StockAvailable}");

        // Generate order ID upfront
        var orderId = Guid.NewGuid().ToString("N");

        var queueOrder = new QueueClient(_conn, _queueOrder,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        await queueOrder.CreateIfNotExistsAsync();

        var orderCreationMsg = new
        {
            Type = "CreateOrder",
            OrderId = orderId,  // ← Pass the ID to queue processor
            CustomerId = input.CustomerId,
            CustomerName = $"{customer.Name} {customer.Surname}",
            ProductId = input.ProductId,
            ProductName = product.ProductName,
            Quantity = input.Quantity,
            UnitPrice = product.Price,
            PreviousStock = product.StockAvailable
        };

        await queueOrder.SendMessageAsync(JsonSerializer.Serialize(orderCreationMsg));

        // Return a DTO that matches what MVC expects
        var dto = new OrderDto(
            Id: orderId,
            CustomerId: input.CustomerId,
            ProductId: input.ProductId,
            ProductName: product.ProductName,
            Quantity: input.Quantity,
            UnitPrice: (decimal)product.Price,
            TotalAmount: (decimal)product.Price * input.Quantity,
            OrderDateUtc: DateTimeOffset.UtcNow,
            Status: "Queued"  // ← Indicates it's being processed
        );

        return HttpJson.Created(req, dto);
    }
    /*[Function("Orders_Create")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders")] HttpRequestData req)
    {
        var input = await HttpJson.ReadAsync<OrderCreate>(req);
        if (input is null || string.IsNullOrWhiteSpace(input.CustomerId) || string.IsNullOrWhiteSpace(input.ProductId) || input.Quantity < 1)
            return HttpJson.Bad(req, "CustomerId, ProductId, Quantity >= 1 required");

        var orders = new TableClient(_conn, _ordersTable);
        var products = new TableClient(_conn, _productsTable);
        var customers = new TableClient(_conn, _customersTable);
        await orders.CreateIfNotExistsAsync();
        await products.CreateIfNotExistsAsync();
        await customers.CreateIfNotExistsAsync();

        // Validate refs
        ProductEntity product;
        CustomerEntity customer;

        try
        {
            product = (await products.GetEntityAsync<ProductEntity>("Product", input.ProductId)).Value;
        }
        catch { return HttpJson.Bad(req, "Invalid ProductId"); }

        try
        {
            customer = (await customers.GetEntityAsync<CustomerEntity>("Customer", input.CustomerId)).Value;
        }
        catch { return HttpJson.Bad(req, "Invalid CustomerId"); }

        if (product.StockAvailable < input.Quantity)
            return HttpJson.Bad(req, $"Insufficient stock. Available: {product.StockAvailable}");

        // Snapshot price & reduce stock (naïve; for concurrency use ETag preconditions)
        var order = new OrderEntity
        {
            CustomerId = input.CustomerId,
            ProductId = input.ProductId,
            ProductName = product.ProductName,
            Quantity = input.Quantity,
            UnitPrice = product.Price,
            OrderDateUtc = DateTimeOffset.UtcNow,
            Status = "Submitted"
        };
        await orders.AddEntityAsync(order);

        product.StockAvailable -= input.Quantity;
        await products.UpdateEntityAsync(product, product.ETag, TableUpdateMode.Replace);

        // Send queue messages
        var queueOrder = new QueueClient(_conn, _queueOrder, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        var queueStock = new QueueClient(_conn, _queueStock, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        await queueOrder.CreateIfNotExistsAsync();
        await queueStock.CreateIfNotExistsAsync();

        var orderMsg = new
        {
            Type = "OrderCreated",
            OrderId = order.RowKey,
            order.CustomerId,
            CustomerName = $"{customer.Name} {customer.Surname}",
            order.ProductId,
            ProductName = product.ProductName,
            order.Quantity,
            order.UnitPrice,
            TotalAmount = order.UnitPrice * order.Quantity,
            OrderDateUtc = order.OrderDateUtc,
            order.Status
        };
        await queueOrder.SendMessageAsync(JsonSerializer.Serialize(orderMsg));

        var stockMsg = new
        {
            Type = "StockUpdated",
            productId = product.RowKey,
            ProductName = product.ProductName,
            PreviousStock = product.StockAvailable + input.Quantity,
            NewStock = product.StockAvailable,
            UpdatedDateUtc = DateTimeOffset.UtcNow,
            UpdatedBy = "Order System"
        };
        await queueStock.SendMessageAsync(JsonSerializer.Serialize(stockMsg));

        return HttpJson.Created(req, Map.ToDto(order));
    }*/
    /*[Function("Orders_Create")]
    public async Task<HttpResponseData> Create(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders")] HttpRequestData req)
    {
        var input = await HttpJson.ReadAsync<OrderCreate>(req);
        if (input is null || string.IsNullOrWhiteSpace(input.CustomerId) ||
            string.IsNullOrWhiteSpace(input.ProductId) || input.Quantity < 1)
            return HttpJson.Bad(req, "CustomerId, ProductId, Quantity >= 1 required");

        var products = new TableClient(_conn, _productsTable);
        var customers = new TableClient(_conn, _customersTable);
        await products.CreateIfNotExistsAsync();
        await customers.CreateIfNotExistsAsync();

        // Validate refs
        ProductEntity product;
        CustomerEntity customer;

        try
        {
            product = (await products.GetEntityAsync<ProductEntity>("Product", input.ProductId)).Value;
        }
        catch { return HttpJson.Bad(req, "Invalid ProductId"); }

        try
        {
            customer = (await customers.GetEntityAsync<CustomerEntity>("Customer", input.CustomerId)).Value;
        }
        catch { return HttpJson.Bad(req, "Invalid CustomerId"); }

        if (product.StockAvailable < input.Quantity)
            return HttpJson.Bad(req, $"Insufficient stock. Available: {product.StockAvailable}");

        // ===== KEY CHANGE: Don't create order here, send to queue instead =====
        var queueOrder = new QueueClient(_conn, _queueOrder,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        await queueOrder.CreateIfNotExistsAsync();

        // Send order creation message to queue
        var orderCreationMsg = new
        {
            Type = "CreateOrder",
            CustomerId = input.CustomerId,
            CustomerName = $"{customer.Name} {customer.Surname}",
            ProductId = input.ProductId,
            ProductName = product.ProductName,
            Quantity = input.Quantity,
            UnitPrice = product.Price,
            PreviousStock = product.StockAvailable
        };

        await queueOrder.SendMessageAsync(JsonSerializer.Serialize(orderCreationMsg));

        return HttpJson.Ok(req, new { message = "Order submitted for processing", status = "Queued" });
    }

    /*[Function("Orders_QueueProcessor")]
    public async Task ProcessOrderCreation(
        [QueueTrigger("%QUEUE_ORDER_NOTIFICATIONS%", Connection = "STORAGE_CONNECTION")] string message,
        FunctionContext ctx)
    {
        var log = ctx.GetLogger("Orders_QueueProcessor");

        try
        {
            // Deserialize the queue message
            var msgData = JsonSerializer.Deserialize<JsonElement>(message);
            var msgType = msgData.GetProperty("Type").GetString();

            // Only process "CreateOrder" messages (ignore status updates, etc.)
            if (msgType != "CreateOrder")
                return;

            var customerId = msgData.GetProperty("CustomerId").GetString()!;
            var productId = msgData.GetProperty("ProductId").GetString()!;
            var quantity = msgData.GetProperty("Quantity").GetInt32();
            var productName = msgData.GetProperty("ProductName").GetString()!;
            var unitPrice = msgData.GetProperty("UnitPrice").GetDouble();
            var customerName = msgData.GetProperty("CustomerName").GetString()!;

            // ===== NOW create the order in the table =====
            var ordersTable = new TableClient(_conn, _ordersTable);
            var productsTable = new TableClient(_conn, _productsTable);
            await ordersTable.CreateIfNotExistsAsync();
            await productsTable.CreateIfNotExistsAsync();

            var order = new OrderEntity
            {
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

            // Send stock update notification
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
            throw; // Let Azure Functions retry logic handle it
        }
    }*/
    /*[Function("Orders_QueueProcessor")]
    public async Task ProcessOrderCreation(
    [QueueTrigger("%QUEUE_ORDER_NOTIFICATIONS%", Connection = "STORAGE_CONNECTION")] string message,
    FunctionContext ctx)
    {
        var log = ctx.GetLogger("Orders_QueueProcessor");

        try
        {
            var msgData = JsonSerializer.Deserialize<JsonElement>(message);
            var msgType = msgData.GetProperty("Type").GetString();

            if (msgType != "CreateOrder")
                return;

            var orderId = msgData.GetProperty("OrderId").GetString()!;  // ← Use provided ID
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
                RowKey = orderId,  // ← Use the ID from message
                CustomerId = customerId,
                ProductId = productId,
                ProductName = productName,
                Quantity = quantity,
                UnitPrice = unitPrice,
                OrderDateUtc = DateTimeOffset.UtcNow,
                Status = "Submitted"  // ← Now mark as Submitted
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
    }*/
    public record OrderStatusUpdate(string Status);

    [Function("Orders_UpdateStatus")]
    public async Task<HttpResponseData> UpdateStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", "post", "put", Route = "orders/{id}/status")] HttpRequestData req, string id)
    {
        var input = await HttpJson.ReadAsync<OrderStatusUpdate>(req);
        if (input is null || string.IsNullOrWhiteSpace(input.Status))
            return HttpJson.Bad(req, "Status is required");

        var orders = new TableClient(_conn, _ordersTable);
        try
        {
            var resp = await orders.GetEntityAsync<OrderEntity>("Order", id);
            var e = resp.Value;
            var previous = e.Status;

            e.Status = input.Status;
            await orders.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);

            // notify
            var queueOrder = new QueueClient(_conn, _queueOrder, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
            await queueOrder.CreateIfNotExistsAsync();
            var statusMsg = new
            {
                Type = "OrderStatusUpdated",
                OrderId = e.RowKey,
                PreviousStatus = previous,
                NewStatus = e.Status,
                UpdatedDateUtc = DateTimeOffset.UtcNow,
                UpdatedBy = "System"
            };
            await queueOrder.SendMessageAsync(JsonSerializer.Serialize(statusMsg));

            return HttpJson.Ok(req, Map.ToDto(e));
        }
        catch
        {
            return HttpJson.NotFound(req, "Order not found");
        }
    }

    [Function("Orders_Delete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "orders/{id}")] HttpRequestData req, string id)
    {
        var table = new TableClient(_conn, _ordersTable);
        await table.DeleteEntityAsync("Order", id);
        return HttpJson.NoContent(req);
    }
}
