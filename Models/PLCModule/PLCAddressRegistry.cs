using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedCut.Models.PLCModule
{
    /// <summary>
    /// PLC地址注册表 - 统一管理所有PLC地址
    /// </summary>
    public class PLCAddressRegistry
    {
        private readonly Dictionary<string, PLCAddressItem> _addressDictionary;
        private readonly List<PLCAddressItem> _addressList;

        public string Version { get; set; } = "4.0";

        public IReadOnlyList<PLCAddressItem> Addresses => _addressList.AsReadOnly();

        public PLCAddressRegistry()
        {
            _addressDictionary = new Dictionary<string, PLCAddressItem>(StringComparer.OrdinalIgnoreCase);
            _addressList = new List<PLCAddressItem>();
        }

        #region 地址管理

        public void AddAddress(PLCAddressItem address)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));

            if (string.IsNullOrEmpty(address.Name))
                throw new ArgumentException("地址名称不能为空");

            if (_addressDictionary.ContainsKey(address.Name))
                throw new InvalidOperationException($"地址名称已存在: {address.Name}");

            _addressDictionary[address.Name] = address;
            _addressList.Add(address);
        }

        public void AddAddresses(IEnumerable<PLCAddressItem> addresses)
        {
            foreach (var address in addresses)
            {
                AddAddress(address);
            }
        }

        public PLCAddressItem GetAddress(string name)
        {
            return string.IsNullOrEmpty(name) ? null :
                _addressDictionary.TryGetValue(name, out var address) ? address : null;
        }

        public bool ContainsAddress(string name)
        {
            return !string.IsNullOrEmpty(name) && _addressDictionary.ContainsKey(name);
        }

        public void Clear()
        {
            _addressDictionary.Clear();
            _addressList.Clear();
        }

        #endregion

        #region 查询方法

        public List<PLCAddressItem> GetAddressesByGroup(string group)
        {
            return string.IsNullOrEmpty(group) ? new List<PLCAddressItem>() :
                _addressList.Where(a => string.Equals(a.Group, group, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public List<PLCAddressItem> GetAddressesBySubGroup(string group, string subGroup)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(subGroup))
                return new List<PLCAddressItem>();

            return _addressList.Where(a =>
                string.Equals(a.Group, group, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.SubGroup, subGroup, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public List<string> GetAllGroups()
        {
            return _addressList
                .Select(a => a.Group)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g)
                .ToList();
        }

        public List<PLCAddressItem> GetUIAddresses()
        {
            return _addressList.Where(a => a.ShowInUI).ToList();
        }

        public List<PLCAddressItem> GetBackgroundAddresses()
        {
            return _addressList.Where(a => !a.ShowInUI).ToList();
        }

        public List<PLCAddressItem> GetWritableAddresses()
        {
            return _addressList.Where(a => a.Writable).ToList();
        }

        #endregion

        #region 验证

        public List<string> Validate()
        {
            var errors = new List<string>();

            // ✅ 修复：检查位地址冲突时需要包含 MemoryArea
            var bitGroups = _addressList
                .Where(a => a.DataType == PLCAddressType.Bit)
                .GroupBy(a => new { a.MemoryArea, a.DBNumber, a.StartAddress, a.BitPosition })  // ✅ 添加 MemoryArea
                .Where(g => g.Count() > 1);

            foreach (var group in bitGroups)
            {
                var names = string.Join(", ", group.Select(a => a.Name));
                // ✅ 修复：显示正确的地址格式
                string areaPrefix;
                switch (group.Key.MemoryArea)
                {
                    case PLCMemoryArea.Input:
                        areaPrefix = "I";
                        break;
                    case PLCMemoryArea.Output:
                        areaPrefix = "Q";
                        break;
                    case PLCMemoryArea.DataBlock:
                        areaPrefix = $"DB{group.Key.DBNumber}.DBX";
                        break;
                    default:
                        areaPrefix = "M";
                        break;
                }
                errors.Add($"位地址冲突: {areaPrefix}{group.Key.StartAddress}.{group.Key.BitPosition} -> {names}");
            }

            // UI地址缺少信息的检查保持不变
            foreach (var addr in _addressList.Where(a => a.ShowInUI))
            {
                if (string.IsNullOrEmpty(addr.SubGroup))
                    errors.Add($"UI地址缺少SubGroup: {addr.Name}");
            }

            return errors;
        }

        #endregion

        #region 统计和打印

        public void PrintAddressMap(string group = null)
        {
            var addresses = string.IsNullOrEmpty(group) ? _addressList :
                _addressList.Where(a => string.Equals(a.Group, group, StringComparison.OrdinalIgnoreCase));

            System.Diagnostics.Debug.WriteLine($"");
            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"地址映射表 {(string.IsNullOrEmpty(group) ? "(全部)" : $"({group})")}");
            System.Diagnostics.Debug.WriteLine($"========================================");

            foreach (var addr in addresses.OrderBy(a => a.Group).ThenBy(a => a.SubGroup).ThenBy(a => a.UIOrder))
            {
                var ui = addr.ShowInUI ? "[UI]" : "    ";
                var rw = addr.Writable ? "RW" : "R-";
                System.Diagnostics.Debug.WriteLine($"{ui} {rw} {addr.GetSiemensAddress(),-25} | {addr.Name}");
            }

            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"");
        }

        #endregion
    }
}