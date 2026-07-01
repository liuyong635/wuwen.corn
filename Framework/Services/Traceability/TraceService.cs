using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Data.SQLite;
using Dapper;
using SeedCut.Framework.Services.Interfaces;

namespace SeedCut.Framework.Services.Traceability
{
    /// <summary>
    /// 料盘追溯服务 - SQLite实现
    /// </summary>
    public class TraceService : ITraceService
    {
        #region 私有字段

        private readonly string _connectionString;
        private readonly string _dbPath;
        private readonly ILogService _logService;
        private readonly object _seqLock = new object();

        private int _todaySequence = 0;
        private DateTime _sequenceDate = DateTime.MinValue;
        private bool _disposed;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数 - 自动初始化数据库
        /// </summary>
        /// <param name="logService">日志服务（可选）</param>
        /// <param name="dbDirectory">数据库目录（可选，默认为程序目录/Data）</param>
        public TraceService(ILogService logService = null, string dbDirectory = null)
        {
            _logService = logService;

            // 确定数据库路径
            if (string.IsNullOrEmpty(dbDirectory))
            {
                dbDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            }

            _dbPath = Path.Combine(dbDirectory, "trace.db");
            _connectionString = $"Data Source={_dbPath};Version=3;";

            // 初始化数据库
            InitializeDatabase();

            _logService?.Information("[TraceService] 追溯服务已初始化，数据库路径: {0}", _dbPath);
        }

        #endregion

        #region 数据库初始化

        /// <summary>
        /// 初始化数据库（创建表和索引）
        /// </summary>
        private void InitializeDatabase()
        {
            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();

                    // 创建表（IF NOT EXISTS 保证幂等）
                    connection.Execute(@"
                        CREATE TABLE IF NOT EXISTS TrayPairings (
                            TraceId           TEXT PRIMARY KEY,
                            SmallTrayBarcode  TEXT NOT NULL,
                            LargeTrayBarcode  TEXT NOT NULL,
                            SmallTrayScanTime TEXT NOT NULL,
                            LargeTrayScanTime TEXT NOT NULL,
                            PairTime          TEXT NOT NULL,
                            IsSmallDuplicate  INTEGER DEFAULT 0,
                            IsLargeDuplicate  INTEGER DEFAULT 0,
                            BatchNo           TEXT,
                            WorkOrderNo       TEXT,
                            OperatorId        TEXT,
                            StationNo         TEXT,
                            Remarks           TEXT,
                            CreatedAt         TEXT DEFAULT (datetime('now', 'localtime'))
                        );
                    ");

                    // 创建索引
                    connection.Execute(@"
                        CREATE INDEX IF NOT EXISTS IX_SmallBarcode ON TrayPairings(SmallTrayBarcode);
                        CREATE INDEX IF NOT EXISTS IX_LargeBarcode ON TrayPairings(LargeTrayBarcode);
                        CREATE INDEX IF NOT EXISTS IX_PairTime ON TrayPairings(PairTime);
                        CREATE INDEX IF NOT EXISTS IX_CreatedAt ON TrayPairings(CreatedAt);
                    ");
                }

                _logService?.Debug("[TraceService] 数据库初始化完成");
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[TraceService] 数据库初始化失败");
                throw;
            }
        }

        #endregion

        #region 核心方法

        /// <summary>
        /// 创建配对记录
        /// </summary>
        public async Task<string> CreatePairingAsync(
            string smallBarcode,
            string largeBarcode,
            DateTime smallScanTime,
            DateTime largeScanTime,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(smallBarcode))
                throw new ArgumentNullException(nameof(smallBarcode));
            if (string.IsNullOrWhiteSpace(largeBarcode))
                throw new ArgumentNullException(nameof(largeBarcode));

            // 1. 生成追溯号
            string traceId = GenerateTraceId();

            // 2. 检查重复
            bool isSmallDup = await CheckDuplicateAsync(smallBarcode, "Small", ct);
            bool isLargeDup = await CheckDuplicateAsync(largeBarcode, "Large", ct);

            // 3. 创建记录
            var pairing = new TrayPairing
            {
                TraceId = traceId,
                SmallTrayBarcode = smallBarcode,
                LargeTrayBarcode = largeBarcode,
                SmallTrayScanTime = smallScanTime,
                LargeTrayScanTime = largeScanTime,
                PairTime = DateTime.Now,
                IsSmallDuplicate = isSmallDup,
                IsLargeDuplicate = isLargeDup,
                CreatedAt = DateTime.Now
            };

            // 4. 插入数据库
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct);

                await connection.ExecuteAsync(@"
                    INSERT INTO TrayPairings (
                        TraceId, SmallTrayBarcode, LargeTrayBarcode,
                        SmallTrayScanTime, LargeTrayScanTime, PairTime,
                        IsSmallDuplicate, IsLargeDuplicate, CreatedAt
                    ) VALUES (
                        @TraceId, @SmallTrayBarcode, @LargeTrayBarcode,
                        @SmallTrayScanTime, @LargeTrayScanTime, @PairTime,
                        @IsSmallDuplicate, @IsLargeDuplicate, @CreatedAt
                    )", pairing);
            }

            // 5. 记录日志
            if (isSmallDup || isLargeDup)
            {
                _logService?.Warning("[TraceService] 检测到重复条码 - 追溯号:{0}, 小料盘:{1}({2}), 大料盘:{3}({4})",
                    traceId,
                    smallBarcode, isSmallDup ? "重复" : "正常",
                    largeBarcode, isLargeDup ? "重复" : "正常");
            }
            else
            {
                _logService?.Debug("[TraceService] 配对记录已创建: {0}", traceId);
            }

            return traceId;
        }

        /// <summary>
        /// 检查条码是否重复（当天是否已出现）
        /// </summary>
        private async Task<bool> CheckDuplicateAsync(string barcode, string trayType, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct);

                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);

                string sql = trayType == "Small"
                    ? "SELECT COUNT(1) FROM TrayPairings WHERE SmallTrayBarcode = @Barcode AND PairTime >= @Today AND PairTime < @Tomorrow"
                    : "SELECT COUNT(1) FROM TrayPairings WHERE LargeTrayBarcode = @Barcode AND PairTime >= @Today AND PairTime < @Tomorrow";

                int count = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    Barcode = barcode,
                    Today = today,
                    Tomorrow = tomorrow
                });

                return count > 0;
            }
        }

        /// <summary>
        /// 生成追溯号
        /// 格式：TR-YYYYMMDD-NNNNNN
        /// </summary>
        private string GenerateTraceId()
        {
            lock (_seqLock)
            {
                var today = DateTime.Today;

                // 日期变更，重置序号
                if (_sequenceDate != today)
                {
                    _sequenceDate = today;
                    _todaySequence = GetMaxSequenceFromDb(today);
                }

                _todaySequence++;

                return string.Format("TR-{0:yyyyMMdd}-{1:D6}", today, _todaySequence);
            }
        }

        /// <summary>
        /// 从数据库获取当天最大序号（程序重启时使用）
        /// </summary>
        private int GetMaxSequenceFromDb(DateTime date)
        {
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();

                    string prefix = string.Format("TR-{0:yyyyMMdd}-", date);

                    var maxTraceId = connection.ExecuteScalar<string>(
                        "SELECT MAX(TraceId) FROM TrayPairings WHERE TraceId LIKE @Prefix",
                        new { Prefix = prefix + "%" });

                    if (!string.IsNullOrEmpty(maxTraceId) && maxTraceId.Length >= 18)
                    {
                        // 提取序号部分
                        string seqStr = maxTraceId.Substring(12); // TR-YYYYMMDD- 后面的部分
                        if (int.TryParse(seqStr, out int seq))
                        {
                            return seq;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logService?.Warning("[TraceService] 获取最大序号失败: {0}", ex.Message);
            }

            return 0;
        }

        #endregion

        #region 查询方法

        /// <summary>
        /// 按条码或追溯号查询
        /// </summary>
        public async Task<List<TrayPairing>> QueryAsync(string keyword, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new List<TrayPairing>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct);

                var results = await connection.QueryAsync<TrayPairing>(@"
                    SELECT * FROM TrayPairings 
                    WHERE TraceId = @Keyword 
                       OR SmallTrayBarcode = @Keyword 
                       OR LargeTrayBarcode = @Keyword
                    ORDER BY PairTime DESC
                    LIMIT 100", new { Keyword = keyword });

                return results.AsList();
            }
        }

        /// <summary>
        /// 按追溯号精确查询
        /// </summary>
        public async Task<TrayPairing> GetByTraceIdAsync(string traceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(traceId))
                return null;

            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct);

                return await connection.QueryFirstOrDefaultAsync<TrayPairing>(
                    "SELECT * FROM TrayPairings WHERE TraceId = @TraceId",
                    new { TraceId = traceId });
            }
        }

        /// <summary>
        /// 按时间范围查询
        /// </summary>
        public async Task<List<TrayPairing>> QueryByTimeRangeAsync(
            DateTime start,
            DateTime end,
            bool onlyDuplicates = false,
            CancellationToken ct = default)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct);

                string sql = @"
                    SELECT * FROM TrayPairings 
                    WHERE PairTime >= @Start AND PairTime < @End";

                if (onlyDuplicates)
                {
                    sql += " AND (IsSmallDuplicate = 1 OR IsLargeDuplicate = 1)";
                }

                sql += " ORDER BY PairTime DESC LIMIT 1000";

                var results = await connection.QueryAsync<TrayPairing>(sql, new
                {
                    Start = start,
                    End = end
                });

                return results.AsList();
            }
        }

        #endregion

        #region 统计方法

        /// <summary>
        /// 获取今日统计
        /// </summary>
        public Task<TraceStatistics> GetTodayStatisticsAsync(CancellationToken ct = default)
        {
            return GetStatisticsAsync(DateTime.Today, ct);
        }

        /// <summary>
        /// 获取指定日期统计
        /// </summary>
        public async Task<TraceStatistics> GetStatisticsAsync(DateTime date, CancellationToken ct = default)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct);

                var today = date.Date;
                var tomorrow = today.AddDays(1);

                var stats = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT 
                        COUNT(1) as TotalPairings,
                        SUM(CASE WHEN IsSmallDuplicate = 1 OR IsLargeDuplicate = 1 THEN 1 ELSE 0 END) as DuplicateCount
                    FROM TrayPairings 
                    WHERE PairTime >= @Today AND PairTime < @Tomorrow",
                    new { Today = today, Tomorrow = tomorrow });

                return new TraceStatistics
                {
                    TotalPairings = (int)(stats?.TotalPairings ?? 0),
                    DuplicateCount = (int)(stats?.DuplicateCount ?? 0),
                    StatDate = date
                };
            }
        }

        /// <summary>
        /// 获取重复条码列表
        /// </summary>
        public async Task<List<DuplicateInfo>> GetDuplicatesAsync(DateTime? date = null, CancellationToken ct = default)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct);

                var targetDate = date?.Date ?? DateTime.Today;
                var nextDay = targetDate.AddDays(1);

                // 查询小料盘重复
                var smallDuplicates = await connection.QueryAsync<DuplicateInfo>(@"
                    SELECT 
                        SmallTrayBarcode as Barcode,
                        'Small' as TrayType,
                        COUNT(1) as OccurrenceCount,
                        MIN(PairTime) as FirstTime,
                        MAX(PairTime) as LastTime
                    FROM TrayPairings 
                    WHERE PairTime >= @Today AND PairTime < @Tomorrow
                    GROUP BY SmallTrayBarcode
                    HAVING COUNT(1) > 1",
                    new { Today = targetDate, Tomorrow = nextDay });

                // 查询大料盘重复
                var largeDuplicates = await connection.QueryAsync<DuplicateInfo>(@"
                    SELECT 
                        LargeTrayBarcode as Barcode,
                        'Large' as TrayType,
                        COUNT(1) as OccurrenceCount,
                        MIN(PairTime) as FirstTime,
                        MAX(PairTime) as LastTime
                    FROM TrayPairings 
                    WHERE PairTime >= @Today AND PairTime < @Tomorrow
                    GROUP BY LargeTrayBarcode
                    HAVING COUNT(1) > 1",
                    new { Today = targetDate, Tomorrow = nextDay });

                var result = new List<DuplicateInfo>();
                result.AddRange(smallDuplicates);
                result.AddRange(largeDuplicates);

                return result;
            }
        }

        #endregion

        #region 维护方法

        /// <summary>
        /// 清理过期数据
        /// </summary>
        public async Task<int> CleanupOldDataAsync(int retentionDays = 90, CancellationToken ct = default)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct);

                var cutoffDate = DateTime.Today.AddDays(-retentionDays);

                int deleted = await connection.ExecuteAsync(
                    "DELETE FROM TrayPairings WHERE CreatedAt < @CutoffDate",
                    new { CutoffDate = cutoffDate });

                if (deleted > 0)
                {
                    _logService?.Information("[TraceService] 已清理 {0} 条过期数据（保留 {1} 天）",
                        deleted, retentionDays);

                    // 清理后执行 VACUUM 优化数据库大小
                    await connection.ExecuteAsync("VACUUM");
                }

                return deleted;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _logService?.Debug("[TraceService] 追溯服务已释放");
            }
        }

        #endregion
    }
}