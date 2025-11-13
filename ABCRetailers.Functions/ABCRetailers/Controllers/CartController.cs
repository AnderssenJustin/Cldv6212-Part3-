using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ABCRetailers.Data;
using ABCRetailers.Models;
using ABCRetailers.Models.ViewModels;
using ABCRetailers.Services;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.Operations;

namespace ABCRetailers.Controllers
{
    [Authorize(Roles = "Customer")] // ✅ Restrict to logged-in customers only
    public class CartController : Controller
    {
        private readonly AuthDbContext _db;
        private readonly IFunctionsApi _api;

        public CartController(AuthDbContext db, IFunctionsApi api)
        {
            _db = db;
            _api = api;
        }

        // ======================================================
        // GET: /Cart/Index → Display items in cart
        // ======================================================
        public async Task<IActionResult> Index()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
                return RedirectToAction("Index", "Login");

            var cartItems = await _db.Cart
                .Where(c => c.CustomerUsername == username)
                .ToListAsync();
            if (!cartItems.Any())
            {
                // ✅ return a model with empty list, no product fetch loop
                return View(new CartPageViewModel { Items = new List<CartItemViewModel>() });
            }
            var viewModelList = new List<CartItemViewModel>();

            // ✅ For each cart item, fetch product info from Azure
            foreach (var item in cartItems)
            {
                var product = await _api.GetProductAsync(item.ProductId);
                if (product == null) continue;

                viewModelList.Add(new CartItemViewModel
                {
                    //ProductId = product.Id, added thsi 
                    ProductId = item.ProductId,
                    ProductName = product.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = product.Price
                });
            }

            // ✅ Return the cart view model
            return View(new CartPageViewModel { Items = viewModelList });
        }

        // ======================================================
        // GET: /Cart/Add → Add product to cart
        // ======================================================
        public async Task<IActionResult> Add(string productId)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(productId))
                return RedirectToAction("Index", "Product");

            var product = await _api.GetProductAsync(productId);
            if (product == null)
                return NotFound();

            var existing = await _db.Cart.FirstOrDefaultAsync(c =>
                c.ProductId == productId && c.CustomerUsername == username);

            // ✅ Increment quantity if product already in cart
            if (existing != null)
            {
                existing.Quantity += 1;
            }
            else
            {
                _db.Cart.Add(new Cart
                {
                    CustomerUsername = username,
                    ProductId = productId,
                    Quantity = 1
                });
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = $"{product.ProductName} added to cart.";
            return RedirectToAction("Index", "Product");
        }

        // ======================================================
        // POST: /Cart/Checkout → Place orders for all items in cart
        // ======================================================
        [HttpPost]
        public async Task<IActionResult> Checkout()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
                return RedirectToAction("Index", "Login");

            // ✅ Step 1: Get actual Customer from Azure Table using Username
            var customer = await _api.GetCustomerByUsernameAsync(username);
            if (customer == null)
            {
                TempData["Error"] = "Customer not found.";
                return RedirectToAction("Index");
            }

            // ✅ Step 2: Fetch all cart items from local DB
            var cartItems = await _db.Cart
                .Where(c => c.CustomerUsername == username)
                .ToListAsync();

            if (!cartItems.Any())
            {
                TempData["Error"] = "Your cart is empty.";
                return RedirectToAction("Index");
            }

            // ✅ Step 3: Create Azure orders for each item (reduces stock automatically)
            foreach (var item in cartItems)
            {
                await _api.CreateOrderAsync(customer.Id, item.ProductId, item.Quantity);
            }

            // ✅ Step 4: Clear local cart after successful checkout
            _db.Cart.RemoveRange(cartItems);
            await _db.SaveChangesAsync();

            // ✅ Step 5: Add confirmation message and redirect to confirmation page
            TempData["SuccessMessage"] = "✅ Order placed successfully!";
            return RedirectToAction("Confirmation");
        }

        // ======================================================
        // GET: /Cart/Confirmation → Simple confirmation page after checkout
        // ======================================================
        public IActionResult Confirmation()
        {
            // ✅ Display success message on a dedicated confirmation page
            ViewBag.Message = TempData["SuccessMessage"] ?? "Thank you for your purchase!";
            return View();
        }

        // ======================================================
        // POST: /Cart/Remove → Remove product from cart
        // ======================================================
        // ======================================================
        // POST: /Cart/Remove → Remove product from cart
        // ======================================================
        [HttpPost]
        public async Task<IActionResult> Remove(string productId)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return RedirectToAction("Index");

            // ✅ Use exact match - no ToLower() which can cause inconsistencies
            var item = await _db.Cart.FirstOrDefaultAsync(c =>
                c.CustomerUsername == username &&
                c.ProductId == productId);

            if (item != null)
            {
                _db.Cart.Remove(item);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Item removed from cart.";
            }
            else
            {
                // Optional: Add feedback if item wasn't found
                TempData["Error"] = "Item not found in cart.";
            }

            return RedirectToAction(nameof(Index));
        }
        /*[HttpPost]
        public async Task<IActionResult> Remove(string productId)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return RedirectToAction("Index");

            var item = await _db.Cart.FirstOrDefaultAsync(c =>
                c.CustomerUsername == username &&
                c.ProductId.ToLower() == productId.ToLower()); // chnaged this 

            if (item != null)
            {
                _db.Cart.Remove(item);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Item removed from cart.";
            }

            return RedirectToAction(nameof(Index));
        }*/

        // ======================================================
        // POST: /Cart/UpdateQuantities → Update quantities in cart
        // ======================================================
        [HttpPost]
        public async Task<IActionResult> UpdateQuantities(List<CartItemViewModel> items)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return RedirectToAction("Index");

            foreach (var item in items)
            {
                var cartItem = await _db.Cart.FirstOrDefaultAsync(c =>
                    c.CustomerUsername == username && c.ProductId == item.ProductId);

                if (cartItem != null)
                {
                    cartItem.Quantity = item.Quantity;
                }
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "Cart updated successfully.";
            return RedirectToAction("Index");
        }
    }
}