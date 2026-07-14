using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
namespace Wuwen.Core.Services.Storage
{
    public enum MonitorInfoType
    {
        ChildFolder = 1,
        File = 2,
        All = 4
    }
    public class MonitorInfo
    {
        public MonitorInfoType Type = MonitorInfoType.ChildFolder;

        public DirectoryInfo Folder { get; set; }


    }
    public class StorageTask : Lazy<StorageTask>
    {
        public static StorageTask Current { get; private set; } = new Lazy<StorageTask>().Value;

        private ConcurrentDictionary<string, DriveInfo> driveInfo = new ConcurrentDictionary<string, DriveInfo>();

        private ConcurrentDictionary<string, ConcurrentDictionary<string, MonitorInfo>> monitorfolders = new ConcurrentDictionary<string, ConcurrentDictionary<string, MonitorInfo>>();

        DateTime lastTime = DateTime.Now.AddDays(-1);
        private DateTime lastCheckTime = DateTime.MinValue;
        private bool isChecking = false;
        public StorageTask()
        {

            CheckDiskVolumeLoop();
        }
        /// <summary>
        /// 每隔一分钟检查一次
        /// </summary>

        private async void CheckDiskVolumeLoop()
        {
          
            do
            {
                await Task.Delay(60 * 1000);
                if (!isChecking && (DateTime.Now - lastCheckTime).TotalMinutes >= 5)
                {
                    CheckDiskVolume();
                }
            } while (true);


        }
        /// <summary>
        /// 检查磁盘空间
        /// </summary>
        public async void CheckDiskVolume()
        {
            Console.WriteLine("StartCheck");
            isChecking = true;
            lastCheckTime = DateTime.Now;
            await Task.Run(() =>
            {
                var keys = monitorfolders.Keys;
                foreach (var key in keys)
                {
                    if (monitorfolders.TryGetValue(key, out var d))
                    {
                        if (driveInfo.TryGetValue(key, out var ds))
                        {
                            if ((DateTime.Now - lastTime).TotalDays > 1)
                            {
                                lastTime = DateTime.Now.AddDays(-1);
                            }
                            ///小于100GB
                            if ((NotEnough(ds)))
                            {
                                List<MonitorInfo> fs = d.Values.ToList();
                                var dir = fs.FindAll(o => o.Folder != null && o.Folder.Exists);

                                List<DirectoryInfo> childs = new List<DirectoryInfo>();

                                List<FileInfo> files = new List<FileInfo>();
                                if (dir.Count > 0)
                                {
                                    foreach (var item in dir)
                                    {
                                        if (item.Folder == null || !item.Folder.Exists)
                                        {
                                            continue;
                                        }
                                        if (item.Type == MonitorInfoType.ChildFolder || item.Type == MonitorInfoType.All)
                                        {
                                            var child = item.Folder.GetDirectories();
                                            foreach (var chld in child)
                                            {
                                                if (fs.FindIndex(o => o.Folder.FullName == chld.FullName) != -1)
                                                {
                                                    continue;
                                                }

                                                if (chld.CreationTime <= lastTime)
                                                    childs.Add(chld);
                                            }
                                        }
                                        else
                                        {
                                            var filesTmp = item.Folder.GetFiles("*.*").Where(f => f.CreationTime <= lastTime).ToList();
                                            if (filesTmp.Count > 0)
                                            {
                                                files.AddRange(filesTmp);
                                            }

                                        }

                                    }
                                }

                                if (childs.Count > 0)
                                {
                                    foreach (var item in childs)
                                    {
                                        DeleteFolder(item, ds);
                                    }
                                }

                                if (files.Count > 0)
                                {
                                    foreach (var item in files)
                                    {
                                        DeleteFile(item, ds);
                                    }
                                }

                                if (NotEnough(ds))
                                {
                                    lastTime = DateTime.Now.AddMinutes(-30);
                                    CheckDiskVolume();
                                }


                            }
                        }
                    }
                }
            });
            isChecking = false;
            Console.WriteLine("EndCheck");
        }


        private bool NotEnough(DriveInfo driveInfo)
        {
            return driveInfo.IsReady && driveInfo.AvailableFreeSpace < (long)(1024 * 1024 * 1024 * 100d);
        }
        private bool DeleteFile(FileInfo item, DriveInfo driveInfo)
        {
            try
            {
                File.Delete(item.FullName);
            }
            catch
            {

            }

            return driveInfo.AvailableFreeSpace <= (long)(1024 * 1024 * 1024 * 40d);
        }

        /// <summary>
        /// 删除文件夹如果空间大于40就不做清除工作
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="driveInfo"></param>
        /// <returns></returns>
        private bool DeleteFolder(DirectoryInfo directory, DriveInfo driveInfo)
        {
            try
            {
                Console.WriteLine("Delete Folder " + directory.FullName);
                directory.Delete(true);
            }
            catch(Exception ex) 
            {

            }

            return driveInfo.AvailableFreeSpace <= (long)(1024 * 1024 * 1024 * 40d);
        }
        public void AddMonitorFolder(params string[] folders)
        {
            var drives = DriveInfo.GetDrives();
            if (folders != null && folders.Length > 0)
            {
                Regex regex = new Regex(@"\w+:", RegexOptions.IgnoreCase);

                foreach (string folder in folders)
                {

                    if (regex.IsMatch(folder))
                    {
                        string diskName = regex.Match(folder).Value;

                        DriveInfo drive = drives.FirstOrDefault((o) =>
                        {
                            return o.Name.ToLower().Contains(diskName.ToLower());
                        });
                        if (drive != null)
                        {

                            driveInfo[drive.Name] = drive;
                            var fs = monitorfolders.GetOrAdd(drive.Name, new ConcurrentDictionary<string, MonitorInfo>());

                            MonitorInfo info = new MonitorInfo { Folder = new DirectoryInfo(folder) };
                            MonitorInfo info2;
                            if (fs.TryGetValue(folder, out info2))
                            {
                                info.Type = info2.Type | MonitorInfoType.ChildFolder;
                            }
                            fs.GetOrAdd(folder, info);

                        }

                    }
                }

            }
          
        }

        public void AddMonitor(string folder, MonitorInfoType monitorInfo = MonitorInfoType.ChildFolder)
        {
            var drives = DriveInfo.GetDrives();
            Regex regex = new Regex(@"\w+:", RegexOptions.IgnoreCase);

            if (regex.IsMatch(folder))
            {
                string diskName = regex.Match(folder).Value;

                DriveInfo drive = drives.FirstOrDefault((o) =>
                {
                    return o.Name.ToLower().Contains(diskName.ToLower());
                });
                if (drive != null)
                {

                    driveInfo[drive.Name] = drive;
                    var fs = monitorfolders.GetOrAdd(drive.Name, new ConcurrentDictionary<string, MonitorInfo>());

                    MonitorInfo info = new MonitorInfo { Folder = new DirectoryInfo(folder), Type = monitorInfo };
                    MonitorInfo info2;
                    if (fs.TryGetValue(folder, out info2))
                    {
                        info.Type = info2.Type | monitorInfo;
                    }
                    fs.GetOrAdd(folder, info);

                }

            }
         
        }

    }
}
