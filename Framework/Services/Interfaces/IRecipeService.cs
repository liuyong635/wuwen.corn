using System;
using System.Collections.Generic;
using SeedCut.Framework.Models;

namespace SeedCut.Framework.Services.Interfaces
{
    /// <summary>
    /// 配方服务接口
    /// 
    /// 核心特性：
    /// 1. 全内存缓存 - 启动时加载到内存，Handler读取直接从缓存取，无文件IO
    /// 2. 自动添加参数 - Handler调用GetXxx()时若参数不存在，自动创建并加入缓存
    /// 3. 手动保存 - UI编辑后需点击保存按钮才写入JSON文件
    /// 
    /// Handler中使用示例：
    /// <code>
    /// var recipe = ctx.GetService&lt;IRecipeService&gt;();
    /// double speed = recipe.GetDouble("Robot", "MoveSpeed", 100.0);
    /// int[] positions = recipe.GetIntArray("Robot", "Positions", new[] { 0, 0, 0 });
    /// </code>
    /// </summary>
    public interface IRecipeService : IDisposable
    {
        #region 事件

        /// <summary>
        /// 配方切换事件
        /// </summary>
        event EventHandler<RecipeChangedEventArgs> RecipeChanged;

        /// <summary>
        /// 参数修改事件（UI编辑触发）
        /// </summary>
        event EventHandler<ParameterModifiedEventArgs> ParameterModified;

        /// <summary>
        /// 参数自动创建事件（Handler调用GetXxx时参数不存在触发）
        /// UI监听此事件可自动刷新显示新参数
        /// </summary>
        event EventHandler<ParameterAutoCreatedEventArgs> ParameterAutoCreated;

        #endregion

        #region 属性

        /// <summary>
        /// 当前配方名称
        /// </summary>
        string CurrentRecipeName { get; }

        /// <summary>
        /// 当前配方数据（只读）
        /// </summary>
        RecipeData CurrentRecipe { get; }

        /// <summary>
        /// 是否有未保存的修改
        /// </summary>
        bool HasUnsavedChanges { get; }

        /// <summary>
        /// 可用配方列表
        /// </summary>
        IReadOnlyList<string> AvailableRecipes { get; }

        #endregion

        #region 配方管理

        /// <summary>
        /// 切换配方
        /// </summary>
        /// <param name="recipeName">配方名称</param>
        /// <returns>是否成功</returns>
        bool SwitchRecipe(string recipeName);

        /// <summary>
        /// 创建新配方（从当前配方复制）
        /// </summary>
        /// <param name="newRecipeName">新配方名称</param>
        /// <returns>是否成功</returns>
        bool CreateRecipe(string newRecipeName);

        /// <summary>
        /// 删除配方
        /// </summary>
        /// <param name="recipeName">配方名称</param>
        /// <returns>是否成功</returns>
        bool DeleteRecipe(string recipeName);

        /// <summary>
        /// 刷新配方列表（重新扫描文件夹）
        /// </summary>
        void RefreshRecipeList();

        #endregion

        #region 参数读取（Handler使用）- 不存在时自动创建

        /// <summary>
        /// 获取int参数
        /// 若参数不存在，自动创建并返回defaultValue
        /// </summary>
        /// <param name="groupId">分组ID（如 "Robot"）</param>
        /// <param name="key">参数Key（如 "RetryCount"）</param>
        /// <param name="defaultValue">默认值</param>
        /// <param name="name">显示名称（可选，自动创建时使用，为null则默认等于key）</param>
        /// <param name="unit">单位（可选，自动创建时使用）</param>
        /// <param name="description">描述（可选，自动创建时使用）</param>
        /// <returns>参数值</returns>
        /// <example>
        /// // 简洁写法
        /// int count = recipe.GetInt("Robot", "RetryCount", 3);
        /// 
        /// // 完整写法（带元数据）
        /// int count = recipe.GetInt("Robot", "RetryCount", 3, 
        ///     name: "重试次数", unit: "次", description: "操作失败时的重试次数");
        /// </example>
        int GetInt(string groupId, string key, int defaultValue = 0,
            string name = null, string unit = null, string description = null);

        /// <summary>
        /// 获取double参数
        /// 若参数不存在，自动创建并返回defaultValue
        /// </summary>
        /// <param name="groupId">分组ID</param>
        /// <param name="key">参数Key</param>
        /// <param name="defaultValue">默认值</param>
        /// <param name="name">显示名称（可选）</param>
        /// <param name="unit">单位（可选）</param>
        /// <param name="description">描述（可选）</param>
        /// <example>
        /// // 简洁写法
        /// double speed = recipe.GetDouble("Robot", "MoveSpeed", 100.0);
        /// 
        /// // 完整写法
        /// double speed = recipe.GetDouble("Robot", "MoveSpeed", 100.0,
        ///     name: "移动速度", unit: "mm/s", description: "机器人移动速度");
        /// </example>
        double GetDouble(string groupId, string key, double defaultValue = 0.0,
            string name = null, string unit = null, string description = null);

        /// <summary>
        /// 获取float参数
        /// 若参数不存在，自动创建并返回defaultValue
        /// </summary>
        float GetFloat(string groupId, string key, float defaultValue = 0f,
            string name = null, string unit = null, string description = null);

        /// <summary>
        /// 获取bool参数
        /// 若参数不存在，自动创建并返回defaultValue
        /// </summary>
        bool GetBool(string groupId, string key, bool defaultValue = false,
            string name = null, string unit = null, string description = null);

        /// <summary>
        /// 获取string参数
        /// 若参数不存在，自动创建并返回defaultValue
        /// </summary>
        string GetString(string groupId, string key, string defaultValue = "",
            string name = null, string unit = null, string description = null);

        /// <summary>
        /// 获取int数组
        /// 若参数不存在，自动创建并返回defaultValue
        /// </summary>
        /// <example>
        /// int[] levels = recipe.GetIntArray("Vision", "ThresholdLevels", new[] { 100, 150, 200 },
        ///     name: "阈值等级", description: "多级阈值配置");
        /// </example>
        int[] GetIntArray(string groupId, string key, int[] defaultValue = null,
            string name = null, string unit = null, string description = null);

        /// <summary>
        /// 获取double数组
        /// 若参数不存在，自动创建并返回defaultValue
        /// </summary>
        /// <example>
        /// double[] pos = recipe.GetDoubleArray("Robot", "HomePosition", new[] { 0.0, 0.0, 0.0 },
        ///     name: "原点位置", unit: "mm", description: "机器人原点坐标 [X, Y, Z]");
        /// </example>
        double[] GetDoubleArray(string groupId, string key, double[] defaultValue = null,
            string name = null, string unit = null, string description = null);

        /// <summary>
        /// 获取float数组
        /// 若参数不存在，自动创建并返回defaultValue
        /// </summary>
        float[] GetFloatArray(string groupId, string key, float[] defaultValue = null,
            string name = null, string unit = null, string description = null);

        #endregion

        #region 参数修改（UI使用）

        /// <summary>
        /// 设置参数值（修改内存缓存，不保存文件）
        /// </summary>
        /// <param name="groupId">分组ID</param>
        /// <param name="key">参数Key</param>
        /// <param name="value">新值</param>
        void SetValue(string groupId, string key, object value);

        /// <summary>
        /// 更新参数元数据（UI编辑显示名称、单位、描述）
        /// </summary>
        /// <param name="groupId">分组ID</param>
        /// <param name="key">参数Key</param>
        /// <param name="name">显示名称（null表示不修改）</param>
        /// <param name="unit">单位（null表示不修改）</param>
        /// <param name="description">描述（null表示不修改）</param>
        void UpdateParameterMeta(string groupId, string key, string name = null, string unit = null, string description = null);

        /// <summary>
        /// 更新分组元数据（UI编辑分组显示名称）
        /// </summary>
        /// <param name="groupId">分组ID</param>
        /// <param name="groupName">显示名称（null表示不修改）</param>
        /// <param name="sortOrder">排序顺序（null表示不修改）</param>
        void UpdateGroupMeta(string groupId, string groupName = null, int? sortOrder = null);

        /// <summary>
        /// 保存当前配方到文件
        /// </summary>
        /// <returns>是否成功</returns>
        bool Save();

        /// <summary>
        /// 放弃修改，重新从文件加载
        /// </summary>
        void DiscardChanges();

        #endregion

        #region 查询

        /// <summary>
        /// 获取所有分组（按SortOrder排序）
        /// </summary>
        IReadOnlyList<RecipeGroup> GetAllGroups();

        /// <summary>
        /// 获取指定分组
        /// </summary>
        /// <param name="groupId">分组ID</param>
        /// <returns>分组对象，不存在返回null</returns>
        RecipeGroup GetGroup(string groupId);

        /// <summary>
        /// 获取参数
        /// </summary>
        /// <param name="groupId">分组ID</param>
        /// <param name="key">参数Key</param>
        /// <returns>参数对象，不存在返回null</returns>
        RecipeParameter GetParameter(string groupId, string key);

        /// <summary>
        /// 检查参数是否存在
        /// </summary>
        /// <param name="groupId">分组ID</param>
        /// <param name="key">参数Key</param>
        /// <returns>是否存在</returns>
        bool HasParameter(string groupId, string key);

        #endregion
    }
}