using System;
using System.Runtime.InteropServices;

namespace SeedCut.Services.HM
{
    /// <summary>
    /// UDM文件生成DLL的P/Invoke封装
    /// 
    /// 注意：UDM函数也在 HM_HashuScan.dll 中，不是单独的DLL
    /// 
    /// UDM生成流程：
    /// 1. UDM_NewFile()          - 新建文件
    /// 2. UDM_Main()             - 开始主程序
    /// 3. UDM_SetProtocol()      - 设置协议
    /// 4. UDM_SetLayersPara()    - 设置图层参数
    /// 5. UDM_AddPolyline2D()    - 添加图形
    /// 6. UDM_Jump(0,0,0)        - 回零（可选）
    /// 7. UDM_EndMain()          - 结束主程序
    /// 8. UDM_SaveToFile() 或 UDM_GetUDMBuffer() - 保存/获取数据
    /// </summary>
    public static class HM_UDM_DLL
    {
        private const string DLL_NAME = "HM_HashuScan.dll";

        #region 文件操作

        /// <summary>
        /// 新建UDM文件
        /// 必须在生成任何图形之前调用
        /// </summary>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_NewFile();

        /// <summary>
        /// 保存UDM文件到磁盘
        /// </summary>
        /// <param name="strFilePath">文件保存路径</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int UDM_SaveToFile(string strFilePath);

        /// <summary>
        /// 获取UDM数据的内存缓冲区
        /// 配合 HM_DownloadMarkFileBuff 使用
        /// </summary>
        /// <param name="pUdmBuffer">输出：缓冲区指针</param>
        /// <param name="nBytesCount">输出：缓冲区字节数</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_GetUDMBuffer(ref IntPtr pUdmBuffer, ref int nBytesCount);

        #endregion

        #region 程序结构

        /// <summary>
        /// 开始主程序
        /// 必须在 UDM_NewFile 之后调用
        /// </summary>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_Main();

        /// <summary>
        /// 结束主程序
        /// 必须在所有图形添加完成后调用
        /// </summary>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_EndMain();

        /// <summary>
        /// 设置通讯协议
        /// </summary>
        /// <param name="nProtocol">协议类型：0=SPI, 1=XY2-100, 2=SL2</param>
        /// <param name="nDimensional">维度：0=2D, 1=3D</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetProtocol(int nProtocol, int nDimensional);

        /// <summary>
        /// 设置图层参数
        /// </summary>
        /// <param name="layersParameter">图层参数数组</param>
        /// <param name="count">图层数量</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetLayersPara(
            [MarshalAs(UnmanagedType.LPArray)] MarkParameter[] layersParameter,
            int count);

        #endregion

        #region 控制指令

        /// <summary>
        /// 循环开始
        /// 注意：不支持嵌套循环
        /// </summary>
        /// <param name="repeatCount">重复次数</param>
        /// <returns>起始地址（用于 UDM_RepeatEnd）</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_RepeatStart(int repeatCount);

        /// <summary>
        /// 循环结束
        /// </summary>
        /// <param name="startAddress">UDM_RepeatStart 返回的起始地址</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_RepeatEnd(int startAddress);

        /// <summary>
        /// 跳转指令（不出光移动）
        /// </summary>
        /// <param name="x">X坐标 (mm)</param>
        /// <param name="y">Y坐标 (mm)</param>
        /// <param name="z">Z坐标 (mm)</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_Jump(float x, float y, float z);

        /// <summary>
        /// 等待指定时间
        /// </summary>
        /// <param name="msTime">等待时间 (ms)</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_Wait(float msTime);

        /// <summary>
        /// 开关红光（写入UDM文件）
        /// </summary>
        /// <param name="enable">true=开, false=关</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetGuidLaser(bool enable);

        /// <summary>
        /// 等待输入信号
        /// 相应输入信号触发后才继续往下执行，否则一直等待
        /// </summary>
        /// <param name="uInIndex">输入索引 (0~7)</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetInput(uint uInIndex);

        /// <summary>
        /// 脚踏触发（等待IN0）
        /// </summary>
        /// <param name="nDelayTime">触发后延时多久开始打标 (ms)</param>
        /// <param name="nTriggerType">触发类型：0=上升沿触发, 1=电平触发</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_FootTrigger(uint nDelayTime, int nTriggerType);

        #endregion

        #region IO输出 - GMC2

        /// <summary>
        /// 一键全部控制输出信号（写入UDM文件）
        /// 二进制1111代表四个输出全部拉高，0011代表out0、out1拉高，out2/out3拉低
        /// </summary>
        /// <param name="uData">输出数据位图</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetOutPutAll(uint uData);

        /// <summary>
        /// 单独拉高输出信号（写入UDM文件）
        /// </summary>
        /// <param name="nOutIndex">输出索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetOutPutOn(uint nOutIndex);

        /// <summary>
        /// 单独拉低输出信号（写入UDM文件）
        /// </summary>
        /// <param name="nOutIndex">输出索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetOutPutOff(uint nOutIndex);

        #endregion

        #region IO输出 - GMC4

        /// <summary>
        /// GMC4单独拉高输出信号（写入UDM文件）
        /// </summary>
        /// <param name="nOutIndex">输出索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetOutputOn_GMC4(uint nOutIndex);

        /// <summary>
        /// GMC4单独拉低输出信号（写入UDM文件）
        /// </summary>
        /// <param name="nOutIndex">输出索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetOutputOff_GMC4(uint nOutIndex);

        #endregion

        #region 模拟量与坐标变换

        /// <summary>
        /// 设置两路模拟量输出（写入UDM文件）
        /// </summary>
        /// <param name="VoutA">通道A输出，0=0V, 0.5=5V, 1=10V</param>
        /// <param name="VoutB">通道B输出，0=0V, 0.5=5V, 1=10V</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetAnalogValue(float VoutA, float VoutB);

        /// <summary>
        /// 所有点坐标平移（写入UDM文件）
        /// </summary>
        /// <param name="offsetX">X偏移 (mm)</param>
        /// <param name="offsetY">Y偏移 (mm)</param>
        /// <param name="offsetZ">Z偏移 (mm)</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetOffset(float offsetX, float offsetY, float offsetZ);

        /// <summary>
        /// 旋转变换（写入UDM文件）
        /// </summary>
        /// <param name="angle">旋转角度 (度)</param>
        /// <param name="centryX">旋转中心X (mm)</param>
        /// <param name="centryY">旋转中心Y (mm)</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetRotate(float angle, float centryX, float centryY);

        #endregion

        #region 图形添加 - 2D

        /// <summary>
        /// 添加2D折线/多边形（最常用）
        /// </summary>
        /// <param name="nPos">坐标点数组</param>
        /// <param name="nCount">点数量（至少2个点）</param>
        /// <param name="layerIndex">图层索引（从0开始）</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_AddPolyline2D(
            [MarshalAs(UnmanagedType.LPArray)] structUdmPos[] nPos,
            int nCount,
            int layerIndex);

        /// <summary>
        /// 添加2D圆弧
        /// </summary>
        /// <param name="stCenter">圆心坐标</param>
        /// <param name="radius">半径 (mm)</param>
        /// <param name="startAngle">起始角度 (度)</param>
        /// <param name="sweepAngle">扫描角度 (度)，正数逆时针，负数顺时针</param>
        /// <param name="layerIndex">图层索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_AddArc2D(
            structUdmPos stCenter,
            float radius,
            float startAngle,
            float sweepAngle,
            int layerIndex);

        /// <summary>
        /// 添加2D点
        /// </summary>
        /// <param name="pos">点坐标</param>
        /// <param name="time">出光时间 (ms)</param>
        /// <param name="layerIndex">图层索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_AddPoint2D(
            structUdmPos pos,
            float time,
            int layerIndex);

        /// <summary>
        /// 添加带功率控制的2D点
        /// </summary>
        /// <param name="pos">点坐标</param>
        /// <param name="time">出光时间 (ms)，最大时长2147483ms</param>
        /// <param name="power">功率百分比</param>
        /// <param name="layerIndex">图层索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_AddPoint2DPower(
            structUdmPos pos,
            float time,
            float power,
            int layerIndex);

        #endregion

        #region 图形添加 - 3D

        /// <summary>
        /// 添加3D折线/多边形
        /// </summary>
        /// <param name="nPos">坐标点数组</param>
        /// <param name="nCount">点数量</param>
        /// <param name="layerIndex">图层索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_AddPolyline3D(
            [MarshalAs(UnmanagedType.LPArray)] structUdmPos[] nPos,
            int nCount,
            int layerIndex);

        /// <summary>
        /// 添加带断点校正的3D折线
        /// </summary>
        /// <param name="nPos">坐标点数组</param>
        /// <param name="nCount">点数量</param>
        /// <param name="p2pGap">点间距</param>
        /// <param name="layerIndex">图层索引</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_AddBreakAndCorPolyline3D(
            [MarshalAs(UnmanagedType.LPArray)] structUdmPos[] nPos,
            int nCount,
            float p2pGap,
            int layerIndex);

        #endregion

        #region 高级功能

        /// <summary>
        /// 设置闭环控制
        /// </summary>
        /// <param name="enable">是否启用</param>
        /// <param name="galvoType">振镜类型</param>
        /// <param name="followErrorMax">最大跟随误差</param>
        /// <param name="followErrorCount">跟随误差计数</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetCloseLoop(bool enable, int galvoType, int followErrorMax, int followErrorCount);

        /// <summary>
        /// 设置3D校正表参数
        /// </summary>
        /// <param name="baseFocal">基准焦距</param>
        /// <param name="paraK">参数K数组</param>
        /// <param name="nCount">参数数量</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_Set3dCorrectionPara(
            float baseFocal,
            [MarshalAs(UnmanagedType.LPArray)] double[] paraK,
            int nCount);

        /// <summary>
        /// 获取Z值
        /// </summary>
        /// <param name="x">X坐标</param>
        /// <param name="y">Y坐标</param>
        /// <param name="height">高度</param>
        /// <returns>Z值</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_GetZvalue(float x, float y, float height);

        /// <summary>
        /// 设置SkyWriting模式
        /// </summary>
        /// <param name="enable">0=关闭, 1=启用</param>
        /// <param name="mode">模式</param>
        /// <param name="uniformLen">均匀长度</param>
        /// <param name="accLen">加速长度</param>
        /// <param name="angleLimit">角度限制</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetSkyWritingMode(int enable, int mode, float uniformLen, float accLen, float angleLimit);

        /// <summary>
        /// 设置SkyWriting模式（RTC5兼容）
        /// </summary>
        /// <param name="mode">0=关闭, >0=开启</param>
        /// <param name="nPrev">前置参数</param>
        /// <param name="nPost">后置参数</param>
        /// <param name="angleLimit">角度限制</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetSkyWritingModeRTC5(int mode, int nPrev, int nPost, float angleLimit);

        /// <summary>
        /// 设置跳转延长
        /// </summary>
        /// <param name="jumpExtendLen">跳转延长值 (mm)</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetJumpExtendLen(float jumpExtendLen);

        /// <summary>
        /// 设置首脉冲
        /// </summary>
        /// <param name="enable">是否启用</param>
        /// <param name="firstDutyCycle">首脉冲占空比</param>
        /// <param name="incrementDutyCycle">递增占空比</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetFirstPulse(bool enable, float firstDutyCycle, float incrementDutyCycle);

        /// <summary>
        /// 设置可变拐弯延时
        /// 拐弯延时随角度变化
        /// </summary>
        /// <param name="enable">1=启用, 0=禁用</param>
        /// <param name="laserOffEnable">1=拐弯延时大于EdgeLevel时关光</param>
        /// <param name="EdgeLevel">边缘电平阈值 (us)</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetVariablePolygonDelay(int enable, int laserOffEnable, int EdgeLevel);

        /// <summary>
        /// 设置可变跳转延时
        /// </summary>
        /// <param name="jumpDelayMinTime">最小跳转延时</param>
        /// <param name="jumpMinLen">最小跳转长度</param>
        /// <returns>HM_OK=成功, HM_FAILED=失败</returns>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int UDM_SetVariableJumpDelay(int jumpDelayMinTime, float jumpMinLen);

        #endregion

        #region 辅助方法

        /// <summary>
        /// 检查DLL是否可用
        /// </summary>
        public static bool IsDllAvailable()
        {
            try
            {
                // 不要调用 UDM_NewFile()，而是尝试加载 DLL 入口点
                // 通过获取函数指针来检查，而不是实际调用
                var handle = LoadLibrary(DLL_NAME);
                if (handle == IntPtr.Zero)
                    return false;

                var addr = GetProcAddress(handle, "UDM_NewFile");
                FreeLibrary(handle);
                return addr != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);
        #endregion
    }
}