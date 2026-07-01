using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Queues
{
    /// <summary>
    /// 数据管道实现
    /// </summary>
    public class DataPipeline<T> : IDataPipeline<T>
    {
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

        public string PipelineId { get; }
        public int Count => _queue.Count;
        public bool IsEmpty => _queue.IsEmpty;

        public event EventHandler<T> DataAvailable;

        public DataPipeline(string pipelineId)
        {
            PipelineId = pipelineId;
        }

        public void Push(T data)
        {
            _queue.Enqueue(data);
            _signal.Release();
            DataAvailable?.Invoke(this, data);
        }

        public bool TryPop(out T data) => _queue.TryDequeue(out data);

        public bool TryPeek(out T data) => _queue.TryPeek(out data);

        public async Task<T> WaitForDataAsync(CancellationToken ct = default)
        {
            while (true)
            {
                await _signal.WaitAsync(ct);
                if (_queue.TryDequeue(out var item))
                    return item;
            }
        }

        // 注意：返回 ValueTuple<bool, T> 而不是命名元组，以匹配接口签名
        public async Task<ValueTuple<bool, T>> WaitForDataAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(timeout);
                try
                {
                    var data = await WaitForDataAsync(cts.Token);
                    return new ValueTuple<bool, T>(true, data);
                }
                catch (OperationCanceledException)
                {
                    return new ValueTuple<bool, T>(false, default);
                }
            }
        }

        public void Clear()
        {
            while (_queue.TryDequeue(out _)) { }
            while (_signal.CurrentCount > 0) _signal.Wait(0);
        }
    }

    /// <summary>
    /// 数据流管理器实现
    /// </summary>
    public class DataFlowManager : IDataFlowManager
    {
        private readonly ConcurrentDictionary<string, object> _pipelines = new ConcurrentDictionary<string, object>();
        private bool _disposed;

        public IDataPipeline<T> GetOrCreate<T>(string pipelineId)
            => (IDataPipeline<T>)_pipelines.GetOrAdd(pipelineId, _ => new DataPipeline<T>(pipelineId));

        public IDataPipeline<T> Get<T>(string pipelineId)
            => _pipelines.TryGetValue(pipelineId, out var p) && p is IDataPipeline<T> typed ? typed : null;

        public bool Has(string pipelineId) => _pipelines.ContainsKey(pipelineId);

        public void Remove(string pipelineId) => _pipelines.TryRemove(pipelineId, out _);

        public void ClearAll()
        {
            foreach (var p in _pipelines.Values)
            {
                var clear = p.GetType().GetMethod("Clear");
                clear?.Invoke(p, null);
            }
        }

        public IEnumerable<string> GetAllIds() => _pipelines.Keys;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearAll();
            _pipelines.Clear();
        }
    }
}