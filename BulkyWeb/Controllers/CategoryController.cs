﻿using BulkyBook.DataAccess;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using Microsoft.AspNetCore.Mvc;

namespace BulkyBookWeb.Controllers
{
    public class CategoryController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        public CategoryController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public IActionResult Index()
        {
            List<Category> objCategoryList = _unitOfWork.Category.GetAll().ToList();
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
                _unitOfWork.Category.Add(obj);
                _unitOfWork.Save();
                TempData["success"] = "Category created successfully";
                return RedirectToAction("Index");
            }
            return View();
        }

        public IActionResult Edit(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            Category? categoryFromDb = _unitOfWork.Category.Get(x => x.Id == id);
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
                _unitOfWork.Category.Update(obj);
                _unitOfWork.Save();
                TempData["success"] = "Category updated successfully";
                return RedirectToAction("Index");
            }
            return View();
        }

        public IActionResult Delete(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            Category? categoryFromDb = _unitOfWork.Category.Get(x => x.Id == id);
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

            Category? categoryFromDb = _unitOfWork.Category.Get(x => x.Id == id);
            if (categoryFromDb == null)
                return NotFound();

            _unitOfWork.Category.Remove(categoryFromDb);
            _unitOfWork.Save();
            TempData["success"] = "Category deleted successfully";
            return RedirectToAction("Index");
        }
    }
}
