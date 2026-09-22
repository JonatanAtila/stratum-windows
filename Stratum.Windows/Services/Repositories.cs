// Stratum.Windows - SQLite repositories, ported from Stratum.Droid.Persistence.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using Stratum.Core.Entity;
using Stratum.Core.Persistence;
using Stratum.Core.Persistence.Exception;

namespace Stratum.Windows.Services
{
    public abstract class AsyncRepository<T, TU> : IAsyncRepository<T, TU> where T : new()
    {
        private readonly Database _database;

        protected AsyncRepository(Database database)
        {
            _database = database;
        }

        protected Task<SQLiteAsyncConnection> GetConnectionAsync()
        {
            return _database.GetConnectionAsync();
        }

        public async Task CreateAsync(T item)
        {
            var conn = await GetConnectionAsync();

            try
            {
                await conn.InsertAsync(item);
            }
            catch (SQLiteException e)
            {
                throw new EntityDuplicateException(e);
            }
        }

        public async Task<T> GetAsync(TU id)
        {
            var conn = await GetConnectionAsync();

            try
            {
                return await conn.GetAsync<T>(id);
            }
            catch (InvalidOperationException)
            {
                return default;
            }
        }

        public async Task<List<T>> GetAllAsync()
        {
            var conn = await GetConnectionAsync();
            return await conn.Table<T>().ToListAsync();
        }

        public async Task UpdateAsync(T item)
        {
            var conn = await GetConnectionAsync();
            await conn.UpdateAsync(item);
        }

        public async Task DeleteAsync(T item)
        {
            var conn = await GetConnectionAsync();
            await conn.DeleteAsync(item);
        }
    }

    public class AuthenticatorRepository : AsyncRepository<Authenticator, string>, IAuthenticatorRepository
    {
        private readonly Database _database;

        public AuthenticatorRepository(Database database) : base(database)
        {
            _database = database;
        }

        public async Task ChangeSecretAsync(string oldSecret, string newSecret)
        {
            var conn = await _database.GetConnectionAsync();

            try
            {
                await conn.ExecuteAsync("UPDATE authenticator SET secret = ? WHERE secret = ?", newSecret, oldSecret);
            }
            catch (SQLiteException e)
            {
                throw new EntityDuplicateException(e);
            }
        }
    }

    public class CategoryRepository : AsyncRepository<Category, string>, ICategoryRepository
    {
        public CategoryRepository(Database database) : base(database) { }
    }

    public class CustomIconRepository : AsyncRepository<CustomIcon, string>, ICustomIconRepository
    {
        public CustomIconRepository(Database database) : base(database) { }
    }

    public class IconPackRepository : AsyncRepository<IconPack, string>, IIconPackRepository
    {
        public IconPackRepository(Database database) : base(database) { }
    }

    public class AuthenticatorCategoryRepository : IAuthenticatorCategoryRepository
    {
        private readonly Database _database;

        public AuthenticatorCategoryRepository(Database database)
        {
            _database = database;
        }

        public async Task CreateAsync(AuthenticatorCategory item)
        {
            var conn = await _database.GetConnectionAsync();
            var id = new ValueTuple<string, string>(item.AuthenticatorSecret, item.CategoryId);

            if (await GetAsync(id) != null)
                throw new EntityDuplicateException();

            await conn.InsertAsync(item);
        }

        public async Task<AuthenticatorCategory> GetAsync(ValueTuple<string, string> id)
        {
            var conn = await _database.GetConnectionAsync();
            var (authSecret, categoryId) = id;

            try
            {
                return await conn.GetAsync<AuthenticatorCategory>(ac =>
                    ac.AuthenticatorSecret == authSecret && ac.CategoryId == categoryId);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public async Task<List<AuthenticatorCategory>> GetAllAsync()
        {
            var conn = await _database.GetConnectionAsync();
            return await conn.Table<AuthenticatorCategory>().ToListAsync();
        }

        public async Task UpdateAsync(AuthenticatorCategory item)
        {
            var conn = await _database.GetConnectionAsync();
            await conn.ExecuteAsync(
                "UPDATE authenticatorcategory SET authenticatorSecret = ?, categoryId = ?, ranking = ? WHERE authenticatorSecret = ? AND categoryId = ?",
                item.AuthenticatorSecret, item.CategoryId, item.Ranking, item.AuthenticatorSecret, item.CategoryId);
        }

        public async Task DeleteAsync(AuthenticatorCategory item)
        {
            var conn = await _database.GetConnectionAsync();
            await conn.ExecuteAsync(
                "DELETE FROM authenticatorcategory WHERE authenticatorSecret = ? AND categoryId = ?",
                item.AuthenticatorSecret, item.CategoryId);
        }

        public async Task<List<AuthenticatorCategory>> GetAllForAuthenticatorAsync(Authenticator auth)
        {
            var conn = await _database.GetConnectionAsync();
            return await conn.Table<AuthenticatorCategory>().Where(ac => ac.AuthenticatorSecret == auth.Secret).ToListAsync();
        }

        public async Task<List<AuthenticatorCategory>> GetAllForCategoryAsync(Category category)
        {
            var conn = await _database.GetConnectionAsync();
            return await conn.Table<AuthenticatorCategory>().Where(ac => ac.CategoryId == category.Id).ToListAsync();
        }

        public async Task DeleteAllForAuthenticatorAsync(Authenticator authenticator)
        {
            var conn = await _database.GetConnectionAsync();
            await conn.ExecuteAsync("DELETE FROM authenticatorcategory WHERE authenticatorSecret = ?", authenticator.Secret);
        }

        public async Task DeleteAllForCategoryAsync(Category category)
        {
            var conn = await _database.GetConnectionAsync();
            await conn.ExecuteAsync("DELETE FROM authenticatorcategory WHERE categoryId = ?", category.Id);
        }

        public async Task TransferCategoryAsync(Category initial, Category next)
        {
            var conn = await _database.GetConnectionAsync();
            await conn.ExecuteAsync("UPDATE authenticatorcategory SET categoryId = ? WHERE categoryId = ?", next.Id, initial.Id);
        }

        public async Task TransferAuthenticatorAsync(Authenticator initial, Authenticator next)
        {
            var conn = await _database.GetConnectionAsync();
            await conn.ExecuteAsync("UPDATE authenticatorcategory SET authenticatorSecret = ? WHERE authenticatorSecret = ?",
                next.Secret, initial.Secret);
        }
    }

    public class IconPackEntryRepository : IIconPackEntryRepository
    {
        private readonly Database _database;

        public IconPackEntryRepository(Database database)
        {
            _database = database;
        }

        public async Task CreateAsync(IconPackEntry item)
        {
            var conn = await _database.GetConnectionAsync();
            var id = new ValueTuple<string, string>(item.IconPackName, item.Name);

            if (await GetAsync(id) != null)
                throw new EntityDuplicateException();

            await conn.InsertAsync(item);
        }

        public async Task CreateManyAsync(List<IconPackEntry> items)
        {
            var conn = await _database.GetConnectionAsync();
            await conn.InsertAllAsync(items);
        }

        public async Task<IconPackEntry> GetAsync(ValueTuple<string, string> id)
        {
            var conn = await _database.GetConnectionAsync();
            var (packName, name) = id;

            try
            {
                return await conn.GetAsync<IconPackEntry>(e => e.IconPackName == packName && e.Name == name);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public async Task<List<IconPackEntry>> GetAllAsync()
        {
            var conn = await _database.GetConnectionAsync();
            return await conn.Table<IconPackEntry>().ToListAsync();
        }

        public async Task UpdateAsync(IconPackEntry item)
        {
            var conn = await _database.GetConnectionAsync();
            await conn.ExecuteAsync(
                "UPDATE iconpackentry SET iconPackName = ?, name = ?, data = ? WHERE iconPackName = ? AND name = ?",
                item.IconPackName, item.Name, item.Data, item.IconPackName, item.Name);
        }

        public async Task DeleteAsync(IconPackEntry item)
        {
            var conn = await _database.GetConnectionAsync();
            await conn.ExecuteAsync(
                "DELETE FROM iconpackentry WHERE iconPackName = ? AND name = ?", item.IconPackName, item.Name);
        }

        public async Task<List<IconPackEntry>> GetAllForPackAsync(IconPack pack)
        {
            var conn = await _database.GetConnectionAsync();
            return await conn.Table<IconPackEntry>().Where(e => e.IconPackName == pack.Name).ToListAsync();
        }

        public async Task DeleteAllForPackAsync(IconPack pack)
        {
            var conn = await _database.GetConnectionAsync();
            await conn.ExecuteAsync("DELETE FROM iconpackentry WHERE iconPackName = ?", pack.Name);
        }
    }
}
