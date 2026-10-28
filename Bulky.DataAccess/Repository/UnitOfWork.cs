using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace BulkyBook.DataAccess.Repository
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _db;
        public ICategoryRepository Category { get; private set; }
        public IProductRepository Product { get; private set; }
        public ICompanyRepository Company { get; private set; }
        public IShoppingCartRepository ShoppingCart { get; private set; }
        public IApplicationUserRepository ApplicationUser { get; private set; }
        public IOrderHeaderRepository OrderHeader { get; private set; }
        public IOrderDetailRepository OrderDetail { get; private set; }
        public IProductImageRepository ProductImage { get; private set; }

        public UnitOfWork(ApplicationDbContext db)
        {
            _db = db;
            Category = new CategoryRepository(_db);
            Product = new ProductRepository(_db);
            Company = new CompanyRepository(_db);
            ShoppingCart = new ShoppingCartRepository(_db);
            ApplicationUser = new ApplicationUserRepository(_db);
            OrderHeader = new OrderHeaderRepository(_db);
            OrderDetail = new OrderDetailRepository(_db);
            ProductImage = new ProductImageRepository(_db);
        }

        public T ExecuteInTransaction<T>(Func<T> operation)
        {
            using var transaction = _db.Database.BeginTransaction(IsolationLevel.Serializable);
            var result = operation();
            transaction.Commit();
            return result;
        }

        public T ExecuteWithOrderLock<T>(int orderId, Func<T> operation) => ExecuteInTransaction(() =>
        {
            _db.OrderHeaders.FromSqlInterpolated($"SELECT * FROM [OrderHeaders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {orderId}")
                .AsNoTracking().ToList();
            // A newly created order may already be tracked before an external
            // checkout call. Refresh it only after obtaining the database lock.
            foreach (var entry in _db.ChangeTracker.Entries<OrderHeader>().Where(e => e.Entity.Id == orderId).ToList())
                entry.Reload();
            return operation();
        });

        public void Save()
        {
            _db.SaveChanges();
        }
    }
}
