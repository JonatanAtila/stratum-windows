// Stratum.Windows - Windows (WinUI 3) database layer, adapted from Stratum.Droid Database.
// Uses SQLCipher so .db3 files stay compatible with the Android app.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SQLite;
using Stratum.Core.Entity;

namespace Stratum.Windows.Services
{
    public class Database
    {
        private const string FileName = "authenticator.db3";
        private const SQLiteOpenFlags Flags = SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private SQLiteAsyncConnection _connection;

        public static string DataDirOverride { get; set; }

        public static string AppDataDir
        {
            get
            {
                if (!string.IsNullOrEmpty(DataDirOverride))
                {
                    Directory.CreateDirectory(DataDirOverride);
                    return DataDirOverride;
                }

                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stratum");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string DbPath => Path.Combine(AppDataDir, FileName);
        public static bool Exists => File.Exists(DbPath);

        public async Task<SQLiteAsyncConnection> GetConnectionAsync()
        {
            await _lock.WaitAsync();

            try
            {
                if (_connection == null)
                    throw new InvalidOperationException("Connection not open");

                return _connection;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> IsOpenAsync()
        {
            await _lock.WaitAsync();

            try
            {
                return _connection != null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task CloseAsync()
        {
            await _lock.WaitAsync();

            try
            {
                if (_connection == null)
                    return;

                await _connection.CloseAsync();
                _connection = null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task OpenAsync(string password)
        {
            var path = DbPath;
            var firstLaunch = !File.Exists(path);

            if (password == "")
                password = null;

            var connStr = new SQLiteConnectionString(path, Flags, true, password);
            await _lock.WaitAsync();

            try
            {
                if (_connection != null)
                    await _connection.CloseAsync();

                _connection = new SQLiteAsyncConnection(connStr);

                try
                {
                    await MigrateAsync(firstLaunch);
                }
                catch
                {
                    // Already holding _lock here: close directly to avoid
                    // deadlocking on CloseAsync (which also takes _lock).
                    try
                    {
                        await _connection.CloseAsync();
                    }
                    catch
                    {
                        // best-effort cleanup
                    }

                    _connection = null;
                    throw;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task MigrateAsync(bool firstLaunch)
        {
            if (firstLaunch)
                await _connection.EnableWriteAheadLoggingAsync();

            await _connection.CreateTableAsync<Authenticator>();
            await _connection.CreateTableAsync<Category>();
            await _connection.CreateTableAsync<AuthenticatorCategory>();
            await _connection.CreateTableAsync<CustomIcon>();
            await _connection.CreateTableAsync<IconPack>();
            await _connection.CreateTableAsync<IconPackEntry>();
        }

        public async Task SetPasswordAsync(string currentPassword, string newPassword)
        {
            if (currentPassword == newPassword)
                return;

            var dbPath = DbPath;
            var backupPath = dbPath + ".backup";

            void DeleteDatabase()
            {
                File.Delete(dbPath);
                File.Delete(dbPath.Replace("db3", "db3-shm"));
                File.Delete(dbPath.Replace("db3", "db3-wal"));
            }

            void RestoreBackup()
            {
                DeleteDatabase();
                File.Move(backupPath, dbPath);
            }

            File.Copy(dbPath, backupPath, true);
            SQLiteAsyncConnection conn;

            try
            {
                conn = await GetConnectionAsync();
                await conn.ExecuteScalarAsync<string>("PRAGMA wal_checkpoint(TRUNCATE)");
            }
            catch
            {
                File.Delete(backupPath);
                throw;
            }

            if (currentPassword == null || newPassword == null)
            {
                var tempPath = dbPath + ".temp";

                try
                {
                    if (newPassword != null)
                        await conn.ExecuteAsync("ATTACH DATABASE ? AS temporary KEY ?", tempPath, newPassword);
                    else
                        await conn.ExecuteAsync("ATTACH DATABASE ? AS temporary KEY ''", tempPath);

                    await conn.ExecuteScalarAsync<string>("SELECT sqlcipher_export('temporary')");
                }
                catch
                {
                    File.Delete(tempPath);
                    File.Delete(backupPath);
                    throw;
                }
                finally
                {
                    await conn.ExecuteAsync("DETACH DATABASE temporary");
                }

                try
                {
                    await CloseAsync();
                    DeleteDatabase();
                    File.Move(tempPath, dbPath);
                    await OpenAsync(newPassword);
                }
                catch
                {
                    File.Delete(tempPath);
                    RestoreBackup();
                    await OpenAsync(currentPassword);
                    throw;
                }
                finally
                {
                    File.Delete(backupPath);
                }
            }
            else
            {
                var quoted = "'" + newPassword.Replace("'", "''") + "'";

                try
                {
                    await conn.ExecuteScalarAsync<string>($"PRAGMA rekey = {quoted}");
                    await CloseAsync();
                    await OpenAsync(newPassword);
                }
                catch
                {
                    RestoreBackup();
                    await OpenAsync(currentPassword);
                    throw;
                }
                finally
                {
                    File.Delete(backupPath);
                }
            }
        }
    }
}
