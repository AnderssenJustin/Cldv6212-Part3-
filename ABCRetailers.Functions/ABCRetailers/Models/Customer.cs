using System.ComponentModel.DataAnnotations;

namespace ABCRetailers.Models
{
    public class Customer
    {
        [Display(Name = "Customer ID")]
        public string Id { get; set; } = string.Empty; // set from Function response

        [Required, Display(Name = "First Name")]
        public string Name { get; set; } = string.Empty;

        [Required, Display(Name = "Last Name")]
        public string Surname { get; set; } = string.Empty;

        [Required, Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required, EmailAddress, Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required, Display(Name = "Shipping Address")]
        public string ShippingAddress { get; set; } = string.Empty;
    }
}
