using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SmartMES.Modules.Database
{
    // ============================================================
    // 浠撳偍鎺ュ彛锛圧epository Pattern锛?
    // 灏佽CRUD锛屼笂灞俈iewModel涓嶇洿鎺ユ搷浣淒bContext
    // ============================================================

    public interface IRepository<T> where T : class
    {
        Task<List<T>> GetAllAsync();
        Task<T?> GetByIdAsync(int id);
        Task<int> AddAsync(T entity);
        Task UpdateAsync(T entity);
        Task DeleteAsync(int id);
    }

    // ============================================================
    // EF Core 浠撳偍瀹炵幇锛堥€氱敤娉涘瀷锛?
    // ============================================================
    public class EfRepository<T> : IRepository<T> where T : class
    {
        protected readonly SmartMesDbContext _ctx;
        /// <summary>
        /// 自动补齐：EfRepository 方法说明。
        /// </summary>
        public EfRepository(SmartMesDbContext ctx) { _ctx = ctx; }

        /// <summary>
        /// 自动补齐：GetAllAsync 方法说明。
        /// </summary>
        public async Task<List<T>> GetAllAsync()
            => await _ctx.Set<T>().AsNoTracking().ToListAsync();

        /// <summary>
        /// 自动补齐：GetByIdAsync 方法说明。
        /// </summary>
        public async Task<T?> GetByIdAsync(int id)
            => await _ctx.Set<T>().FindAsync(id);

        /// <summary>
        /// 自动补齐：AddAsync 方法说明。
        /// </summary>
        public async Task<int> AddAsync(T entity)
        {
            await _ctx.Set<T>().AddAsync(entity);
            await _ctx.SaveChangesAsync();
            // 灏濊瘯鍙嶅皠鑾峰彇Id
            var prop = entity.GetType().GetProperty("Id");
            return prop != null ? (int)(prop.GetValue(entity) ?? 0) : 0;
        }

        /// <summary>
        /// 自动补齐：UpdateAsync 方法说明。
        /// </summary>
        public async Task UpdateAsync(T entity)
        {
            _ctx.Set<T>().Update(entity);
            await _ctx.SaveChangesAsync();
        }

        /// <summary>
        /// 自动补齐：DeleteAsync 方法说明。
        /// </summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await GetByIdAsync(id);
            if (entity != null)
            {
                _ctx.Set<T>().Remove(entity);
                await _ctx.SaveChangesAsync();
            }
        }
    }

    // ============================================================
    // Dapper 浠撳偍瀹炵幇锛堝師鐢烻QL锛岄€傚悎澶嶆潅鏌ヨ锛?
    // 浼樺娍锛氭€ц兘鏇撮珮锛孲QL瀹屽叏鍙帶锛岄€傚悎鎶ヨ〃/缁熻鏌ヨ
    // ============================================================
    public class DapperProductionRepository
    {
        private readonly string _connStr;

        /// <summary>
        /// 自动补齐：DapperProductionRepository 方法说明。
        /// </summary>
        public DapperProductionRepository(string sqliteConnStr)
        {
            _connStr = sqliteConnStr;
        }

        /// <summary>Dapper鍘熺敓SQL鏌ヨ锛堟瘮EF Core鎬ц兘鏇撮珮锛?/summary>
        public async Task<IEnumerable<ProductionRecord>> QueryByOrderAsync(string orderId)
        {
            using var conn = new SqliteConnection(_connStr);
            // Dapper鐩存帴鎵цSQL锛屽弬鏁板寲闃叉敞鍏?
            return await conn.QueryAsync<ProductionRecord>(
                "SELECT * FROM ProductionRecords WHERE OrderId=@OrderId ORDER BY RecordTime DESC",
                new { OrderId = orderId });
        }

        /// <summary>缁熻姣忎釜宸ュ崟鐨勮壇鍝佺巼锛圖apper閫傚悎姝ょ被鑱氬悎鏌ヨ锛?/summary>
        public async Task<IEnumerable<dynamic>> GetPassRateByOrderAsync()
        {
            using var conn = new SqliteConnection(_connStr);
            return await conn.QueryAsync(
                @"SELECT OrderId,
                         COUNT(*) AS Total,
                         SUM(CASE WHEN IsPass=1 THEN 1 ELSE 0 END) AS PassCount,
                         ROUND(100.0*SUM(CASE WHEN IsPass=1 THEN 1 ELSE 0 END)/COUNT(*),2) AS PassRate
                  FROM ProductionRecords
                  GROUP BY OrderId
                  ORDER BY PassRate DESC");
        }

        /// <summary>鎵归噺鎻掑叆锛堜娇鐢ㄤ簨鍔′繚璇佸師瀛愭€э級</summary>
        public async Task BulkInsertAsync(IEnumerable<ProductionRecord> records)
        {
            using var conn = new SqliteConnection(_connStr);
            await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync();
            try
            {
                await conn.ExecuteAsync(
                    @"INSERT INTO ProductionRecords
                      (OrderId,ProductCode,Qty,IsPass,Temperature,Pressure,RecordTime,Operator)
                      VALUES
                      (@OrderId,@ProductCode,@Qty,@IsPass,@Temperature,@Pressure,@RecordTime,@Operator)",
                    records, transaction: tx);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }

    // ============================================================
    // 绠€鏄揜edis KV瀛樺偍锛堥潪鍏崇郴鍨嬫暟鎹簱妯℃嫙锛?
    // 鐢熶骇鐜鏇挎崲涓?StackExchange.Redis 搴?
    // ============================================================
    public class InMemoryKvStore
    {
        private readonly Dictionary<string, (object Value, DateTime? Expiry)> _store = new();
        private readonly object _lock = new();

        /// <summary>璁剧疆閿€硷紙鍙缃繃鏈熸椂闂达級</summary>
        public void Set(string key, object value, TimeSpan? ttl = null)
        {
            lock (_lock)
            {
                _store[key] = (value, ttl.HasValue ? DateTime.Now + ttl.Value : null);
            }
        }

        /// <summary>鑾峰彇鍊?/summary>
        public T? Get<T>(string key)
        {
            lock (_lock)
            {
                if (!_store.TryGetValue(key, out var entry)) return default;
                if (entry.Expiry.HasValue && DateTime.Now > entry.Expiry.Value)
                {
                    _store.Remove(key);
                    return default;
                }
                return entry.Value is T t ? t : default;
            }
        }

        /// <summary>
        /// 自动补齐：Delete 方法说明。
        /// </summary>
        public bool Delete(string key) { lock (_lock) { return _store.Remove(key); } }
        /// <summary>
        /// 自动补齐：Keys 方法说明。
        /// </summary>
        public IEnumerable<string> Keys() { lock (_lock) { return _store.Keys.ToList(); } }
        /// <summary>
        /// 自动补齐：Count 方法说明。
        /// </summary>
        public int Count() { lock (_lock) { return _store.Count; } }
    }
}
