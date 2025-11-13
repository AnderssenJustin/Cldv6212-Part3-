using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using ABCRetailers.Functions.Entities;   
using ABCRetailers.Functions.Helpers;    
using ABCRetailers.Functions.Models;     


namespace ABCRetailers.Functions.Functions;
public class CustomersFunctions
{
    private readonly string _conn;
    private readonly string _table;

    public CustomersFunctions(IConfiguration cfg)
    {
        _conn = cfg["STORAGE_CONNECTION"] ?? throw new InvalidOperationException("STORAGE_CONNECTION missing");
        _table = cfg["TABLE_CUSTOMER"] ?? "Customer";
    }

    [Function("Customers_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers")] HttpRequestData req)
    {
        var table = new TableClient(_conn, _table);
        await table.CreateIfNotExistsAsync();

        var items = new List<CustomerDto>();
        await foreach (var e in table.QueryAsync<CustomerEntity>(x => x.PartitionKey == "Customer"))
            items.Add(Map.ToDto(e));

        return HttpJson.Ok(req, items);
    }

    [Function("Customers_Get")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers/{id}")] HttpRequestData req, string id)
    {
        var table = new TableClient(_conn, _table);
        try
        {
            var e = await table.GetEntityAsync<CustomerEntity>("Customer", id);
            return HttpJson.Ok(req, Map.ToDto(e.Value));
        }
        catch
        {
            return HttpJson.NotFound(req, "Customer not found");
        }
    }

    public record CustomerCreateUpdate(string? Name, string? Surname, string? Username, string? Email, string? ShippingAddress);

    [Function("Customers_Create")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customers")] HttpRequestData req)
    {
        var input = await HttpJson.ReadAsync<CustomerCreateUpdate>(req);
        if (input is null || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Email))
            return HttpJson.Bad(req, "Name and Email are required");

        var table = new TableClient(_conn, _table);
        await table.CreateIfNotExistsAsync();

        var e = new CustomerEntity
        {
            Name = input.Name!,
            Surname = input.Surname ?? "",
            Username = input.Username ?? "",
            Email = input.Email!,
            ShippingAddress = input.ShippingAddress ?? ""
        };
        await table.AddEntityAsync(e);

        return HttpJson.Created(req, Map.ToDto(e));
    }
    

    [Function("Customers_Update")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customers/{id}")] HttpRequestData req, string id)
    {
        var input = await HttpJson.ReadAsync<CustomerCreateUpdate>(req);
        if (input is null) return HttpJson.Bad(req, "Invalid body");

        var table = new TableClient(_conn, _table);
        try
        {
            var resp = await table.GetEntityAsync<CustomerEntity>("Customer", id);
            var e = resp.Value;

            e.Name = input.Name ?? e.Name;
            e.Surname = input.Surname ?? e.Surname;
            e.Username = input.Username ?? e.Username;
            e.Email = input.Email ?? e.Email;
            e.ShippingAddress = input.ShippingAddress ?? e.ShippingAddress;

            await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);
            return HttpJson.Ok(req, Map.ToDto(e));
        }
        catch
        {
            return HttpJson.NotFound(req, "Customer not found");
        }
    }

    [Function("Customers_Delete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "customers/{id}")] HttpRequestData req, string id)
    {
        var table = new TableClient(_conn, _table);
        await table.DeleteEntityAsync("Customer", id);
        return HttpJson.NoContent(req);
    }
}
