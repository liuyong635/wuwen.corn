using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Traceability
{
    /// <summary>
    /// 料盘追溯服务接口
    /// </summary>
    public interface ITraceService : IDisposable
    {
        #region 核心方法

        /// <summary>
        /// 创建配对记录
        /// </summary>
        /// <param name="smallBarcode">小料盘条码</param>
        /// <param name="largeBarcode">大料盘条码</param>
        /// <param name="smallScanTime">小料盘扫码时间</param>
        /// <param name="largeScanTime">大料盘扫码时间</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>追溯号</returns>
        Task<string> CreatePairingAsync(
            string smallBarcode,
            string largeBarcode,
            DateTime smallScanTime,
            DateTime largeScanTime,
            CancellationToken ct = default);

        #endregion

        #region 查询方法

        /// <summary>
        /// 按条码或追溯号查询
        /// </summary>
        /// <param name="keyword">条码或追溯号</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>匹配的配对记录列表</returns>
        Task<List<TrayPairing>> QueryAsync(string keyword, CancellationToken ct = default);

        /// <summary>
        /// 按追溯号精确查询
        /// </summary>
        /// <param name="traceId">追溯号</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>配对记录，不存在返回null</returns>
        Task<TrayPairing> GetByTraceIdAsync(string traceId, CancellationToken ct = default);

        /// <summary>
        /// 按时间范围查询
        /// </summary>
        /// <param name="start">开始时间</param>
        /// <param name="end">结束时间</param>
        /// <param name="onlyDuplicates">是否只返回重复记录</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>配对记录列表</returns>
        Task<List<TrayPairing>> QueryByTimeRangeAsync(
            DateTime start,
            DateTime end,
            bool onlyDuplicates = false,
            CancellationToken ct = default);

        #endregion

        #region 统计方法

        /// <summary>
        /// 获取今日统计
        /// </summary>
        Task<TraceStatistics> GetTodayStatisticsAsync(CancellationToken ct = default);

        /// <summary>
        /// 获取指定日期统计
        /// </summary>
        Task<TraceStatistics> GetStatisticsAsync(DateTime date, CancellationToken ct = default);

        /// <summary>
        /// 获取重复条码列表
        /// </summary>
        /// <param name="date">日期，null表示今天</param>
        /// <param name="ct">取消令牌</param>
        Task<List<DuplicateInfo>> GetDuplicatesAsync(DateTime? date = null, CancellationToken ct = default);

        #endregion

        #region 维护方法

        /// <summary>
        /// 清理过期数据
        /// </summary>
        /// <param name="retentionDays">保留天数</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>删除的记录数</returns>
        Task<int> CleanupOldDataAsync(int retentionDays = 90, CancellationToken ct = default);

        #endregion
    }
}
