using System.ComponentModel.DataAnnotations;

namespace BulkyBook.Models.ViewModels;

public sealed class AddCartInput
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, 1000)] public int Count { get; set; }
}
