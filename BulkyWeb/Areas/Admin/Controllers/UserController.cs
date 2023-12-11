using BulkyBook.DataAccess;
using BulkyBook.DataAccess.Repository;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace BulkyBookWeb.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]

    public class UserController : Controller
    {
        private readonly ApplicationDbContext _db;
        public UserController(ApplicationDbContext db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult RoleManagement(string userId)
        {
            var roleId = _db.UserRoles.FirstOrDefault(x => x.UserId == userId).RoleId;

            RoleManagementVM roleVM = new()
            {
                ApplicationUser = _db.ApplicationUsers.Include(x => x.Company).FirstOrDefault(x => x.Id == userId),
                RoleList = _db.Roles.Select(x => new SelectListItem
                {
                    Text = x.Name,
                    Value = x.Name
                }),
                CompanyList = _db.Companies.Select(x => new SelectListItem
                {
                    Text = x.Name,
                    Value = x.Id.ToString()
                })
            };

            roleVM.ApplicationUser.Role = _db.Roles.FirstOrDefault(x => x.Id == roleId).Name;

            return View(roleVM);
        }



        #region API CALLS
        [HttpGet]
        public IActionResult GetAll()
        {
            List<ApplicationUser> userList = _db.ApplicationUsers.Include(x => x.Company).ToList();

            var userRoles = _db.UserRoles.ToList();
            var roles = _db.Roles.ToList();

            foreach (ApplicationUser user in userList)
            {
                var roleId = userRoles.FirstOrDefault(x => x.UserId == user.Id).RoleId;
                user.Role = roles.FirstOrDefault(x => x.Id == roleId).Name;

                if (user.Company == null)
                {
                    user.Company = new Company() { Name = "" };
                }
            }
            return Json(new { data = userList });
        }

        [HttpPost]
        public IActionResult LockUnlock([FromBody] string id)
        {
            ApplicationUser userFromDb = _db.ApplicationUsers.FirstOrDefault(x => x.Id == id);

            if (userFromDb == null)
                return Json(new { success = false, message = "Error while Locking/Unlocking" });

            if (userFromDb.LockoutEnd != null && userFromDb.LockoutEnd > DateTime.Now)
                userFromDb.LockoutEnd = DateTime.Now;
            else
                userFromDb.LockoutEnd = DateTime.Now.AddYears(1000);

            _db.SaveChanges();
            return Json(new { success = true, message = "Operation Successful" });
        }
        #endregion
    }
}
