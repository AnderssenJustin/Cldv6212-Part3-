using ABCRetailers.Models;

namespace ABCRetailers.Services;

public interface IFunctionsApi
{
    // Customers
    Task<List<Customer>> GetCustomersAsync();
    Task<Customer?> GetCustomerAsync(string id);
    Task<Customer?> GetCustomerByUsernameAsync(string username); // ✅ Added for username lookup
    Task<Customer> CreateCustomerAsync(Customer c);
    Task<Customer> UpdateCustomerAsync(string id, Customer c);
    Task DeleteCustomerAsync(string id);

    // Products
    Task<List<Product>> GetProductsAsync();
    Task<Product?> GetProductAsync(string id);
    Task<Product> CreateProductAsync(Product p, IFormFile? imageFile);
    Task<Product> UpdateProductAsync(string id, Product p, IFormFile? imageFile);
    Task DeleteProductAsync(string id);

    // Orders
    Task<List<Order>> GetOrdersAsync();
    Task<Order?> GetOrderAsync(string id);
    Task<Order> CreateOrderAsync(string customerId, string productId, int quantity);
    Task UpdateOrderStatusAsync(string id, string newStatus);
    Task DeleteOrderAsync(string id);

    // ✅ NEW: Filtered orders for current customer
    Task<List<Order>> GetOrdersByCustomerIdAsync(string customerId);

    // Uploads
    Task<string> UploadProofOfPaymentAsync(IFormFile file, string? orderId, string? customerName);
}