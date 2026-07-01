using System;
using System.Runtime.InteropServices;

namespace SeedCut.Services.HM
{
    /// <summary>
    /// HM打标控制DLL的P/Invoke封装
    /// 
    /// 注意：此文件是C#封装类，实际调用的是 HM_HashuScan.dll（本地DLL）
    /// 请确保 HM_HashuScan.dll 和 HM_Comm.dll 放置在程序运行目录
    /// </summary>
    public static class HM_HashuScanDLL
    {
        private const string DLL_NAME = "HM_HashuScan.dll";

        #region 初始化与连接

        /// <summary>
        /// 初始化控制卡通讯
        /// 必须在UI线程调用（需要窗口句柄接收消息）
        /// </summary>
        /// <param name="hWnd">窗口句柄，用于接收DLL回调消息</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_InitBoard(IntPtr hWnd);

        /// <summary>
        /// 通过索引连接控制卡
        /// </summary>
        /// <param name="nIndex">设备索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_ConnectTo(int nIndex);

        /// <summary>
        /// 通过IP字符串连接控制卡
        /// </summary>
        /// <param name="pIp">IP地址，如 "172.18.34.227"</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int HM_ConnectByIpStr(string pIp);

        /// <summary>
        /// 断开连接
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_DisconnectTo(int ipIndex);

        /// <summary>
        /// 根据IP地址获取设备索引
        /// </summary>
        /// <param name="strIP">IP地址</param>
        /// <returns>设备索引，-1表示未找到</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int HM_GetIndexByIpAddr(string strIP);

        /// <summary>
        /// 获取连接状态
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>HM_DEV_Connect/HM_DEV_Ready/HM_DEV_NotAvailable</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetConnectStatus(int ipIndex);

        #endregion

        #region 文件下载

        /// <summary>
        /// 下载打标文件（异步，需等待HM_MSG_StreamEnd消息）
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="filePath">UDM文件路径</param>
        /// <param name="hWnd">接收消息的窗口句柄</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int HM_DownloadMarkFile(int ipIndex, string filePath, IntPtr hWnd);

        /// <summary>
        /// 下载打标文件（同步，阻塞直到完成）
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="filePath">UDM文件路径</param>
        /// <param name="hWnd">接收消息的窗口句柄</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int HM_DownloadMarkFileSyn(int ipIndex, string filePath, IntPtr hWnd);

        /// <summary>
        /// 通过内存缓冲区下载打标数据
        /// 配合 UDM_GetUDMBuffer 使用
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="pUDMBuff">UDM数据缓冲区指针</param>
        /// <param name="nBytesCount">缓冲区字节数</param>
        /// <param name="hWnd">接收消息的窗口句柄</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_DownloadMarkFileBuff(int ipIndex, IntPtr pUDMBuff, int nBytesCount, IntPtr hWnd);

        #endregion

        #region 打标控制

        /// <summary>
        /// 查询打标进度（调用后会收到HM_MSG_ExecProcess消息）
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>进度值 0~100</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_ExecuteProgress(int ipIndex);

        /// <summary>
        /// 开始打标
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_StartMark(int ipIndex);

        /// <summary>
        /// 停止打标
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_StopMark(int ipIndex);

        /// <summary>
        /// 暂停打标
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_PauseMark(int ipIndex);

        /// <summary>
        /// 继续打标
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_ContinueMark(int ipIndex);

        /// <summary>
        /// 获取工作状态
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>1=ready, 2=run, 3=alarm</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetWorkStatus(int ipIndex);

        #endregion

        #region 振镜控制

        /// <summary>
        /// 设置偏移量
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="offsetX">X偏移 (mm)</param>
        /// <param name="offsetY">Y偏移 (mm)</param>
        /// <param name="offsetZ">Z偏移 (mm)</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetOffset(int ipIndex, float offsetX, float offsetY, float offsetZ);

        /// <summary>
        /// 设置旋转
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="angle">旋转角度 (度)</param>
        /// <param name="centryX">旋转中心X (mm)</param>
        /// <param name="centryY">旋转中心Y (mm)</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetRotates(int ipIndex, float angle, float centryX, float centryY);

        /// <summary>
        /// 开关红光
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="enable">true=开启, false=关闭</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetGuidLaser(int ipIndex, bool enable);

        /// <summary>
        /// 振镜跳转到指定位置
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="X">X坐标 (mm)</param>
        /// <param name="Y">Y坐标 (mm)</param>
        /// <param name="Z">Z坐标 (mm)</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_ScannerJump(int ipIndex, float X, float Y, float Z);

        #endregion

        #region IO控制

        /// <summary>
        /// 设置控制卡Alarm输出信号状态
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="status">0=默认状态, 1=报警状态</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetBoardAlarmStatus(int ipIndex, int status);

        /// <summary>
        /// 设置坐标系
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="coordinate">坐标系索引 0~7</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetCoordinate(int ipIndex, int coordinate);

        /// <summary>
        /// 设置打标范围
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="region">打标范围</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetMarkRegion(int ipIndex, int region);

        /// <summary>
        /// 获取打标范围
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>打标范围值</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetMarkRegion(int ipIndex);

        /// <summary>
        /// 获取GMC2输入状态（返回值转二进制，每位对应一个输入）
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>输入状态位图</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetInput_GMC2(int ipIndex);

        /// <summary>
        /// 获取GMC4输入状态（返回值转二进制，每位对应一个输入）
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>输入状态位图</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetInput_GMC4(int ipIndex);

        /// <summary>
        /// 获取激光器报警状态
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>报警状态位图</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetLaserInput(int ipIndex);

        /// <summary>
        /// GMC2在线单独拉高指定输出信号
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetOutputOn_GMC2(int ipIndex, int nOutIndex);

        /// <summary>
        /// GMC2在线单独拉低指定输出信号
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetOutputOff_GMC2(int ipIndex, int nOutIndex);

        /// <summary>
        /// GMC4在线单独拉高指定输出信号
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetOutputOn_GMC4(int ipIndex, int nOutIndex);

        /// <summary>
        /// GMC4在线单独拉低指定输出信号
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetOutputOff_GMC4(int ipIndex, int nOutIndex);

        /// <summary>
        /// 设置两路模拟量输出
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="VoutA">通道A，0~1 对应 0~10V</param>
        /// <param name="VoutB">通道B，0~1 对应 0~10V</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetAnalog(int ipIndex, float VoutA, float VoutB);

        #endregion

        #region 位置反馈

        /// <summary>
        /// 获取XY位置反馈
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetFeedbackPosXY(int ipIndex, ref short fbX, ref short fbY);

        /// <summary>
        /// 获取XY指令位置
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetCmdPosXY(int ipIndex, ref short cmdX, ref short cmdY);

        /// <summary>
        /// 获取超范围报警信息
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>1=有超范围报警, 0=无</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetOverangeInfo(int ipIndex);

        /// <summary>
        /// 获取XY电机状态
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetXYGalvoStatus(int ipIndex, ref short xStatus, ref short yStatus);

        /// <summary>
        /// 获取Z电机状态
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetZGalvoStatus(int ipIndex, ref short zStatus);

        /// <summary>
        /// 清除闭环报警状态
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_ClearCloseLoopAlarm(int ipIndex);

        /// <summary>
        /// 获取振镜状态信息
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetGalvoStatusInfo(int ipIndex, int galvoType);

        #endregion

        #region 校正表

        /// <summary>
        /// 下载校正表到DDR
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int HM_DownloadCorrection(int ipIndex, string filePath, IntPtr hWnd);

        /// <summary>
        /// 固化校正表到Flash
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int HM_BurnCorrection(int ipIndex, string filePath, IntPtr hWnd);

        /// <summary>
        /// 切换校正表（一卡多方头时使用）
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="crtIndex">校正表索引 0 或 1</param>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SelectCorrection(int ipIndex, int crtIndex);

        #endregion

        #region 脱机打标

        /// <summary>
        /// 检查是否有SD卡
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>0=无SD卡, 1=有SD卡</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetSDCardFlag(int ipIndex);

        /// <summary>
        /// 设置脱机模式
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <param name="mode">1=单文档, 2=多文档(IO选择), 3=多文档按顺序循环</param>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetBurnMode(int ipIndex, int mode);

        /// <summary>
        /// 设置第几个脱机文件标志位
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetBurnIndex(int ipIndex, int udmIndex);

        /// <summary>
        /// 开始多文档固化
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_SetStartBurnFlag(int ipIndex);

        /// <summary>
        /// 判断是否固化完成
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetBurnOverFlag(int ipIndex);

        /// <summary>
        /// 脱机时固化打标文件
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_BurnMarkFile(int ipIndex, bool enable);

        /// <summary>
        /// 获取当前脱机文件个数
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int HM_GetBurnFileNum(int ipIndex);

        #endregion

        #region 辅助方法

        /// <summary>
        /// 检查DLL是否可用
        /// </summary>
        /// <returns>true=DLL可用, false=DLL不存在或无法加载</returns>
        public static bool IsDllAvailable()
        {
            try
            {
                // 尝试调用一个简单的函数来检测DLL是否存在
                HM_InitBoard(IntPtr.Zero);
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch
            {
                // 其他异常（如函数执行失败）说明DLL存在
                return true;
            }
        }

        /// <summary>
        /// 获取设备状态（带异常保护）
        /// </summary>
        /// <param name="ipIndex">设备索引</param>
        /// <returns>连接状态</returns>
        public static int HM_GetDeviceStatus(int ipIndex)
        {
            try
            {
                return HM_GetConnectStatus(ipIndex);
            }
            catch
            {
                return HM_Constants.HM_DEV_NotAvailable;
            }
        }

        /// <summary>
        /// 获取连接状态描述
        /// </summary>
        /// <param name="status">状态值</param>
        /// <returns>状态描述字符串</returns>
        public static string GetConnectStatusDescription(int status)
        {
            switch (status)
            {
                case HM_Constants.HM_DEV_Connect:
                    return "已连接";
                case HM_Constants.HM_DEV_Ready:
                    return "可连接";
                case HM_Constants.HM_DEV_NotAvailable:
                    return "不可用";
                default:
                    return string.Format("未知({0})", status);
            }
        }

        /// <summary>
        /// 获取工作状态描述
        /// </summary>
        /// <param name="status">工作状态值</param>
        /// <returns>工作状态描述</returns>
        public static string GetWorkStatusDescription(int status)
        {
            switch (status)
            {
                case HM_Constants.HM_WorkStatus_Ready:
                    return "就绪";
                case HM_Constants.HM_WorkStatus_Run:
                    return "运行中";
                case HM_Constants.HM_WorkStatus_Alarm:
                    return "报警";
                default:
                    return string.Format("未知({0})", status);
            }
        }

        #endregion
    }
}