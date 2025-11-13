
// Models/Order.cs
using System.ComponentModel.DataAnnotations;

namespace ABCRetailers.Models
{
    public enum OrderStatus
    {
        Submitted,
        Processing,
        Completed,
        Cancelled
    }

    public class Order
    {
        [Display(Name = "Order ID")]
        public string Id { get; set; } = string.Empty; // set from Function response

        [Required, Display(Name = "Customer")]
        public string CustomerId { get; set; } = string.Empty;

        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required, Display(Name = "Product")]
        public string ProductId { get; set; } = string.Empty;

        [Display(Name = "Product Name")]
        public string ProductName { get; set; } = string.Empty;

        // set by Function (server truth)
        [Display(Name = "Order Placed (UTC)")]
        public DateTimeOffset? OrderDateUtc { get; set; }

        [Required, Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
        [Display(Name = "Quantity")]
        public int Quantity { get; set; }

        [Display(Name = "Unit Price"), DataType(DataType.Currency)]
        public decimal UnitPrice { get; set; }

        [Display(Name = "Total")]
        public decimal TotalAmount => UnitPrice * Quantity;

        [Required, Display(Name = "Status")]
        public OrderStatus Status { get; set; } = OrderStatus.Submitted;
    }
}
