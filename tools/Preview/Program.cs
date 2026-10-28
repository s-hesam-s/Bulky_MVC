using BulkyBook.Models;
using BulkyBook.DataAccess.Repository.IRepository;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;

var root = PreviewSetup.Root("BulkyWeb"); var web = typeof(BulkyBookWeb.Areas.Customer.Controllers.HomeController).Assembly;
var builder = PreviewSetup.Builder(root, 8840, Path.Combine(root, "BulkyWeb", "wwwroot"), web.GetName().Name);
builder.Services.AddHttpContextAccessor(); builder.Services.AddDistributedMemoryCache(); builder.Services.AddSession();
builder.Services.AddAuthentication().AddCookie(); builder.Services.AddIdentityCore<IdentityUser>().AddSignInManager();
builder.Services.AddSingleton<IUserStore<IdentityUser>>(new Mock<IUserStore<IdentityUser>>().Object);
builder.Services.AddSingleton<IUnitOfWork>(new Mock<IUnitOfWork>(MockBehavior.Strict).Object);
builder.Services.AddControllersWithViews().AddApplicationPart(web).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new PreviewControllers()));
var app = builder.Build(); PreviewSetup.Guard(app, true); app.UseSession(); app.UseStaticFiles();
app.MapControllerRoute("preview", "{controller=Preview}/{action=Index}"); app.MapGet("/Home", () => Results.Redirect("/")); app.Run();

public sealed class PreviewController : Controller
{
    [HttpGet] public IActionResult Index() => View("~/Areas/Customer/Views/Home/Index.cshtml", products());
    [HttpGet]
    public IActionResult Details(int productId)
    { var product = products().SingleOrDefault(p => p.Id == productId); return product is null ? NotFound() : View("~/Areas/Customer/Views/Home/Details.cshtml", new ShoppingCart { ProductId = productId, Count = 1, Product = product }); }
    private static Product[] products()
    {
        var root = PreviewSetup.Root("BulkyWeb"); var folder = Path.Combine(root, "BulkyWeb", "wwwroot", "images", "product");
        return Directory.GetFiles(folder, "*.jpg").Order().Take(3).Select((file, index) => new Product { Id = index + 1, Title = $"Sample book {index + 1}", Author = "Fixture author", ISBN = $"FIXTURE-{index + 1}", Description = "Synthetic catalog entry rendered by the existing bookstore view.", ListPrice = 30 + index * 5, Price = 25 + index * 5, Price50 = 23 + index * 5, Price100 = 20 + index * 5, CategoryId = 1, Category = new Category { Name = "Sample books" }, ProductImages = new List<ProductImage> { new() { ImageUrl = "/images/product/" + Path.GetFileName(file) } } }).ToArray();
    }
}
