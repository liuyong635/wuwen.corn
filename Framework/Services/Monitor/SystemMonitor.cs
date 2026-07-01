using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SeedCut.Framework.Services.Interfaces;

namespace SeedCut.Framework.Services.Monitor
{
    /// <summary>
    /// 系统监控器实现
    /// 用于应用启动时检查各模块状态
    /// </summary>
    public class SystemMonitor : ISystemMonitor, INotifyPropertyChanged
    {
        #region 内部类

        private class ModuleInfo
        {
            public string Name { get; set; }
            public Func<ModuleCheckResult> CheckFunc { get; set; }
            public bool IsCritical { get; set; }
            public ModuleCheckResult Result { get; set; }
        }

        #endregion

        private readonly List<ModuleInfo> _modules = new List<ModuleInfo>();
        private readonly ILogService _logService;
        private int _totalModules;
        private int _checkedModules;
        private bool _allCriticalPassed = true;

        #region 属性

        public int TotalModules
        {
            get { return _totalModules; }
            private set { SetProperty(ref _totalModules, value); }
        }

        public int CheckedModules
        {
            get { return _checkedModules; }
            private set
            {
                SetProperty(ref _checkedModules, value);
                OnPropertyChanged(nameof(Progress));
            }
        }

        public int Progress
        {
            get { return TotalModules > 0 ? (CheckedModules * 100 / TotalModules) : 0; }
        }

        public bool AllCriticalPassed
        {
            get { return _allCriticalPassed; }
            private set { SetProperty(ref _allCriticalPassed, value); }
        }

        #endregion

        #region 事件

        public event EventHandler<ModuleCheckedEventArgs> ModuleChecked;
        public event EventHandler<bool> CheckCompleted;
        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region 构造函数

        public SystemMonitor(ILogService logService = null)
        {
            _logService = logService;
        }

        #endregion

        #region 公共方法

        public void RegisterModule(string moduleName, Func<ModuleCheckResult> checkFunc, bool isCritical = true)
        {
            var moduleInfo = new ModuleInfo
            {
                Name = moduleName,
                CheckFunc = checkFunc,
                IsCritical = isCritical,
                Result = new ModuleCheckResult(moduleName, isCritical)
            };

            _modules.Add(moduleInfo);
            TotalModules = _modules.Count;

            LogDebug("注册模块: {0} {1}", moduleName, isCritical ? "[关键]" : "[可选]");
        }

        public async Task StartCheckAsync()
        {
            LogInfo("========== 开始系统检查 ==========");
            LogInfo("共需检查 {0} 个模块", TotalModules);

            CheckedModules = 0;
            AllCriticalPassed = true;

            foreach (var moduleInfo in _modules)
            {
                await CheckModuleAsync(moduleInfo);
            }

            LogInfo("========== 系统检查完成 ==========");
            LogInfo("关键模块状态: {0}", AllCriticalPassed ? "全部通过" : "存在失败");

            // 触发完成事件
            RaiseEventOnUIThread(() =>
            {
                var handler = CheckCompleted;
                if (handler != null)
                {
                    handler(this, AllCriticalPassed);
                }
            });
        }

        public ModuleCheckResult GetModuleResult(string moduleName)
        {
            var module = _modules.FirstOrDefault(m => m.Name == moduleName);
            return module != null ? module.Result : new ModuleCheckResult();
        }

        public List<string> GetFailedCriticalModules()
        {
            return _modules
                .Where(m => m.IsCritical && m.Result.Status == ModuleStatus.Failed)
                .Select(m => m.Name)
                .ToList();
        }

        public string GetSummary()
        {
            int success = _modules.Count(m => m.Result.Status == ModuleStatus.Success);
            int failed = _modules.Count(m => m.Result.Status == ModuleStatus.Failed);
            int warning = _modules.Count(m => m.Result.Status == ModuleStatus.Warning);

            return string.Format("成功: {0}, 失败: {1}, 警告: {2}", success, failed, warning);
        }

        #endregion

        #region 私有方法

        private async Task CheckModuleAsync(ModuleInfo moduleInfo)
        {
            LogDebug("检查模块: {0}", moduleInfo.Name);

            // 发送"检查中"状态
            moduleInfo.Result.Status = ModuleStatus.Checking;
            RaiseModuleChecked(moduleInfo.Name, "checking", "检查中...", moduleInfo.IsCritical);

            // 模拟UI渲染延迟
            await Task.Delay(200);

            try
            {
                // 在后台线程执行检查（避免阻塞UI）
                var result = await Task.Run(() => moduleInfo.CheckFunc());

                // 确保模块信息正确
                if (string.IsNullOrEmpty(result.ModuleName))
                {
                    result.ModuleName = moduleInfo.Name;
                }
                result.IsCritical = moduleInfo.IsCritical;

                moduleInfo.Result = result;

                // 检查关键模块是否失败
                if (moduleInfo.IsCritical && result.Status == ModuleStatus.Failed)
                {
                    AllCriticalPassed = false;
                }

                string statusStr = StatusToString(result.Status);
                LogDebug("  └─ {0}: {1}", statusStr, result.Message);

                // 在UI线程发送事件
                RaiseEventOnUIThread(() =>
                {
                    RaiseModuleChecked(result.ModuleName, statusStr, result.Message, result.IsCritical);
                });
            }
            catch (Exception ex)
            {
                // 捕获异常
                moduleInfo.Result.Status = ModuleStatus.Failed;
                moduleInfo.Result.Message = string.Format("检查异常: {0}", ex.Message);

                if (moduleInfo.IsCritical)
                {
                    AllCriticalPassed = false;
                }

                LogError(ex, "模块检查异常: {0}", moduleInfo.Name);

                RaiseEventOnUIThread(() =>
                {
                    RaiseModuleChecked(moduleInfo.Name, "failed", moduleInfo.Result.Message, moduleInfo.IsCritical);
                });
            }

            CheckedModules++;
        }

        private void RaiseModuleChecked(string name, string status, string message, bool isCritical)
        {
            var handler = ModuleChecked;
            if (handler != null)
            {
                var args = new ModuleCheckedEventArgs
                {
                    ModuleName = name,
                    Status = status,
                    Message = message,
                    IsCritical = isCritical
                };
                handler(this, args);
            }
        }

        private string StatusToString(ModuleStatus status)
        {
            switch (status)
            {
                case ModuleStatus.NotChecked: return "not_checked";
                case ModuleStatus.Checking: return "checking";
                case ModuleStatus.Success: return "success";
                case ModuleStatus.Failed: return "failed";
                case ModuleStatus.Warning: return "warning";
                default: return "unknown";
            }
        }

        private void RaiseEventOnUIThread(Action action)
        {
            var app = System.Windows.Application.Current;
            if (app != null)
            {
                app.Dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }

        #endregion

        #region 日志辅助

        private void LogDebug(string message, params object[] args)
        {
            if (_logService != null)
            {
                _logService.Debug(message, args);
            }
            System.Diagnostics.Debug.WriteLine(string.Format("[SystemMonitor] " + message, args));
        }

        private void LogInfo(string message, params object[] args)
        {
            if (_logService != null)
            {
                _logService.Information(message, args);
            }
            System.Diagnostics.Debug.WriteLine(string.Format("[SystemMonitor] " + message, args));
        }

        private void LogError(Exception ex, string message, params object[] args)
        {
            if (_logService != null)
            {
                _logService.Error(ex, message, args);
            }
            System.Diagnostics.Debug.WriteLine(string.Format("[SystemMonitor] ERROR: " + message + " - " + ex.Message, args));
        }

        #endregion

        #region INotifyPropertyChanged

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}