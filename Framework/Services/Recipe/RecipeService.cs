using Newtonsoft.Json;
using SeedCut.Framework.Models;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SeedCut.Framework.Services.Recipe
{
    /// <summary>
    /// 配方服务实现
    /// 
    /// 核心特性：
    /// 1. 全内存缓存 - 启动时加载到内存，Handler读取直接从缓存取
    /// 2. 自动添加参数 - GetXxx()时若参数不存在，自动创建
    /// 3. 手动保存 - UI编辑后需点击保存按钮才写入文件
    /// </summary>
    public class RecipeService : IRecipeService
    {
        #region 私有字段

        private readonly ILogService _logService;
        private readonly string _recipeDirectory;
        private readonly object _lock = new object();

        // 配方设置
        private RecipeSettings _settings;

        // 当前配方数据
        private RecipeData _currentRecipe;

        // 内存缓存（用于快速查找）
        private readonly ConcurrentDictionary<string, RecipeGroup> _groupCache;
        private readonly ConcurrentDictionary<string, RecipeParameter> _parameterCache;

        // 未保存修改标记
        private bool _hasUnsavedChanges;

        // 是否已释放
        private bool _disposed;

        #endregion

        #region 事件

        public event EventHandler<RecipeChangedEventArgs> RecipeChanged;
        public event EventHandler<ParameterModifiedEventArgs> ParameterModified;
        public event EventHandler<ParameterAutoCreatedEventArgs> ParameterAutoCreated;

        #endregion

        #region 属性

        public string CurrentRecipeName => _currentRecipe?.RecipeName ?? string.Empty;

        public RecipeData CurrentRecipe => _currentRecipe;

        public bool HasUnsavedChanges => _hasUnsavedChanges;

        public IReadOnlyList<string> AvailableRecipes => _settings?.AvailableRecipes?.AsReadOnly()
            ?? new List<string>().AsReadOnly();

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建配方服务实例
        /// </summary>
        /// <param name="logService">日志服务（可选）</param>
        /// <param name="recipeDirectory">配方目录（可选，默认为 Config/Recipes）</param>
        public RecipeService(ILogService logService = null, string recipeDirectory = null)
        {
            _logService = logService;
            _groupCache = new ConcurrentDictionary<string, RecipeGroup>(StringComparer.OrdinalIgnoreCase);
            _parameterCache = new ConcurrentDictionary<string, RecipeParameter>(StringComparer.OrdinalIgnoreCase);

            // 确定配方目录
            _recipeDirectory = recipeDirectory ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Config",
                "Recipes");

            // 初始化
            Initialize();
        }

        /// <summary>
        /// 初始化服务
        /// </summary>
        private void Initialize()
        {
            try
            {
                // 确保目录存在
                RecipeStorage.EnsureDirectory(_recipeDirectory);

                // 加载或创建设置
                _settings = RecipeStorage.LoadSettings(_recipeDirectory);
                if (_settings == null)
                {
                    _settings = RecipeSettings.CreateDefault();
                    RecipeStorage.SaveSettings(_recipeDirectory, _settings);
                    _logService?.Information("[RecipeService] 创建默认配方设置");
                }

                // 刷新配方列表（扫描目录）
                RefreshRecipeListInternal();

                // 加载当前配方
                LoadCurrentRecipe();

                _logService?.Information("[RecipeService] 初始化完成，当前配方: {0}，可用配方: {1}",
                    CurrentRecipeName, string.Join(", ", AvailableRecipes));
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[RecipeService] 初始化失败");
                throw;
            }
        }

        #endregion

        #region 配方管理

        public bool SwitchRecipe(string recipeName)
        {
            if (string.IsNullOrWhiteSpace(recipeName))
                return false;

            if (recipeName.Equals(CurrentRecipeName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!_settings.AvailableRecipes.Contains(recipeName, StringComparer.OrdinalIgnoreCase))
            {
                _logService?.Warning("[RecipeService] 配方不存在: {0}", recipeName);
                return false;
            }

            lock (_lock)
            {
                var previousRecipe = CurrentRecipeName;

                try
                {
                    // 加载新配方
                    var newRecipe = RecipeStorage.LoadRecipe(_recipeDirectory, recipeName);
                    if (newRecipe == null)
                    {
                        _logService?.Error(null, "[RecipeService] 加载配方失败: {0}", recipeName);
                        return false;
                    }

                    // 更新当前配方
                    _currentRecipe = newRecipe;
                    _settings.CurrentRecipe = recipeName;
                    RecipeStorage.SaveSettings(_recipeDirectory, _settings);

                    // 重建缓存
                    RebuildCache();

                    // 清除未保存标记
                    _hasUnsavedChanges = false;

                    _logService?.Information("[RecipeService] 切换配方: {0} -> {1}", previousRecipe, recipeName);

                    // 触发事件
                    RecipeChanged?.Invoke(this, new RecipeChangedEventArgs(previousRecipe, recipeName));

                    return true;
                }
                catch (Exception ex)
                {
                    _logService?.Error(ex, "[RecipeService] 切换配方异常: {0}", recipeName);
                    return false;
                }
            }
        }

        public bool CreateRecipe(string newRecipeName)
        {
            if (string.IsNullOrWhiteSpace(newRecipeName))
                return false;

            // 检查名称是否已存在
            if (_settings.AvailableRecipes.Contains(newRecipeName, StringComparer.OrdinalIgnoreCase))
            {
                _logService?.Warning("[RecipeService] 配方已存在: {0}", newRecipeName);
                return false;
            }

            // 验证名称合法性
            if (!IsValidRecipeName(newRecipeName))
            {
                _logService?.Warning("[RecipeService] 配方名称不合法: {0}", newRecipeName);
                return false;
            }

            lock (_lock)
            {
                try
                {
                    // 从当前配方复制
                    var newRecipe = _currentRecipe?.Clone(newRecipeName) ?? RecipeData.CreateDefault(newRecipeName);

                    // 保存新配方
                    if (!RecipeStorage.SaveRecipe(_recipeDirectory, newRecipe))
                    {
                        _logService?.Error(null, "[RecipeService] 保存新配方失败: {0}", newRecipeName);
                        return false;
                    }

                    // 更新配方列表
                    _settings.AvailableRecipes.Add(newRecipeName);
                    RecipeStorage.SaveSettings(_recipeDirectory, _settings);

                    _logService?.Information("[RecipeService] 创建配方成功: {0}（从 {1} 复制）",
                        newRecipeName, CurrentRecipeName);

                    return true;
                }
                catch (Exception ex)
                {
                    _logService?.Error(ex, "[RecipeService] 创建配方异常: {0}", newRecipeName);
                    return false;
                }
            }
        }

        public bool DeleteRecipe(string recipeName)
        {
            if (string.IsNullOrWhiteSpace(recipeName))
                return false;

            // 不允许删除Default配方
            if (recipeName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                _logService?.Warning("[RecipeService] 不允许删除Default配方");
                return false;
            }

            // 不允许删除当前正在使用的配方
            if (recipeName.Equals(CurrentRecipeName, StringComparison.OrdinalIgnoreCase))
            {
                _logService?.Warning("[RecipeService] 不允许删除当前正在使用的配方: {0}", recipeName);
                return false;
            }

            if (!_settings.AvailableRecipes.Contains(recipeName, StringComparer.OrdinalIgnoreCase))
            {
                _logService?.Warning("[RecipeService] 配方不存在: {0}", recipeName);
                return false;
            }

            lock (_lock)
            {
                try
                {
                    // 删除文件
                    if (!RecipeStorage.DeleteRecipe(_recipeDirectory, recipeName))
                    {
                        _logService?.Error(null, "[RecipeService] 删除配方文件失败: {0}", recipeName);
                        return false;
                    }

                    // 更新配方列表
                    _settings.AvailableRecipes.RemoveAll(r =>
                        r.Equals(recipeName, StringComparison.OrdinalIgnoreCase));
                    RecipeStorage.SaveSettings(_recipeDirectory, _settings);

                    _logService?.Information("[RecipeService] 删除配方成功: {0}", recipeName);

                    return true;
                }
                catch (Exception ex)
                {
                    _logService?.Error(ex, "[RecipeService] 删除配方异常: {0}", recipeName);
                    return false;
                }
            }
        }

        public void RefreshRecipeList()
        {
            lock (_lock)
            {
                RefreshRecipeListInternal();
            }
        }

        private void RefreshRecipeListInternal()
        {
            var recipes = RecipeStorage.ScanRecipes(_recipeDirectory);

            // 确保Default配方存在
            if (!recipes.Contains("Default", StringComparer.OrdinalIgnoreCase))
            {
                // 创建Default配方
                var defaultRecipe = RecipeData.CreateDefault();
                RecipeStorage.SaveRecipe(_recipeDirectory, defaultRecipe);
                recipes.Insert(0, "Default");
                _logService?.Information("[RecipeService] 自动创建Default配方");
            }

            _settings.AvailableRecipes = recipes;

            // 如果当前配方不在列表中，切换到Default
            if (!recipes.Contains(_settings.CurrentRecipe, StringComparer.OrdinalIgnoreCase))
            {
                _settings.CurrentRecipe = "Default";
            }

            RecipeStorage.SaveSettings(_recipeDirectory, _settings);
        }

        #endregion

        #region 参数读取（带自动创建）

        public int GetInt(string groupId, string key, int defaultValue = 0,
            string name = null, string unit = null, string description = null)
        {
            var param = GetOrCreateParameter(groupId, key, "int", defaultValue, name, unit, description);
            return RecipeTypeConverter.ToInt(param.Value, defaultValue);
        }

        public double GetDouble(string groupId, string key, double defaultValue = 0.0,
            string name = null, string unit = null, string description = null)
        {
            var param = GetOrCreateParameter(groupId, key, "double", defaultValue, name, unit, description);
            return RecipeTypeConverter.ToDouble(param.Value, defaultValue);
        }

        public float GetFloat(string groupId, string key, float defaultValue = 0f,
            string name = null, string unit = null, string description = null)
        {
            var param = GetOrCreateParameter(groupId, key, "float", defaultValue, name, unit, description);
            return RecipeTypeConverter.ToFloat(param.Value, defaultValue);
        }

        public bool GetBool(string groupId, string key, bool defaultValue = false,
            string name = null, string unit = null, string description = null)
        {
            var param = GetOrCreateParameter(groupId, key, "bool", defaultValue, name, unit, description);
            return RecipeTypeConverter.ToBool(param.Value, defaultValue);
        }

        public string GetString(string groupId, string key, string defaultValue = "",
            string name = null, string unit = null, string description = null)
        {
            var param = GetOrCreateParameter(groupId, key, "string", defaultValue, name, unit, description);
            return param.Value ?? defaultValue;
        }

        public int[] GetIntArray(string groupId, string key, int[] defaultValue = null,
            string name = null, string unit = null, string description = null)
        {
            var param = GetOrCreateParameter(groupId, key, "int[]", defaultValue, name, unit, description);
            return RecipeTypeConverter.ToIntArray(param.Value, defaultValue);
        }

        public double[] GetDoubleArray(string groupId, string key, double[] defaultValue = null,
            string name = null, string unit = null, string description = null)
        {
            var param = GetOrCreateParameter(groupId, key, "double[]", defaultValue, name, unit, description);
            return RecipeTypeConverter.ToDoubleArray(param.Value, defaultValue);
        }

        public float[] GetFloatArray(string groupId, string key, float[] defaultValue = null,
            string name = null, string unit = null, string description = null)
        {
            var param = GetOrCreateParameter(groupId, key, "float[]", defaultValue, name, unit, description);
            return RecipeTypeConverter.ToFloatArray(param.Value, defaultValue);
        }

        /// <summary>
        /// 获取或创建参数（核心方法）
        /// </summary>
        /// <param name="groupId">分组ID</param>
        /// <param name="key">参数Key</param>
        /// <param name="valueType">值类型</param>
        /// <param name="defaultValue">默认值</param>
        /// <param name="name">显示名称（可选，为null则使用key）</param>
        /// <param name="unit">单位（可选）</param>
        /// <param name="description">描述（可选）</param>
        private RecipeParameter GetOrCreateParameter(
            string groupId, string key, string valueType, object defaultValue,
            string name = null, string unit = null, string description = null)
        {
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("groupId和key不能为空");
            }

            var cacheKey = BuildCacheKey(groupId, key);

            // 1. 尝试从缓存获取
            if (_parameterCache.TryGetValue(cacheKey, out var param))
            {
                return param;
            }

            // 2. 不存在，需要创建
            lock (_lock)
            {
                // 双重检查
                if (_parameterCache.TryGetValue(cacheKey, out param))
                {
                    return param;
                }

                bool isNewGroup = false;

                // 2.1 获取或创建分组
                if (!_groupCache.TryGetValue(groupId, out var group))
                {
                    group = new RecipeGroup
                    {
                        GroupId = groupId,
                        GroupName = groupId,  // 默认等于ID，用户可在UI修改
                        SortOrder = _groupCache.Count + 1,
                        IsAutoCreated = true
                    };
                    _groupCache[groupId] = group;
                    _currentRecipe.Groups.Add(group);
                    isNewGroup = true;

                    _logService?.Information("[RecipeService] 自动创建分组: {0}", groupId);
                }

                // 2.2 创建参数
                // 如果传入了元数据，则使用传入的值；否则使用默认值
                bool hasMetadata = !string.IsNullOrEmpty(name) ||
                                   !string.IsNullOrEmpty(unit) ||
                                   !string.IsNullOrEmpty(description);

                param = new RecipeParameter
                {
                    Key = key,
                    Name = name ?? key,  // 如果没有传入name，默认等于Key
                    ValueType = valueType,
                    Value = RecipeTypeConverter.ToString(defaultValue, valueType),
                    Unit = unit ?? string.Empty,
                    Description = description ?? string.Empty,
                    IsAutoCreated = !hasMetadata  // 如果传入了元数据，则不标记为自动创建
                };

                // 2.3 加入分组和缓存
                group.Parameters.Add(param);
                _parameterCache[cacheKey] = param;

                // 2.4 标记有修改
                _hasUnsavedChanges = true;

                if (hasMetadata)
                {
                    _logService?.Information("[RecipeService] 自动创建参数(带元数据): [{0}].{1} = {2} (名称: {3}, 单位: {4})",
                        groupId, key, param.Value, param.Name, param.Unit);
                }
                else
                {
                    _logService?.Information("[RecipeService] 自动创建参数: [{0}].{1} = {2} (类型: {3})",
                        groupId, key, param.Value, valueType);
                }

                // 2.5 触发事件，通知UI刷新
                ParameterAutoCreated?.Invoke(this, new ParameterAutoCreatedEventArgs(
                    groupId, key, valueType, defaultValue, isNewGroup));

                return param;
            }
        }

        #endregion

        #region 参数修改

        public void SetValue(string groupId, string key, object value)
        {
            var cacheKey = BuildCacheKey(groupId, key);

            if (!_parameterCache.TryGetValue(cacheKey, out var param))
            {
                _logService?.Warning("[RecipeService] 参数不存在: [{0}].{1}", groupId, key);
                return;
            }

            lock (_lock)
            {
                var oldValue = param.Value;
                param.Value = RecipeTypeConverter.ToString(value, param.ValueType);
                _hasUnsavedChanges = true;

                // 更新修改时间
                _currentRecipe.ModifiedTime = DateTime.Now;

                ParameterModified?.Invoke(this, new ParameterModifiedEventArgs(groupId, key, oldValue, param.Value));
            }
        }

        public void UpdateParameterMeta(string groupId, string key, string name = null, string unit = null, string description = null)
        {
            var param = GetParameter(groupId, key);
            if (param == null) return;

            lock (_lock)
            {
                if (name != null) param.Name = name;
                if (unit != null) param.Unit = unit;
                if (description != null) param.Description = description;

                param.IsAutoCreated = false;  // 用户已编辑，不再标记为自动创建
                _hasUnsavedChanges = true;
                _currentRecipe.ModifiedTime = DateTime.Now;

                ParameterModified?.Invoke(this, new ParameterModifiedEventArgs(groupId, key));
            }
        }

        public void UpdateGroupMeta(string groupId, string groupName = null, int? sortOrder = null)
        {
            if (!_groupCache.TryGetValue(groupId, out var group)) return;

            lock (_lock)
            {
                if (groupName != null) group.GroupName = groupName;
                if (sortOrder.HasValue) group.SortOrder = sortOrder.Value;

                group.IsAutoCreated = false;
                _hasUnsavedChanges = true;
                _currentRecipe.ModifiedTime = DateTime.Now;
            }
        }

        public bool Save()
        {
            lock (_lock)
            {
                try
                {
                    _currentRecipe.ModifiedTime = DateTime.Now;

                    if (RecipeStorage.SaveRecipe(_recipeDirectory, _currentRecipe))
                    {
                        _hasUnsavedChanges = false;
                        _logService?.Information("[RecipeService] 保存配方成功: {0}", CurrentRecipeName);
                        return true;
                    }

                    _logService?.Error(null, "[RecipeService] 保存配方失败: {0}", CurrentRecipeName);
                    return false;
                }
                catch (Exception ex)
                {
                    _logService?.Error(ex, "[RecipeService] 保存配方异常: {0}", CurrentRecipeName);
                    return false;
                }
            }
        }

        public void DiscardChanges()
        {
            lock (_lock)
            {
                LoadCurrentRecipe();
                _hasUnsavedChanges = false;
                _logService?.Information("[RecipeService] 已放弃修改，重新加载配方: {0}", CurrentRecipeName);
            }
        }

        #endregion

        #region 查询

        public IReadOnlyList<RecipeGroup> GetAllGroups()
        {
            return _currentRecipe?.Groups?
                .OrderBy(g => g.SortOrder)
                .ToList()
                .AsReadOnly() ?? new List<RecipeGroup>().AsReadOnly();
        }

        public RecipeGroup GetGroup(string groupId)
        {
            _groupCache.TryGetValue(groupId, out var group);
            return group;
        }

        public RecipeParameter GetParameter(string groupId, string key)
        {
            var cacheKey = BuildCacheKey(groupId, key);
            _parameterCache.TryGetValue(cacheKey, out var param);
            return param;
        }

        public bool HasParameter(string groupId, string key)
        {
            var cacheKey = BuildCacheKey(groupId, key);
            return _parameterCache.ContainsKey(cacheKey);
        }

        #endregion

        #region 私有辅助方法

        /// <summary>
        /// 加载当前配方
        /// </summary>
        private void LoadCurrentRecipe()
        {
            var recipeName = _settings.CurrentRecipe ?? "Default";
            _currentRecipe = RecipeStorage.LoadRecipe(_recipeDirectory, recipeName);

            if (_currentRecipe == null)
            {
                // 配方不存在，创建默认配方
                _currentRecipe = RecipeData.CreateDefault(recipeName);
                RecipeStorage.SaveRecipe(_recipeDirectory, _currentRecipe);
                _logService?.Information("[RecipeService] 创建默认配方: {0}", recipeName);
            }

            RebuildCache();
        }

        /// <summary>
        /// 重建内存缓存
        /// </summary>
        private void RebuildCache()
        {
            _groupCache.Clear();
            _parameterCache.Clear();

            if (_currentRecipe?.Groups == null) return;

            foreach (var group in _currentRecipe.Groups)
            {
                _groupCache[group.GroupId] = group;

                foreach (var param in group.Parameters)
                {
                    var cacheKey = BuildCacheKey(group.GroupId, param.Key);
                    _parameterCache[cacheKey] = param;
                }
            }
        }

        /// <summary>
        /// 构建缓存键
        /// </summary>
        private string BuildCacheKey(string groupId, string key)
        {
            return $"{groupId}.{key}";
        }

        /// <summary>
        /// 验证配方名称是否合法
        /// </summary>
        private bool IsValidRecipeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            // 不允许包含文件系统非法字符
            var invalidChars = Path.GetInvalidFileNameChars();
            return !name.Any(c => invalidChars.Contains(c));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            // 如果有未保存的修改，可以选择自动保存或警告
            if (_hasUnsavedChanges)
            {
                _logService?.Warning("[RecipeService] 服务释放时存在未保存的修改，配方: {0}", CurrentRecipeName);
            }

            _disposed = true;
        }

        #endregion
    }

    #region 辅助类：类型转换器

    /// <summary>
    /// 配方参数类型转换器
    /// </summary>
    internal static class RecipeTypeConverter
    {
        /// <summary>
        /// 将值转换为字符串
        /// </summary>
        public static string ToString(object value, string valueType)
        {
            if (value == null) return string.Empty;

            switch (valueType?.ToLowerInvariant())
            {
                case "int":
                case "double":
                case "float":
                case "bool":
                case "string":
                    return Convert.ToString(value, CultureInfo.InvariantCulture);

                case "int[]":
                case "double[]":
                case "float[]":
                    return JsonConvert.SerializeObject(value);

                default:
                    return value.ToString();
            }
        }

        public static int ToInt(string value, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result : defaultValue;
        }

        public static double ToDouble(string value, double defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result : defaultValue;
        }

        public static float ToFloat(string value, float defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result : defaultValue;
        }

        public static bool ToBool(string value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            return bool.TryParse(value, out var result) ? result : defaultValue;
        }

        public static int[] ToIntArray(string value, int[] defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            try
            {
                return JsonConvert.DeserializeObject<int[]>(value) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static double[] ToDoubleArray(string value, double[] defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            try
            {
                return JsonConvert.DeserializeObject<double[]>(value) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static float[] ToFloatArray(string value, float[] defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            try
            {
                return JsonConvert.DeserializeObject<float[]>(value) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    #endregion

    #region 辅助类：文件存储

    /// <summary>
    /// 配方文件存储辅助类
    /// </summary>
    internal static class RecipeStorage
    {
        private const string SettingsFileName = "_settings.json";

        /// <summary>
        /// 确保目录存在
        /// </summary>
        public static void EnsureDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// 加载设置文件
        /// </summary>
        public static RecipeSettings LoadSettings(string directory)
        {
            var filePath = Path.Combine(directory, SettingsFileName);
            if (!File.Exists(filePath)) return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<RecipeSettings>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 保存设置文件
        /// </summary>
        public static bool SaveSettings(string directory, RecipeSettings settings)
        {
            try
            {
                EnsureDirectory(directory);
                var filePath = Path.Combine(directory, SettingsFileName);
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 加载配方
        /// </summary>
        public static RecipeData LoadRecipe(string directory, string recipeName)
        {
            var filePath = Path.Combine(directory, $"{recipeName}.json");
            if (!File.Exists(filePath)) return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<RecipeData>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 保存配方
        /// </summary>
        public static bool SaveRecipe(string directory, RecipeData recipe)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.RecipeName))
                return false;

            try
            {
                EnsureDirectory(directory);
                var filePath = Path.Combine(directory, $"{recipe.RecipeName}.json");
                var json = JsonConvert.SerializeObject(recipe, Formatting.Indented);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 删除配方
        /// </summary>
        public static bool DeleteRecipe(string directory, string recipeName)
        {
            try
            {
                var filePath = Path.Combine(directory, $"{recipeName}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 扫描目录中的所有配方
        /// </summary>
        public static List<string> ScanRecipes(string directory)
        {
            var recipes = new List<string>();

            if (!Directory.Exists(directory))
                return recipes;

            try
            {
                var files = Directory.GetFiles(directory, "*.json");
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    // 排除设置文件
                    if (!fileName.StartsWith("_"))
                    {
                        recipes.Add(fileName);
                    }
                }
            }
            catch
            {
                // 忽略扫描错误
            }

            return recipes;
        }
    }

    #endregion
}