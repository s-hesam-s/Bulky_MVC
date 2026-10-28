using System.ComponentModel.DataAnnotations;

namespace BulkyBook.Models.ViewModels;

public class ShipmentInput
{
    [Range(1, int.MaxValue)] public int Id { get; set; }
    [Required, StringLength(100)] public string Carrier { get; set; } = "";
    [Required, StringLength(100)] public string TrackingNumber { get; set; } = "";
}
