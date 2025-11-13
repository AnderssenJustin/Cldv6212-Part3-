
// Models/Product.cs
using System.ComponentModel.DataAnnotations;

namespace ABCRetailers.Models
{
    public class Product
    {
        [Display(Name = "Product ID")]
        public string Id { get; set; } = string.Empty; // set from Function response

        [Required(ErrorMessage = "Product name is required")]
        [Display(Name = "Product Name")]
        public string ProductName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required")]
        [Display(Name = "Description")]
        public string Description { get; set; } = string.Empty;

 

        // Models/Product.cs  (only the Range line shown)
        [Required, Display(Name = "Price")]
        [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
               ErrorMessage = "Price must be greater than 0")]
        public decimal Price { get; set; }


        [Required, Display(Name = "Stock Available")]
        public int StockAvailable { get; set; }

        [Display(Name = "Image URL")]
        public string ImageUrl { get; set; } = string.Empty;
    }
}
