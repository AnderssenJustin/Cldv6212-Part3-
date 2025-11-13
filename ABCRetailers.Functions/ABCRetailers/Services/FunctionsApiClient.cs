using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ABCRetailers.Models;
using System.Linq;

namespace ABCRetailers.Services
{
    public class FunctionsApiClient : IFunctionsApi
    {
        private readonly HttpClient _http;
        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

        // Centralize your Function routes here
        private const string CustomersRoute = "customers";
        private const string ProductsRoute = "products";
        private const string OrdersRoute = "orders";
        private const string UploadsRoute = "uploads/proof-of-payment"; // multipart

        public FunctionsApiClient(IHttpClientFactory factory)
        {
            _http = factory.CreateClient("Functions"); // BaseAddress set in Program.cs
        }

        // ---------- Helpers ----------
        private static HttpContent JsonBody(object obj)
            => new StringContent(JsonSerializer.Serialize(obj, _json), Encoding.UTF8, "application/json");

        private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage resp)
        {
            resp.EnsureSuccessStatusCode();
            var stream = await resp.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<T>(stream, _json);
            return data!;
        }

        // ---------- Customers ----------
        public async Task<List<Customer>> GetCustomersAsync()
            => await ReadJsonAsync<List<Customer>>(await _http.GetAsync(CustomersRoute));

        // ✅ FIXED: Match by Username instead of Email
        public async Task<Customer?> GetCustomerByUsernameAsync(string username)
        {
            var customers = await GetCustomersAsync();
            return customers.FirstOrDefault(c =>
                c.Username?.Equals(username, StringComparison.OrdinalIgnoreCase) == true);
        }

        public async Task<Customer?> GetCustomerAsync(string id)
        {
            var resp = await _http.GetAsync($"{CustomersRoute}/{id}");
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            return await ReadJsonAsync<Customer>(resp);
        }

        public async Task<Customer> CreateCustomerAsync(Customer c)
            => await ReadJsonAsync<Customer>(await _http.PostAsync(CustomersRoute, JsonBody(new
            {
                name = c.Name,
                surname = c.Surname,
                username = c.Username,
                email = c.Email,
                shippingAddress = c.ShippingAddress
            })));

        public async Task<Customer> UpdateCustomerAsync(string id, Customer c)
            => await ReadJsonAsync<Customer>(await _http.PutAsync($"{CustomersRoute}/{id}", JsonBody(new
            {
                name = c.Name,
                surname = c.Surname,
                username = c.Username,
                email = c.Email,
                shippingAddress = c.ShippingAddress
            })));

        public async Task DeleteCustomerAsync(string id)
            => (await _http.DeleteAsync($"{CustomersRoute}/{id}")).EnsureSuccessStatusCode();

        // ---------- Products ----------
        public async Task<List<Product>> GetProductsAsync()
            => await ReadJsonAsync<List<Product>>(await _http.GetAsync(ProductsRoute));

        public async Task<Product?> GetProductAsync(string id)
        {
            var resp = await _http.GetAsync($"{ProductsRoute}/{id}");
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            return await ReadJsonAsync<Product>(resp);
        }

        public async Task<Product> CreateProductAsync(Product p, IFormFile? imageFile)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(p.ProductName), "ProductName");
            form.Add(new StringContent(p.Description ?? string.Empty), "Description");
            form.Add(new StringContent(p.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)), "Price");
            form.Add(new StringContent(p.StockAvailable.ToString(System.Globalization.CultureInfo.InvariantCulture)), "StockAvailable");
            if (!string.IsNullOrWhiteSpace(p.ImageUrl))
                form.Add(new StringContent(p.ImageUrl), "ImageUrl");

            if (imageFile is not null && imageFile.Length > 0)
            {
                var file = new StreamContent(imageFile.OpenReadStream());
                file.Headers.ContentType = new MediaTypeHeaderValue(imageFile.ContentType ?? "application/octet-stream");
                form.Add(file, "ImageFile", imageFile.FileName);
            }

            return await ReadJsonAsync<Product>(await _http.PostAsync(ProductsRoute, form));
        }

        public async Task<Product> UpdateProductAsync(string id, Product p, IFormFile? imageFile)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(p.ProductName), "ProductName");
            form.Add(new StringContent(p.Description ?? string.Empty), "Description");
            form.Add(new StringContent(p.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)), "Price");
            form.Add(new StringContent(p.StockAvailable.ToString(System.Globalization.CultureInfo.InvariantCulture)), "StockAvailable");
            if (!string.IsNullOrWhiteSpace(p.ImageUrl))
                form.Add(new StringContent(p.ImageUrl), "ImageUrl");

            if (imageFile is not null && imageFile.Length > 0)
            {
                var file = new StreamContent(imageFile.OpenReadStream());
                file.Headers.ContentType = new MediaTypeHeaderValue(imageFile.ContentType ?? "application/octet-stream");
                form.Add(file, "ImageFile", imageFile.FileName);
            }

            return await ReadJsonAsync<Product>(await _http.PutAsync($"{ProductsRoute}/{id}", form));
        }

        public async Task DeleteProductAsync(string id)
            => (await _http.DeleteAsync($"{ProductsRoute}/{id}")).EnsureSuccessStatusCode();

        // ---------- Orders (use DTOs → map to enum) ----------
        public async Task<List<Order>> GetOrdersAsync()
        {
            var dtos = await ReadJsonAsync<List<OrderDto>>(await _http.GetAsync(OrdersRoute));
            return dtos.Select(ToOrder).ToList();
        }

        public async Task<Order?> GetOrderAsync(string id)
        {
            var resp = await _http.GetAsync($"{OrdersRoute}/{id}");
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            var dto = await ReadJsonAsync<OrderDto>(resp);
            return ToOrder(dto);
        }

        public async Task<Order> CreateOrderAsync(string customerId, string productId, int quantity)
        {
            var payload = new { customerId, productId, quantity };
            var dto = await ReadJsonAsync<OrderDto>(await _http.PostAsync(OrdersRoute, JsonBody(payload)));
            return ToOrder(dto);
        }

        public async Task UpdateOrderStatusAsync(string id, string newStatus)
        {
            var payload = new { status = newStatus };
            (await _http.PatchAsync($"{OrdersRoute}/{id}/status", JsonBody(payload))).EnsureSuccessStatusCode();
        }

        public async Task DeleteOrderAsync(string id)
            => (await _http.DeleteAsync($"{OrdersRoute}/{id}")).EnsureSuccessStatusCode();

        public async Task<List<Order>> GetOrdersByCustomerIdAsync(string customerId)
        {
            var all = await GetOrdersAsync();
            return all.Where(o => o.CustomerId == customerId).ToList();
        }

        // ---------- Uploads ----------
        public async Task<string> UploadProofOfPaymentAsync(IFormFile file, string? orderId, string? customerName)
        {
            using var form = new MultipartFormDataContent();
            var sc = new StreamContent(file.OpenReadStream());
            sc.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
            form.Add(sc, "ProofOfPayment", file.FileName);
            if (!string.IsNullOrWhiteSpace(orderId)) form.Add(new StringContent(orderId), "OrderId");
            if (!string.IsNullOrWhiteSpace(customerName)) form.Add(new StringContent(customerName), "CustomerName");

            var resp = await _http.PostAsync(UploadsRoute, form);
            resp.EnsureSuccessStatusCode();

            var doc = await ReadJsonAsync<Dictionary<string, string>>(resp);
            return doc.TryGetValue("fileName", out var name) ? name : file.FileName;
        }

        // ---------- Mapping ----------
        private static Order ToOrder(OrderDto d)
        {
            var status = Enum.TryParse<OrderStatus>(d.Status, ignoreCase: true, out var s)
                ? s : OrderStatus.Submitted;

            return new Order
            {
                Id = d.Id,
                CustomerId = d.CustomerId,
                ProductId = d.ProductId,
                ProductName = d.ProductName,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                OrderDateUtc = d.OrderDateUtc,
                Status = status
            };
        }

        // DTOs that match Functions JSON (camelCase)
        private sealed record OrderDto(
            string Id,
            string CustomerId,
            string ProductId,
            string ProductName,
            int Quantity,
            decimal UnitPrice,
            DateTimeOffset OrderDateUtc,
            string Status);
    }

    // Minimal PATCH extension for HttpClient
    internal static class HttpClientPatchExtensions
    {
        public static Task<HttpResponseMessage> PatchAsync(this HttpClient client, string requestUri, HttpContent content)
            => client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, requestUri) { Content = content });
    }
}
