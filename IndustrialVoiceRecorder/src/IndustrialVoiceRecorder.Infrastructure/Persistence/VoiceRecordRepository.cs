using Dapper;
using IndustrialVoiceRecorder.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IndustrialVoiceRecorder.Infrastructure.Persistence;

/// <summary>
/// 语音记录仓储实现 (v2.0)
/// 新增：操作员管理、复核修改记录、数字转换字段、应用设置持久化
/// </summary>
public class VoiceRecordRepository : IVoiceRecordRepository
{
    private readonly string _connectionString;
    private readonly ILogger<VoiceRecordRepository> _logger;

    public VoiceRecordRepository(string connectionString, ILogger<VoiceRecordRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
        InitializeDatabase();
    }

    /// <summary>
    /// 初始化数据库（建表和升级）
    /// </summary>
    private void InitializeDatabase()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // 主记录表（v3.0增加SerialNumber）
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS VoiceRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SerialNumber TEXT,
                    RecordTime TEXT NOT NULL,
                    RecognizedText TEXT NOT NULL,
                    NormalizedText TEXT,
                    RawText TEXT,
                    CorrectedText TEXT,
                    CorrectedBy TEXT,
                    CorrectedAt TEXT,
                    IsReviewed INTEGER DEFAULT 0,
                    AudioFilePath TEXT,
                    DeviceName TEXT,
                    Duration REAL,
                    Confidence REAL,
                    Category TEXT,
                    Status TEXT,
                    ProductId TEXT,
                    Operator TEXT,
                    Remarks TEXT,
                    CreatedAt TEXT DEFAULT (datetime('now', 'localtime')),
                    UpdatedAt TEXT DEFAULT (datetime('now', 'localtime'))
                );
                CREATE INDEX IF NOT EXISTS idx_record_time ON VoiceRecords(RecordTime);
                CREATE INDEX IF NOT EXISTS idx_status ON VoiceRecords(Status);
                CREATE INDEX IF NOT EXISTS idx_operator ON VoiceRecords(Operator);
            ");

            // 操作员表
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Operators (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    WorkId TEXT,
                    IsActive INTEGER DEFAULT 1,
                    CreatedAt TEXT DEFAULT (datetime('now', 'localtime'))
                );
            ");

            // 应用设置表（用于记住上次登录的操作员等）
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT,
                    UpdatedAt TEXT DEFAULT (datetime('now', 'localtime'))
                );
            ");

            // 修改历史表（用于稽核）
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS EditHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RecordId INTEGER NOT NULL,
                    FieldName TEXT NOT NULL,
                    OldValue TEXT,
                    NewValue TEXT,
                    EditedBy TEXT,
                    EditedAt TEXT DEFAULT (datetime('now', 'localtime')),
                    FOREIGN KEY(RecordId) REFERENCES VoiceRecords(Id)
                );
            ");

            // v2.0字段升级（如果是旧库，补加新列）
            TryAddColumn(connection, "VoiceRecords", "NormalizedText", "TEXT");
            TryAddColumn(connection, "VoiceRecords", "CorrectedText", "TEXT");
            TryAddColumn(connection, "VoiceRecords", "CorrectedBy", "TEXT");
            TryAddColumn(connection, "VoiceRecords", "CorrectedAt", "TEXT");
            TryAddColumn(connection, "VoiceRecords", "IsReviewed", "INTEGER DEFAULT 0");
            // v3.0字段升级
            TryAddColumn(connection, "VoiceRecords", "SerialNumber", "TEXT");

            // 索引（必须在TryAddColumn之后，否则旧库没有SerialNumber列会报错）
            try { connection.Execute("CREATE INDEX IF NOT EXISTS idx_serial ON VoiceRecords(SerialNumber)"); }
            catch { /* 忽略 */ }

            _logger.LogInformation("数据库初始化完成(v3.0)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据库初始化失败");
            throw;
        }
    }

    /// <summary>
    /// 安全地尝试添加新列（忽略已存在的错误）
    /// </summary>
    private void TryAddColumn(SqliteConnection conn, string table, string column, string type)
    {
        try { conn.Execute($"ALTER TABLE {table} ADD COLUMN {column} {type}"); }
        catch { /* 列已存在则忽略 */ }
    }

    // ==================== 记录CRUD ====================

    public async Task<int> InsertAsync(VoiceRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO VoiceRecords
            (SerialNumber, RecordTime, RecognizedText, NormalizedText, RawText, AudioFilePath, DeviceName, Duration,
             Confidence, Category, Status, ProductId, Operator, Remarks)
            VALUES
            (@SerialNumber, @RecordTime, @RecognizedText, @NormalizedText, @RawText, @AudioFilePath, @DeviceName, @Duration,
             @Confidence, @Category, @Status, @ProductId, @Operator, @Remarks);
            SELECT last_insert_rowid();
        ";

        var id = await connection.ExecuteScalarAsync<int>(sql, record);
        _logger.LogDebug("插入记录成功，ID: {Id}", id);
        return id;
    }

    public async Task<VoiceRecord?> GetByIdAsync(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.QueryFirstOrDefaultAsync<VoiceRecord>(
            "SELECT * FROM VoiceRecords WHERE Id = @Id", new { Id = id });
    }

    public async Task<List<VoiceRecord>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var result = await connection.QueryAsync<VoiceRecord>(@"
            SELECT * FROM VoiceRecords
            WHERE RecordTime BETWEEN @Start AND @End
            ORDER BY RecordTime DESC",
            new { Start = startDate.ToString("yyyy-MM-dd HH:mm:ss"),
                  End = endDate.ToString("yyyy-MM-dd HH:mm:ss") });
        return result.ToList();
    }

    public async Task<List<VoiceRecord>> GetAllAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var result = await connection.QueryAsync<VoiceRecord>(
            "SELECT * FROM VoiceRecords ORDER BY RecordTime DESC");
        return result.ToList();
    }

    public async Task UpdateAsync(VoiceRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(@"
            UPDATE VoiceRecords
            SET RecognizedText=@RecognizedText, NormalizedText=@NormalizedText,
                RawText=@RawText, CorrectedText=@CorrectedText, CorrectedBy=@CorrectedBy,
                CorrectedAt=@CorrectedAt, IsReviewed=@IsReviewed,
                Category=@Category, Status=@Status, ProductId=@ProductId,
                Operator=@Operator, Remarks=@Remarks,
                UpdatedAt=datetime('now','localtime')
            WHERE Id = @Id", record);
    }

    /// <summary>
    /// 复核修改记录（同时记录修改历史）
    /// </summary>
    public async Task ReviewRecordAsync(int recordId, string correctedText, string reviewerName)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // 获取原记录
        var original = await connection.QueryFirstOrDefaultAsync<VoiceRecord>(
            "SELECT * FROM VoiceRecords WHERE Id = @Id", new { Id = recordId });

        if (original == null) return;

        // 写入修改历史
        await connection.ExecuteAsync(@"
            INSERT INTO EditHistory (RecordId, FieldName, OldValue, NewValue, EditedBy)
            VALUES (@RecordId, 'CorrectedText', @Old, @New, @By)",
            new { RecordId = recordId, Old = original.CorrectedText ?? original.RecognizedText,
                  New = correctedText, By = reviewerName });

        // 更新记录
        await connection.ExecuteAsync(@"
            UPDATE VoiceRecords
            SET CorrectedText=@Text, CorrectedBy=@By, CorrectedAt=datetime('now','localtime'),
                IsReviewed=1, UpdatedAt=datetime('now','localtime')
            WHERE Id=@Id",
            new { Text = correctedText, By = reviewerName, Id = recordId });

        _logger.LogInformation("记录已复核: ID={Id}, 修改人={By}", recordId, reviewerName);
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("DELETE FROM VoiceRecords WHERE Id = @Id", new { Id = id });
    }

    public async Task<List<VoiceRecord>> SearchAsync(string keyword)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var result = await connection.QueryAsync<VoiceRecord>(@"
            SELECT * FROM VoiceRecords
            WHERE RecognizedText LIKE @K OR NormalizedText LIKE @K
               OR CorrectedText LIKE @K OR ProductId LIKE @K OR Remarks LIKE @K
            ORDER BY RecordTime DESC", new { K = $"%{keyword}%" });
        return result.ToList();
    }

    public async Task<Statistics> GetStatisticsAsync(DateTime date)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var result = await connection.QueryFirstOrDefaultAsync(@"
            SELECT COUNT(*) as TotalRecords,
                   SUM(CASE WHEN Status='OK' THEN 1 ELSE 0 END) as OkCount,
                   SUM(CASE WHEN Status='NG' THEN 1 ELSE 0 END) as NgCount
            FROM VoiceRecords WHERE date(RecordTime) = date(@Date)",
            new { Date = date.ToString("yyyy-MM-dd") });

        int total = result?.TotalRecords ?? 0;
        int ok = result?.OkCount ?? 0;
        int ng = result?.NgCount ?? 0;
        return new Statistics
        {
            StatDate = date, TotalRecords = total, OkCount = ok, NgCount = ng,
            PassRate = total > 0 ? Math.Round((double)ok / total * 100, 2) : 0
        };
    }

    // ==================== 操作员管理 ====================

    /// <summary>
    /// 获取所有启用的操作员
    /// </summary>
    public async Task<List<OperatorInfo>> GetOperatorsAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var result = await connection.QueryAsync<OperatorInfo>(
            "SELECT * FROM Operators WHERE IsActive=1 ORDER BY Name");
        return result.ToList();
    }

    /// <summary>
    /// 添加操作员
    /// </summary>
    public async Task<int> AddOperatorAsync(OperatorInfo op)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(@"
            INSERT INTO Operators (Name, WorkId, IsActive)
            VALUES (@Name, @WorkId, @IsActive);
            SELECT last_insert_rowid();", op);
    }

    /// <summary>
    /// 删除操作员（软删除）
    /// </summary>
    public async Task RemoveOperatorAsync(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE Operators SET IsActive=0 WHERE Id=@Id", new { Id = id });
    }

    // ==================== 应用设置 ====================

    /// <summary>
    /// 获取设置值
    /// </summary>
    public async Task<string?> GetSettingAsync(string key)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<string>(
            "SELECT Value FROM AppSettings WHERE Key=@Key", new { Key = key });
    }

    /// <summary>
    /// 保存设置值
    /// </summary>
    public async Task SaveSettingAsync(string key, string value)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"
            INSERT INTO AppSettings (Key, Value, UpdatedAt)
            VALUES (@Key, @Value, datetime('now','localtime'))
            ON CONFLICT(Key) DO UPDATE SET Value=@Value, UpdatedAt=datetime('now','localtime')",
            new { Key = key, Value = value });
    }
}

/// <summary>
/// 语音记录仓储接口 (v2.0)
/// </summary>
public interface IVoiceRecordRepository
{
    Task<int> InsertAsync(VoiceRecord record);
    Task<VoiceRecord?> GetByIdAsync(int id);
    Task<List<VoiceRecord>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<List<VoiceRecord>> GetAllAsync();
    Task UpdateAsync(VoiceRecord record);
    Task ReviewRecordAsync(int recordId, string correctedText, string reviewerName);
    Task DeleteAsync(int id);
    Task<List<VoiceRecord>> SearchAsync(string keyword);
    Task<Statistics> GetStatisticsAsync(DateTime date);
    Task<List<OperatorInfo>> GetOperatorsAsync();
    Task<int> AddOperatorAsync(OperatorInfo op);
    Task RemoveOperatorAsync(int id);
    Task<string?> GetSettingAsync(string key);
    Task SaveSettingAsync(string key, string value);
}
