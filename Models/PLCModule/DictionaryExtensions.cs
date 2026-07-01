using System.Collections.Generic;

namespace SeedCut.Models.PLCModule
{
    /// <summary>
    /// 字典扩展方法 - 为 .NET Framework 提供 GetValueOrDefault 支持
    /// </summary>
    public static class DictionaryExtensions
    {
        /// <summary>
        /// 获取字典中的值，如果键不存在则返回默认值
        /// </summary>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary,
            TKey key,
            TValue defaultValue = default(TValue))
        {
            if (dictionary == null)
                return defaultValue;

            return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
        }

        /// <summary>
        /// 获取字典中的object值并转换为指定类型
        /// </summary>
        public static T GetValueOrDefault<T>(
            this Dictionary<string, object> dictionary,
            string key,
            T defaultValue = default(T))
        {
            if (dictionary == null)
                return defaultValue;

            if (!dictionary.TryGetValue(key, out var value))
                return defaultValue;

            if (value == null)
                return defaultValue;

            try
            {
                // 尝试直接转换
                if (value is T typedValue)
                    return typedValue;

                // 尝试类型转换
                return (T)System.Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 获取字典中的字符串值
        /// </summary>
        public static string GetStringOrDefault(
            this Dictionary<string, object> dictionary,
            string key,
            string defaultValue = "")
        {
            if (dictionary == null)
                return defaultValue;

            if (!dictionary.TryGetValue(key, out var value))
                return defaultValue;

            return value?.ToString() ?? defaultValue;
        }

        /// <summary>
        /// 获取字典中的整数值
        /// </summary>
        public static int GetIntOrDefault(
            this Dictionary<string, object> dictionary,
            string key,
            int defaultValue = 0)
        {
            if (dictionary == null)
                return defaultValue;

            if (!dictionary.TryGetValue(key, out var value))
                return defaultValue;

            if (value == null)
                return defaultValue;

            if (value is int intValue)
                return intValue;

            if (int.TryParse(value.ToString(), out var result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// 获取字典中的双精度浮点数值
        /// </summary>
        public static double GetDoubleOrDefault(
            this Dictionary<string, object> dictionary,
            string key,
            double defaultValue = 0.0)
        {
            if (dictionary == null)
                return defaultValue;

            if (!dictionary.TryGetValue(key, out var value))
                return defaultValue;

            if (value == null)
                return defaultValue;

            if (value is double doubleValue)
                return doubleValue;

            if (double.TryParse(value.ToString(), out var result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// 获取字典中的布尔值
        /// </summary>
        public static bool GetBoolOrDefault(
            this Dictionary<string, object> dictionary,
            string key,
            bool defaultValue = false)
        {
            if (dictionary == null)
                return defaultValue;

            if (!dictionary.TryGetValue(key, out var value))
                return defaultValue;

            if (value == null)
                return defaultValue;

            if (value is bool boolValue)
                return boolValue;

            if (bool.TryParse(value.ToString(), out var result))
                return result;

            return defaultValue;
        }
    }
}