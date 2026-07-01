using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace SeedCut.Framework.Models
{
    /// <summary>
    /// 配方参数
    /// </summary>
    public class RecipeParameter
    {
        /// <summary>
        /// 参数键名（唯一标识，如 "MoveSpeed"）
        /// </summary>
        [JsonProperty("key")]
        public string Key { get; set; }

        /// <summary>
        /// 显示名称（如 "移动速度"，默认等于Key）
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// 值类型（int, double, float, bool, string, int[], double[], float[]）
        /// </summary>
        [JsonProperty("valueType")]
        public string ValueType { get; set; }

        /// <summary>
        /// 值（字符串形式，数组用JSON如 "[1,2,3]"）
        /// </summary>
        [JsonProperty("value")]
        public string Value { get; set; }

        /// <summary>
        /// 单位（可选，如 "mm/s"）
        /// </summary>
        [JsonProperty("unit")]
        public string Unit { get; set; }

        /// <summary>
        /// 描述（可选）
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// 是否自动创建的（UI显示用，可高亮提示用户补充元数据）
        /// 不序列化到JSON文件
        /// </summary>
        [JsonIgnore]
        public bool IsAutoCreated { get; set; }

        /// <summary>
        /// 创建参数的深拷贝
        /// </summary>
        public RecipeParameter Clone()
        {
            return new RecipeParameter
            {
                Key = this.Key,
                Name = this.Name,
                ValueType = this.ValueType,
                Value = this.Value,
                Unit = this.Unit,
                Description = this.Description,
                IsAutoCreated = this.IsAutoCreated
            };
        }
    }

    /// <summary>
    /// 参数分组
    /// </summary>
    public class RecipeGroup
    {
        /// <summary>
        /// 分组ID（唯一标识，如 "Robot"）
        /// </summary>
        [JsonProperty("groupId")]
        public string GroupId { get; set; }

        /// <summary>
        /// 分组显示名称（如 "机器人参数"，默认等于GroupId）
        /// </summary>
        [JsonProperty("groupName")]
        public string GroupName { get; set; }

        /// <summary>
        /// 排序顺序
        /// </summary>
        [JsonProperty("sortOrder")]
        public int SortOrder { get; set; }

        /// <summary>
        /// 分组下的参数列表
        /// </summary>
        [JsonProperty("parameters")]
        public List<RecipeParameter> Parameters { get; set; } = new List<RecipeParameter>();

        /// <summary>
        /// 是否自动创建的分组（UI显示用）
        /// 不序列化到JSON文件
        /// </summary>
        [JsonIgnore]
        public bool IsAutoCreated { get; set; }

        /// <summary>
        /// 创建分组的深拷贝（包含参数）
        /// </summary>
        public RecipeGroup Clone()
        {
            var clone = new RecipeGroup
            {
                GroupId = this.GroupId,
                GroupName = this.GroupName,
                SortOrder = this.SortOrder,
                IsAutoCreated = this.IsAutoCreated
            };

            foreach (var param in this.Parameters)
            {
                clone.Parameters.Add(param.Clone());
            }

            return clone;
        }
    }

    /// <summary>
    /// 配方数据（单个配方的完整数据）
    /// </summary>
    public class RecipeData
    {
        /// <summary>
        /// 配方名称
        /// </summary>
        [JsonProperty("recipeName")]
        public string RecipeName { get; set; }

        /// <summary>
        /// 配方描述
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [JsonProperty("createdTime")]
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        [JsonProperty("modifiedTime")]
        public DateTime ModifiedTime { get; set; }

        /// <summary>
        /// 参数分组列表
        /// </summary>
        [JsonProperty("groups")]
        public List<RecipeGroup> Groups { get; set; } = new List<RecipeGroup>();

        /// <summary>
        /// 创建默认配方
        /// </summary>
        public static RecipeData CreateDefault(string recipeName = "Default")
        {
            return new RecipeData
            {
                RecipeName = recipeName,
                Description = string.Empty,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now,
                Groups = new List<RecipeGroup>()
            };
        }

        /// <summary>
        /// 创建配方的深拷贝
        /// </summary>
        public RecipeData Clone(string newRecipeName = null)
        {
            var clone = new RecipeData
            {
                RecipeName = newRecipeName ?? this.RecipeName,
                Description = this.Description,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now
            };

            foreach (var group in this.Groups)
            {
                clone.Groups.Add(group.Clone());
            }

            return clone;
        }
    }

    /// <summary>
    /// 配方设置（全局配置）
    /// </summary>
    public class RecipeSettings
    {
        /// <summary>
        /// 当前选中的配方名称
        /// </summary>
        [JsonProperty("currentRecipe")]
        public string CurrentRecipe { get; set; } = "Default";

        /// <summary>
        /// 可用配方列表
        /// </summary>
        [JsonProperty("availableRecipes")]
        public List<string> AvailableRecipes { get; set; } = new List<string> { "Default" };

        /// <summary>
        /// 创建默认设置
        /// </summary>
        public static RecipeSettings CreateDefault()
        {
            return new RecipeSettings
            {
                CurrentRecipe = "Default",
                AvailableRecipes = new List<string> { "Default" }
            };
        }
    }

    #region 事件参数类

    /// <summary>
    /// 配方切换事件参数
    /// </summary>
    public class RecipeChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 切换前的配方名称
        /// </summary>
        public string PreviousRecipeName { get; set; }

        /// <summary>
        /// 切换后的配方名称
        /// </summary>
        public string CurrentRecipeName { get; set; }

        /// <summary>
        /// 切换时间
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public RecipeChangedEventArgs(string previousRecipe, string currentRecipe)
        {
            PreviousRecipeName = previousRecipe;
            CurrentRecipeName = currentRecipe;
        }
    }

    /// <summary>
    /// 参数修改事件参数
    /// </summary>
    public class ParameterModifiedEventArgs : EventArgs
    {
        /// <summary>
        /// 分组ID
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// 参数Key
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 修改前的值
        /// </summary>
        public string OldValue { get; set; }

        /// <summary>
        /// 修改后的值
        /// </summary>
        public string NewValue { get; set; }

        /// <summary>
        /// 修改时间
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public ParameterModifiedEventArgs(string groupId, string key)
        {
            GroupId = groupId;
            Key = key;
        }

        public ParameterModifiedEventArgs(string groupId, string key, string oldValue, string newValue)
        {
            GroupId = groupId;
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    /// <summary>
    /// 参数自动创建事件参数
    /// 当Handler调用GetXxx()时参数不存在，自动创建后触发此事件
    /// </summary>
    public class ParameterAutoCreatedEventArgs : EventArgs
    {
        /// <summary>
        /// 分组ID
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// 参数Key
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 值类型
        /// </summary>
        public string ValueType { get; set; }

        /// <summary>
        /// 默认值
        /// </summary>
        public object DefaultValue { get; set; }

        /// <summary>
        /// 是否同时创建了新分组
        /// </summary>
        public bool IsNewGroup { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public ParameterAutoCreatedEventArgs(string groupId, string key, string valueType, object defaultValue, bool isNewGroup)
        {
            GroupId = groupId;
            Key = key;
            ValueType = valueType;
            DefaultValue = defaultValue;
            IsNewGroup = isNewGroup;
        }
    }

    #endregion
}
