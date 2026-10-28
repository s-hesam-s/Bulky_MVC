using System.ComponentModel.DataAnnotations;

namespace BulkyBook.Models.ViewModels;

public class CheckoutInput
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(30)] public string PhoneNumber { get; set; } = "";
    [Required, StringLength(300)] public string StreetAddress { get; set; } = "";
    [Required, StringLength(100)] public string City { get; set; } = "";
    [Required, StringLength(100)] public string State { get; set; } = "";
    [Required, StringLength(20)] public string PostalCode { get; set; } = "";
}
