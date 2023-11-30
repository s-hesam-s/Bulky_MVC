using Bulky.DataAccess;
using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Microsoft.AspNetCore.Mvc;

namespace BulkyWeb.Controllers
{
    public class CategoryController : Controller
    {
        private readonly ICategoryRepository _categoryRepo;
        public CategoryController(ICategoryRepository categoryRepo)
        {
            _categoryRepo = categoryRepo;
        }

        public IActionResult Index()
        {
            List<Category> objCategoryList = _categoryRepo.GetAll().ToList();
            return View(objCategoryList);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Create(Category obj) 
        {
            //if (obj.Name == obj.DisplayOrder.ToString())
            //    ModelState.AddModelError("name", "The DisplayOrder cannot exactly match the Name.");
            
            if (ModelState.IsValid)
            {
                _categoryRepo.Add(obj);
                _categoryRepo.Save();
                TempData["success"] = "Category created successfully";
                return RedirectToAction("Index");
            }
            return View();
        }

        public IActionResult Edit(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            Category? categoryFromDb = _categoryRepo.Get(x => x.Id == id);
            //Category? categoryFromDb1 = _db.Categories.FirstOrDefault(x => x.Id == id);
            //Category? categoryFromDb2 = _db.Categories.Where(x => x.Id == id).FirstOrDefault();

            if (categoryFromDb == null)
                return NotFound();
            
            return View(categoryFromDb);
        }

        [HttpPost]
        public IActionResult Edit(Category obj)
        {
            if (ModelState.IsValid)
            {
                _categoryRepo.Update(obj);
                _categoryRepo.Save();
                TempData["success"] = "Category updated successfully";
                return RedirectToAction("Index");
            }
            return View();
        }

        public IActionResult Delete(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            Category? categoryFromDb = _categoryRepo.Get(x => x.Id == id);
            //Category? categoryFromDb1 = _db.Categories.FirstOrDefault(x => x.Id == id);
            //Category? categoryFromDb2 = _db.Categories.Where(x => x.Id == id).FirstOrDefault();

            if (categoryFromDb == null)
                return NotFound();

            return View(categoryFromDb);
        }

        [HttpPost, ActionName("Delete")]
        public IActionResult DeletePOST(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            Category? categoryFromDb = _categoryRepo.Get(x => x.Id == id);
            if (categoryFromDb == null)
                return NotFound();

            _categoryRepo.Remove(categoryFromDb);
            _categoryRepo.Save();
            TempData["success"] = "Category deleted successfully";
            return RedirectToAction("Index");
        }
    }
}
