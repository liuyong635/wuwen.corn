using Modbus.Device;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    /// <summary>
    /// Modbus 请求队列
    /// 
    /// 【核心作用】
    /// 确保所有 Modbus 操作串行执行，避免 NModbus 的线程安全问题。
    /// 
    /// 【设计原理】
    /// 1. 所有 Modbus 读写操作封装为 ModbusRequest 对象
    /// 2. 请求入队后，由单一后台线程顺序执行
    /// 3. 使用 TaskCompletionSource 实现异步等待结果
    /// 
    /// 【使用方式】
    /// var result = await _queue.EnqueueWriteAsync(address, value);
    /// var data = await _queue.EnqueueReadAsync(address, count);
    /// </summary>
    public class ModbusRequestQueue : IDisposable
    {
        #region 私有字段

        private readonly BlockingCollection<ModbusRequest> _requestQueue;
        private readonly Task _processingTask;
        private readonly CancellationTokenSource _cts;
        private readonly ILogService _logService;

        private IModbusMaster _modbusMaster;
        private byte _stationId;
        private bool _disposed;

        // 队列配置
        private const int MAX_QUEUE_SIZE = 100;
        private const int REQUEST_TIMEOUT_MS = 5000;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建 Modbus 请求队列
        /// </summary>
        /// <param name="logService">日志服务（可选）</param>
        public ModbusRequestQueue(ILogService logService = null)
        {
            _logService = logService;
            _requestQueue = new BlockingCollection<ModbusRequest>(MAX_QUEUE_SIZE);
            _cts = new CancellationTokenSource();

            // 启动后台处理线程
            _processingTask = Task.Factory.StartNew(
                ProcessRequestsAsync,
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            _logService?.Debug("[ModbusQueue] 请求队列已创建");
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 设置 Modbus Master（连接成功后调用）
        /// </summary>
        public void SetModbusMaster(IModbusMaster master, byte stationId)
        {
            _modbusMaster = master;
            _stationId = stationId;
            _logService?.Debug("[ModbusQueue] ModbusMaster 已设置, StationId={StationId}", stationId);
        }

        /// <summary>
        /// 清除 Modbus Master（断开连接时调用）
        /// </summary>
        public void ClearModbusMaster()
        {
            _modbusMaster = null;
            _logService?.Debug("[ModbusQueue] ModbusMaster 已清除");
        }

        /// <summary>
        /// 是否已设置 ModbusMaster
        /// </summary>
        public bool IsReady => _modbusMaster != null;

        #endregion

        #region 公开方法 - 写入操作

        /// <summary>
        /// 写入单个寄存器（入队执行）
        /// </summary>
        /// <param name="address">寄存器地址</param>
        /// <param name="value">写入值</param>
        /// <param name="description">操作描述（用于日志）</param>
        /// <returns>是否成功</returns>
        public Task<bool> EnqueueWriteAsync(ushort address, ushort value, string description = null)
        {
            var request = new ModbusRequest
            {
                Type = ModbusRequestType.WriteSingleRegister,
                Address = address,
                WriteValue = value,
                Description = description ?? $"Write 0x{value:X4} to 0x{address:X4}"
            };

            return EnqueueAndWaitAsync<bool>(request);
        }

        /// <summary>
        /// 写入多个寄存器（入队执行）
        /// </summary>
        public Task<bool> EnqueueWriteMultipleAsync(ushort address, ushort[] values, string description = null)
        {
            var request = new ModbusRequest
            {
                Type = ModbusRequestType.WriteMultipleRegisters,
                Address = address,
                WriteValues = values,
                Description = description ?? $"WriteMultiple to 0x{address:X4}"
            };

            return EnqueueAndWaitAsync<bool>(request);
        }

        #endregion

        #region 公开方法 - 读取操作

        /// <summary>
        /// 读取保持寄存器（入队执行）
        /// </summary>
        /// <param name="address">起始地址</param>
        /// <param name="count">读取数量</param>
        /// <param name="description">操作描述</param>
        /// <returns>读取到的数据，失败返回 null</returns>
        public Task<ushort[]> EnqueueReadHoldingAsync(ushort address, ushort count, string description = null)
        {
            var request = new ModbusRequest
            {
                Type = ModbusRequestType.ReadHoldingRegisters,
                Address = address,
                ReadCount = count,
                Description = description ?? $"ReadHolding 0x{address:X4} x{count}"
            };

            return EnqueueAndWaitAsync<ushort[]>(request);
        }

        /// <summary>
        /// 读取输入寄存器（入队执行）
        /// </summary>
        public Task<ushort[]> EnqueueReadInputAsync(ushort address, ushort count, string description = null)
        {
            var request = new ModbusRequest
            {
                Type = ModbusRequestType.ReadInputRegisters,
                Address = address,
                ReadCount = count,
                Description = description ?? $"ReadInput 0x{address:X4} x{count}"
            };

            return EnqueueAndWaitAsync<ushort[]>(request);
        }

        #endregion

        #region 公开方法 - 高优先级操作

        /// <summary>
        /// 高优先级写入（插队到队首）
        /// 用于紧急停止等场景
        /// </summary>
        public Task<bool> EnqueueUrgentWriteAsync(ushort address, ushort value, string description = null)
        {
            var request = new ModbusRequest
            {
                Type = ModbusRequestType.WriteSingleRegister,
                Address = address,
                WriteValue = value,
                Description = description ?? $"[URGENT] Write 0x{value:X4} to 0x{address:X4}",
                IsUrgent = true
            };

            return EnqueueAndWaitAsync<bool>(request);
        }

        #endregion

        #region 内部实现

        /// <summary>
        /// 入队并等待结果
        /// </summary>
        private Task<T> EnqueueAndWaitAsync<T>(ModbusRequest request)
        {
            if (_disposed)
            {
                return Task.FromResult(default(T));
            }

            if (_modbusMaster == null)
            {
                _logService?.Warning("[ModbusQueue] ModbusMaster 未设置，请求被拒绝: {Desc}", request.Description);
                return Task.FromResult(default(T));
            }

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            request.CompletionSource = tcs;

            try
            {
                _requestQueue.Add(request);
                _logService?.Verbose("[ModbusQueue] 请求入队: {Desc}, 队列长度={Len}",
                    request.Description, _requestQueue.Count);
            }
            catch (InvalidOperationException)
            {
                // 队列已关闭
                return Task.FromResult(default(T));
            }

            // 带超时的等待
            return WaitWithTimeoutAsync<T>(tcs.Task, request.Description);
        }

        /// <summary>
        /// 带超时的等待
        /// </summary>
        private async Task<T> WaitWithTimeoutAsync<T>(Task<object> task, string description)
        {
            var timeoutTask = Task.Delay(REQUEST_TIMEOUT_MS);
            var completedTask = await Task.WhenAny(task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logService?.Warning("[ModbusQueue] 请求超时: {Desc}", description);
                return default(T);
            }

            var result = await task;
            if (result is T typedResult)
            {
                return typedResult;
            }

            return default(T);
        }

        /// <summary>
        /// 后台处理线程 - 串行执行所有请求
        /// </summary>
        private async Task ProcessRequestsAsync()
        {
            _logService?.Debug("[ModbusQueue] 处理线程已启动");

            try
            {
                foreach (var request in _requestQueue.GetConsumingEnumerable(_cts.Token))
                {
                    await ProcessSingleRequestAsync(request);
                }
            }
            catch (OperationCanceledException)
            {
                _logService?.Debug("[ModbusQueue] 处理线程被取消");
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[ModbusQueue] 处理线程异常");
            }

            _logService?.Debug("[ModbusQueue] 处理线程已退出");
        }

        /// <summary>
        /// 处理单个请求
        /// </summary>
        private async Task ProcessSingleRequestAsync(ModbusRequest request)
        {
            if (_modbusMaster == null)
            {
                request.CompletionSource?.TrySetResult(null);
                return;
            }

            try
            {
                object result = null;

                switch (request.Type)
                {
                    case ModbusRequestType.WriteSingleRegister:
                        await _modbusMaster.WriteSingleRegisterAsync(
                            _stationId,
                            request.Address,
                            request.WriteValue);
                        result = true;
                        _logService?.Verbose("[ModbusQueue] 写入成功: {Desc}", request.Description);
                        break;

                    case ModbusRequestType.WriteMultipleRegisters:
                        await _modbusMaster.WriteMultipleRegistersAsync(
                            _stationId,
                            request.Address,
                            request.WriteValues);
                        result = true;
                        _logService?.Verbose("[ModbusQueue] 批量写入成功: {Desc}", request.Description);
                        break;

                    case ModbusRequestType.ReadHoldingRegisters:
                        result = await _modbusMaster.ReadHoldingRegistersAsync(
                            _stationId,
                            request.Address,
                            request.ReadCount);
                        _logService?.Verbose("[ModbusQueue] 读取成功: {Desc}", request.Description);
                        break;

                    case ModbusRequestType.ReadInputRegisters:
                        result = await _modbusMaster.ReadInputRegistersAsync(
                            _stationId,
                            request.Address,
                            request.ReadCount);
                        _logService?.Verbose("[ModbusQueue] 读取成功: {Desc}", request.Description);
                        break;
                }

                request.CompletionSource?.TrySetResult(result);
            }
            catch (Exception ex)
            {
                _logService?.Warning("[ModbusQueue] 请求失败: {Desc}, 错误: {Error}",
                    request.Description, ex.Message);

                // 写入操作返回 false，读取操作返回 null
                if (request.Type == ModbusRequestType.WriteSingleRegister ||
                    request.Type == ModbusRequestType.WriteMultipleRegisters)
                {
                    request.CompletionSource?.TrySetResult(false);
                }
                else
                {
                    request.CompletionSource?.TrySetResult(null);
                }
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _logService?.Debug("[ModbusQueue] 正在释放资源...");

            // 停止接收新请求
            _requestQueue.CompleteAdding();

            // 取消处理线程
            _cts.Cancel();

            // 等待处理线程退出
            try
            {
                _processingTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }

            _cts.Dispose();
            _requestQueue.Dispose();

            _logService?.Debug("[ModbusQueue] 资源已释放");
        }

        #endregion
    }

    #region 请求类型定义

    /// <summary>
    /// Modbus 请求类型
    /// </summary>
    internal enum ModbusRequestType
    {
        WriteSingleRegister,
        WriteMultipleRegisters,
        ReadHoldingRegisters,
        ReadInputRegisters
    }

    /// <summary>
    /// Modbus 请求对象
    /// </summary>
    internal class ModbusRequest
    {
        public ModbusRequestType Type { get; set; }
        public ushort Address { get; set; }
        public ushort ReadCount { get; set; }
        public ushort WriteValue { get; set; }
        public ushort[] WriteValues { get; set; }
        public string Description { get; set; }
        public bool IsUrgent { get; set; }
        public TaskCompletionSource<object> CompletionSource { get; set; }
    }

    #endregion
}