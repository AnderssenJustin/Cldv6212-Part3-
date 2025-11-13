using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using System.ComponentModel.DataAnnotations.Schema;

namespace ABCRetailers.Models
    
{
    [Table("Users")]
    public class User
    {
        [Key]

        public int Id { get; set; }

        [Required(ErrorMessage = "Username is required")]
        [MaxLength(100)]
        [Display(Name = "Username")]

        public string Username { get; set; }

        [Required(ErrorMessage ="Password is required")]
        [MaxLength(256)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]

        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        [Display(Name = "Role")]

        public string Role { get; set; } = "Customer";

    }
}
