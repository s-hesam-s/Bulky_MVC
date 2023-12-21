using BulkyBook.DataAccess;
using BulkyBook.DataAccess.Repository;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        public UserController(IUnitOfWork unitOfWork, UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult RoleManagement(string userId)
        {
            //var roleId = _roleManager.FirstOrDefault(x => x.UserId == userId).RoleId;

            RoleManagementVM roleVM = new()
            {
                //ApplicationUser = _db.ApplicationUsers.Include(x => x.Company).FirstOrDefault(x => x.Id == userId),
                ApplicationUser = _unitOfWork.ApplicationUser.Get(x => x.Id == userId, includeProperties: "Company"),
                RoleList = _roleManager.Roles.Select(x => new SelectListItem
                {
                    Text = x.Name,
                    Value = x.Name
                }),
                CompanyList = _unitOfWork.Company.GetAll().Select(x => new SelectListItem
                {
                    Text = x.Name,
                    Value = x.Id.ToString()
                })
            };

            //roleVM.ApplicationUser.Role = _db.Roles.FirstOrDefault(x => x.Id == roleId).Name;
            roleVM.ApplicationUser.Role = _userManager.GetRolesAsync(_unitOfWork.ApplicationUser.Get(x =>
            x.Id == userId)).GetAwaiter().GetResult().FirstOrDefault();

            return View(roleVM);
        }

        [HttpPost]
        public IActionResult RoleManagement(RoleManagementVM roleManagementVM)
        {
            //var roleId = _db.UserRoles.FirstOrDefault(x => x.UserId == roleManagementVM.ApplicationUser.Id).RoleId;
            //var oldRole = _db.Roles.FirstOrDefault(x => x.Id == roleId).Name;
            var oldRole = _userManager.GetRolesAsync(_unitOfWork.ApplicationUser.Get(x =>
            x.Id == roleManagementVM.ApplicationUser.Id)).GetAwaiter().GetResult().FirstOrDefault();

            ApplicationUser userFromDb = _unitOfWork.ApplicationUser.Get(x => x.Id == roleManagementVM.ApplicationUser.Id);

            if (oldRole != roleManagementVM.ApplicationUser.Role)
            {
                if (roleManagementVM.ApplicationUser.Role == SD.Role_Company)
                {
                    userFromDb.CompanyId = roleManagementVM.ApplicationUser.CompanyId;
                }
                if (oldRole == SD.Role_Company)
                {
                    userFromDb.CompanyId = null;
                }

                _unitOfWork.ApplicationUser.Update(userFromDb);
                _unitOfWork.Save();

                _userManager.RemoveFromRoleAsync(userFromDb, oldRole).GetAwaiter().GetResult();
                _userManager.AddToRoleAsync(userFromDb, roleManagementVM.ApplicationUser.Role).GetAwaiter().GetResult();
            }
            else if (roleManagementVM.ApplicationUser.Role == SD.Role_Company &&
                     roleManagementVM.ApplicationUser.CompanyId != userFromDb.CompanyId)
            {
                userFromDb.CompanyId = roleManagementVM.ApplicationUser.CompanyId;
                _unitOfWork.ApplicationUser.Update(userFromDb);
                _unitOfWork.Save();
            }

            return RedirectToAction(nameof(Index));
        }


        #region API CALLS
        [HttpGet]
        public IActionResult GetAll()
        {
            //List<ApplicationUser> userList = _db.ApplicationUsers.Include(x => x.Company).ToList();
            List<ApplicationUser> userList = _unitOfWork.ApplicationUser.GetAll(includeProperties: "Company").ToList();

            //var userRoles = _db.UserRoles.ToList();
            //var roles = _db.Roles.ToList();

            foreach (ApplicationUser user in userList)
            {
                //var roleId = userRoles.FirstOrDefault(x => x.UserId == user.Id).RoleId;
                //user.Role = roles.FirstOrDefault(x => x.Id == roleId).Name;

                user.Role = _userManager.GetRolesAsync(user).GetAwaiter().GetResult().FirstOrDefault();

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
            ApplicationUser userFromDb = _unitOfWork.ApplicationUser.Get(x => x.Id == id);

            if (userFromDb == null)
                return Json(new { success = false, message = "Error while Locking/Unlocking" });

            if (userFromDb.LockoutEnd != null && userFromDb.LockoutEnd > DateTime.Now)
                userFromDb.LockoutEnd = DateTime.Now;
            else
                userFromDb.LockoutEnd = DateTime.Now.AddYears(1000);

            _unitOfWork.ApplicationUser.Update(userFromDb);
            _unitOfWork.Save();
            return Json(new { success = true, message = "Operation Successful" });
        }
        #endregion
    }
}
