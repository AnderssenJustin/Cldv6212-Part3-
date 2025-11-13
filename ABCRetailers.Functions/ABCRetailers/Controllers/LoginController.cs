

using ABCRetailers.Data;
using ABCRetailers.Models;
using ABCRetailers.Models.ViewModels;
using ABCRetailers.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ABCRetailers.Controllers
{
    public class LoginController : Controller
    {
        private readonly AuthDbContext _db;
        private readonly IFunctionsApi _functionsApi;
        private readonly ILogger<LoginController> _logger;

        public LoginController(AuthDbContext db, IFunctionsApi functionsApi, ILogger<LoginController> logger)
        {
            _db = db;
            _functionsApi = functionsApi;
            _logger = logger;
        }

        // =====================================
        // GET: /Login
        // =====================================
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Index(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel());
        }

        // =====================================
        // POST: /Login
        // =====================================
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                // 1️⃣ Verify user in database
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == model.Username);
                if (user == null)
                {
                    ViewBag.Error = "Invalid username or password.";
                    return View(model);
                }

                // 2️⃣ Password check
                if (user.PasswordHash != model.Password) // TODO: replace with hashed password
                {
                    ViewBag.Error = "Invalid username or password.";
                    return View(model);
                }

                // 3️⃣ Prevent unauthorized Admin login
                if (user.Role != "Admin" && model.Role == "Admin")
                {
                    _logger.LogWarning("Unauthorized Admin login attempt by user {Username}", user.Username);
                    ViewBag.Error = "Unauthorized login attempt.";
                    return View(model);
                }

                // 4️⃣ Fetch customer record only if role is Customer
                Customer? customer = null;
                if (user.Role == "Customer")
                {
                    customer = await _functionsApi.GetCustomerByUsernameAsync(user.Username);
                    if (customer == null)
                    {
                        _logger.LogWarning("No matching customer found in Azure for username {Username}", user.Username);
                        ViewBag.Error = "No customer record found in the system.";
                        return View(model);
                    }
                }

                // 5️⃣ Build authentication claims
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role)
                };
                if (customer != null)
                    claims.Add(new Claim("CustomerId", customer.Id));

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                // 6️⃣ Sign in
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(60)
                    });

                // 7️⃣ Store session
                HttpContext.Session.SetString("Username", user.Username);
                HttpContext.Session.SetString("Role", user.Role);
                if (customer != null)
                    HttpContext.Session.SetString("CustomerId", customer.Id);

                // 8️⃣ Redirect appropriately
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return user.Role switch
                {
                    "Admin" => RedirectToAction("AdminDashboard", "Home"),
                    "Customer" => RedirectToAction("CustomerDashboard", "Home"),
                    _ => RedirectToAction("Index", "Home")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected login error for user {Username}", model.Username);
                ViewBag.Error = "Unexpected error occurred during login. Please try again later.";
                return View(model);
            }
        }

        // =====================================
        // GET: /Login/Register
        // =====================================
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        // =====================================
        // POST: /Login/Register
        // =====================================
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // 1️⃣ Check duplicate username
            var exists = await _db.Users.AnyAsync(u => u.Username == model.Username);
            if (exists)
            {
                ViewBag.Error = "Username already exists.";
                return View(model);
            }

            try
            {
                // 2️⃣ Save local user (SQL) — force role to "Customer"
                var user = new User
                {
                    Username = model.Username,
                    PasswordHash = model.Password, // TODO: replace with hashed password
                    Role = model.Role 
                };
                _db.Users.Add(user);
                await _db.SaveChangesAsync();

                // 3️⃣ Save to Azure Function
                var customer = new Customer
                {
                    Username = model.Username,
                    Name = model.FirstName,
                    Surname = model.LastName,
                    Email = model.Email,
                    ShippingAddress = model.ShippingAddress
                };

                await _functionsApi.CreateCustomerAsync(customer);

                TempData["Success"] = "Registration successful! Please log in.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration failed for user {Username}", model.Username);
                ViewBag.Error = "Could not complete registration. Please try again later.";
                return View(model);
            }
        }

        // =====================================
        // LOGOUT
        // =====================================
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        // =====================================
        // ACCESS DENIED
        // =====================================
        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied() => View();
    }
}