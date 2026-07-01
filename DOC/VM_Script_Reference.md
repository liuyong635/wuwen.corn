# Vision Master 脚本模块快速参考

> 本文档为 VM 脚本开发的精简参考手册，涵盖脚本模块和全局脚本的核心用法。

---

## 一、脚本模块 vs 全局脚本

### 1.1 核心区别

| 特性 | 脚本模块 | 全局脚本 |
|------|----------|----------|
| **作用范围** | 单个流程内的数据处理 | 方案下所有流程的控制 |
| **调用位置** | 作为模块拖入流程中 | 快捷工具条 → 全局脚本图标 |
| **触发方式** | 随流程执行自动调用 | 点击"全流程执行"按钮触发 |
| **SDK调用** | ❌ 不支持 | ✅ 支持 |
| **输入输出变量** | ✅ 可视化配置 | ❌ 无（通过代码操作） |

### 1.2 使用场景选择

**必须用脚本模块的场景：**
- 流程内的数据格式转换、计算处理
- 需要将处理结果传递给后续模块
- 配合发送数据模块输出特定格式数据
- 硬触发/通信触发/全局触发的流程

**必须用全局脚本的场景：**
- 多流程协调控制（如：流程1和流程2都完成后再执行流程3）
- 需要调用 SDK 接口
- 方案加载完成后的初始化操作
- 注册流程执行完成的回调事件

**两者都可以的场景：**
- 全局变量的读写
- 通信设备数据发送
- 简单的数据处理逻辑

---

## 二、脚本模块

### 2.1 基本结构

```csharp
using System;
using System.Text;
using System.Windows.Forms;
using Script.Methods;

public partial class UserScript : ScriptMethods, IProcessMethods
{
    int processCount;  // 类成员变量

    /// <summary>
    /// 初始化方法 - 加载方案或预编译时执行
    /// </summary>
    public void Init()
    {
        processCount = 0;
        // 变量初始化、句柄创建等
    }

    /// <summary>
    /// 处理方法 - 每次流程执行时调用
    /// </summary>
    public bool Process()
    {
        // 业务逻辑代码
        return true;  // 返回true表示执行成功
    }
}
```

### 2.2 支持的数据类型

| 类型 | 说明 | 数组形式 |
|------|------|----------|
| `int` | 整型 | `int[]` |
| `float` | 浮点型 | `float[]` |
| `string` | 字符串 | `string[]` |
| `byte` | 字节/十六进制 | `byte[]` |
| `image` | 图像 | - |
| `ROIBOX` | ROI识别框 | `ROIBOX[]` |
| `POINT` | 点 | `POINT[]` |
| `LINE` | 线 | `LINE[]` |
| `RECT` | 矩形 | `RECT[]` |
| `ELLIPSE` | 椭圆 | - |
| `ANNULUS` | 圆环 | - |
| `POLYGON` | 多边形 | - |
| `FIXTURE` | 修正信息 | - |
| `pointset` | 点集 | - |

### 2.3 变量操作（推荐方式）

**直接使用变量名（推荐）：**

```csharp
public bool Process()
{
    // 直接读取输入变量
    int a = in0;           // int类型
    float f = in1;         // float类型
    string s = in2;        // string类型
    
    // 直接赋值输出变量
    out0 = a * 2;
    out1 = f + 1.5f;
    out2 = "Result: " + s;
    
    // 数组类型
    PointData[] points = in3;  // 输入点数组
    out3 = points;             // 输出点数组
    
    return true;
}
```

### 2.4 变量操作（兼容方式）

**使用 Get/Set 接口：**

```csharp
public bool Process()
{
    // === 基本类型 ===
    int intVal = 0;
    GetIntValue("in0", ref intVal);
    SetIntValue("out0", intVal);

    float floatVal = 0f;
    GetFloatValue("in1", ref floatVal);
    SetFloatValue("out1", floatVal);

    string strVal = "";
    GetStringValue("in2", ref strVal);
    SetStringValue("out2", strVal);

    // === 字节数据 ===
    byte[] bytes = new byte[] { };
    GetBytesValue("in3", ref bytes);
    SetBytesValue("out3", bytes);
    // 设置十六进制数据
    SetBytesValue("out3", new byte[] { 0x00, 0x02, 0xFF });

    // === 图像数据 ===
    ImageData imgData = new ImageData();
    GetImageValue("in4", ref imgData);
    SetImageValue("out4", imgData);

    // === ROIBOX数据 ===
    RoiboxData roiData = new RoiboxData();
    GetRoiboxValue("in5", ref roiData);
    SetRoiboxValue("out5", roiData);

    // === 数组类型 ===
    int count = 0;
    
    int[] intArr = new int[10];
    GetIntArrayValue("in6", ref intArr, out count);
    SetIntArrayValue("out6", intArr, 0, intArr.Length);

    float[] floatArr = new float[10];
    GetFloatArrayValue("in7", ref floatArr, out count);
    SetFloatArrayValue("out7", floatArr, 0, floatArr.Length);

    string[] strArr = new string[10];
    GetStringArrayValue("in8", ref strArr, out count);
    SetStringArrayValue("out8", strArr, 0, strArr.Length);

    // ROIBOX数组
    RoiboxData[] roiArr = new RoiboxData[100];
    int boxCount = 0;
    GetRoiBoxArrayValue("in9", ref roiArr, out boxCount);
    SetRoiBoxArrayValue("out9", roiArr, 0, boxCount);

    // === 按索引设置数组元素 ===
    SetIntValueByIndex("out6", 100, 0, 5);      // 索引0，共5个元素
    SetFloatValueByIndex("out7", 3.14f, 1, 5);  // 索引1，共5个元素
    SetStringValueByIndex("out8", "test", 2, 5); // 索引2，共5个元素

    return true;
}
```

### 2.5 全局变量操作

```csharp
// 设置全局变量
GlobalVariableModule.SetValue("varName", "value");

// 获取全局变量（返回object类型）
object val = GlobalVariableModule.GetValue("varName");
string strVal = val?.ToString();
```

### 2.6 获取其他模块的结果

```csharp
// 获取模块结果（模块名从流程中查看）
object result = CurrentProcess.GetModule("图像源1").GetValue("Height");
object width = CurrentProcess.GetModule("图像源1").GetValue("Width");

// 如果模块在Group中，需要加Group名称
object result2 = CurrentProcess.GetModule("组合模块1.图像源1").GetValue("Height");
```

### 2.7 设置其他模块的参数

```csharp
// 设置模块运行参数
CurrentProcess.GetModule("BLOB分析1").SetValue("FindNum", "4");

// 如果模块在Group中
CurrentProcess.GetModule("组合模块1.BLOB分析1").SetValue("FindNum", "4");
```

### 2.8 通信数据发送

```csharp
// === TCP/UDP/串口 发送数据 ===
// deviceID: 通信管理中的设备ID
GlobalCommunicateModule.GetDevice(1).SendData("message", DataType.StringType);
GlobalCommunicateModule.GetDevice(1).SendData(new byte[] { 0x01, 0x02 }); // 十六进制

// === PLC/Modbus 发送数据 ===
// deviceID: 设备ID, addressID: 地址ID
GlobalCommunicateModule.GetDevice(2).GetAddress(1).SendData("100", DataType.IntType);
GlobalCommunicateModule.GetDevice(2).GetAddress(1).SendData("3.14", DataType.FloatType);
GlobalCommunicateModule.GetDevice(2).GetAddress(1).SendData("text", DataType.StringType);
```

### 2.9 点集数据转换

```csharp
// 二进制 → 轮廓点数组
byte[] inBytes = new byte[] { };
GetBytesValue("in0", ref inBytes);
ContourPointData[] contourPoints = null;
int ret = BytesToPointset(inBytes, ref contourPoints);

// 轮廓点数组 → 二进制
byte[] outBytes = PointsetToBytes(contourPoints);
SetBytesValue("out0", outBytes);
```

### 2.10 调试方法

```csharp
// 打印到DebugView
ConsoleWrite("调试信息: " + value.ToString());

// 弹窗提示（用于异常捕获）
try
{
    // 业务代码
}
catch (Exception ex)
{
    ShowMessageBox(ex.ToString());
}
```

---

## 三、全局脚本

### 3.1 基本结构

```csharp
using System;
using VM.GlobalScript.Methods;
using VM.Core;
using VM.PlatformSDKCS;
using iMVS_6000PlatformSDKCS;

public class UserGlobalScript : UserGlobalMethods, IScriptMethods
{
    /// <summary>
    /// 初始化 - 加载方案或预编译时执行
    /// </summary>
    public int Init()
    {
        int ret = InitSDK();
        return ret;
    }

    /// <summary>
    /// 方案加载完成后执行
    /// </summary>
    public override int InitAfterLoadSol()
    {
        // 方案加载完成后的初始化操作
        return 0;
    }

    /// <summary>
    /// 处理方法 - 点击"全流程执行"时调用
    /// </summary>
    public int Process()
    {
        if (m_operateHandle == IntPtr.Zero)
            return ImvsSdkPFDefine.IMVS_EC_NULL_PTR;

        // 默认执行全部流程
        int nRet = DefaultExecuteProcess();
        return nRet;
    }

    /// <summary>
    /// 释放资源 - 关闭程序或重新编译时
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        // 释放自定义资源
    }
}
```

### 3.2 全局变量操作

```csharp
// 设置全局变量
SetGlobalVariableIntValue("intVar", 100);
SetGlobalVariableFloatValue("floatVar", 3.14f);
SetGlobalVariableStringValue("strVar", "hello");

// 获取全局变量
int intVal = 0;
GetGlobalVariableIntValue("intVar", ref intVal);

float floatVal = 0f;
GetGlobalVariableFloatValue("floatVar", ref floatVal);

string strVal = "";
GetGlobalVariableStringValue("strVar", ref strVal);
```

### 3.3 流程控制（SDK调用）

```csharp
// 获取流程对象
VmProcedure pro1 = (VmProcedure)VmSolution.Instance["流程1"];
VmProcedure pro2 = (VmProcedure)VmSolution.Instance["流程2"];

// 同步执行流程
pro1.Run();

// 异步执行流程
pro1.Run("", false);
```

### 3.4 流程执行完成回调

```csharp
private VmProcedure pro1 = null;
private VmProcedure pro2 = null;
private bool pro1Done = false;
private bool pro2Done = false;

public override int InitAfterLoadSol()
{
    pro1 = (VmProcedure)VmSolution.Instance["流程1"];
    pro2 = (VmProcedure)VmSolution.Instance["流程2"];
    
    // 注册回调
    if (pro1 != null) pro1.OnWorkEndStatusCallBack += OnWorkEnd;
    if (pro2 != null) pro2.OnWorkEndStatusCallBack += OnWorkEnd;
    
    return 0;
}

private void OnWorkEnd(object sender, EventArgs e)
{
    if (e == null) return;
    
    ValueEventArgs args = (ValueEventArgs)e;
    var status = (ImvsSdkDefine.IMVS_MODULE_WORK_STAUS)args.Value;
    
    // 根据流程ID判断（流程ID在VM界面查看）
    if (status.nProcessID == 10000) pro1Done = true;
    if (status.nProcessID == 10001) pro2Done = true;
    
    // 两个流程都完成后执行其他操作
    if (pro1Done && pro2Done)
    {
        VmProcedure pro3 = (VmProcedure)VmSolution.Instance["流程3"];
        pro3?.Run("", false);
        pro1Done = false;
        pro2Done = false;
    }
}

public override void Dispose()
{
    base.Dispose();
    if (pro1 != null) pro1.OnWorkEndStatusCallBack -= OnWorkEnd;
    if (pro2 != null) pro2.OnWorkEndStatusCallBack -= OnWorkEnd;
}
```

### 3.5 通信数据发送

```csharp
// TCP/UDP/串口
SendCommDeviceData("message", 1);  // deviceID=1
SendCommDeviceData(new byte[] { 0x01, 0x02 }, 1);

// PLC/Modbus
SendCommDeviceData("100", 2, 1, DataType.IntType);    // deviceID=2, addressID=1
SendCommDeviceData("3.14", 2, 1, DataType.FloatType);
```

### 3.6 通信数据接收

```csharp
public int Init()
{
    InitSDK();
    StartGlobalCommunicate();           // 初始化通信端口
    RegesiterReceiveCommunicateDataEvent();  // 注册接收事件
    return 0;
}

// 接收回调
void UserGlobalMethods_OnReceiveCommunicateDataEvent(ReceiveDataInfo dataInfo)
{
    // dataInfo.communicateType - 通信类型（TCP/UDP/串口/PLC/Modbus）
    // dataInfo.DeviceID - 设备ID
    // dataInfo.DeviceAddressID - 地址ID（PLC/Modbus）
    // dataInfo.DeviceData - 接收的数据（byte[]）
    
    string received = System.Text.Encoding.UTF8.GetString(dataInfo.DeviceData);
    ConsoleWrite("收到数据: " + received);
}

public override void Dispose()
{
    base.Dispose();
    UnRegesiterReceiveCommunicateDataEvent();  // 注销接收事件
}
```

### 3.7 连续执行间隔设置

```csharp
// 设置Process连续执行的时间间隔（毫秒）
SetScriptContinuousExecuteInterval(1000);  // 1秒

// 获取当前间隔
uint interval = GetScriptContinuousExecuteInterval();
```

---

## 四、VS调试方法

### 4.1 脚本模块调试

1. 脚本编辑窗口 → **导出工程**
2. VS打开 `.sln` 文件
3. 菜单：**生成 → Build**
4. 菜单：**调试 → 附加到进程** → 选择 VM 主进程
5. 如报错"已附加调试器"：
   - 方法A：任务管理器结束 `vServerApp.exe`，重新附加
   - 方法B：修改 `vServerApp.exe.config` 中 `DumpEnable` 为 `false`
6. 设置断点，在 VM 中执行流程

### 4.2 全局脚本调试

1. 全局脚本窗口 → **打开工程目录**
2. VS打开 `.sln` 文件
3. 菜单：**调试 → 附加到进程** → 选择 VM 主进程
4. 设置断点，在 VM 中点击"全流程执行"

---

## 五、常用代码模板

### 5.1 数据透传模板

```csharp
public bool Process()
{
    // 将输入直接传递到输出
    int intVal = 0;
    GetIntValue("in0", ref intVal);
    SetIntValue("out0", intVal);

    float floatVal = 0f;
    GetFloatValue("in1", ref floatVal);
    SetFloatValue("out1", floatVal);

    string strVal = "";
    GetStringValue("in2", ref strVal);
    SetStringValue("out2", strVal);

    ImageData imgData = new ImageData();
    GetImageValue("in3", ref imgData);
    SetImageValue("out3", imgData);

    return true;
}
```

### 5.2 异常处理模板

```csharp
public bool Process()
{
    try
    {
        // 业务逻辑
        int a = in0;
        out0 = a * 2;
        return true;
    }
    catch (Exception ex)
    {
        ConsoleWrite("脚本异常: " + ex.Message);
        ShowMessageBox(ex.ToString());
        return false;
    }
}
```

### 5.3 条件判断输出模板

```csharp
public bool Process()
{
    float value = 0f;
    GetFloatValue("in0", ref value);
    
    string result = "";
    if (value > 100)
        result = "OK";
    else if (value > 50)
        result = "NG";
    else
        result = "FAIL";
    
    SetStringValue("out0", result);
    return true;
}
```

### 5.4 数组处理模板

```csharp
public bool Process()
{
    int count = 0;
    float[] values = new float[100];
    GetFloatArrayValue("in0", ref values, out count);
    
    // 计算平均值
    float sum = 0;
    for (int i = 0; i < count; i++)
    {
        sum += values[i];
    }
    float avg = count > 0 ? sum / count : 0;
    
    SetFloatValue("out0", avg);
    return true;
}
```

### 5.5 字符串拼接模板

```csharp
public bool Process()
{
    float x = 0f, y = 0f, angle = 0f;
    GetFloatValue("in_x", ref x);
    GetFloatValue("in_y", ref y);
    GetFloatValue("in_angle", ref angle);
    
    // 格式化输出
    string result = string.Format("{0:F2},{1:F2},{2:F2}", x, y, angle);
    // 或使用分隔符
    string result2 = x.ToString("F2") + ";" + y.ToString("F2") + ";" + angle.ToString("F2");
    
    SetStringValue("out0", result);
    return true;
}
```

---

## 六、注意事项

1. **变量名唯一性**：输入输出变量名称不要重复
2. **数组初始化**：使用 `GetXxxArrayValue` 前需先初始化数组
3. **Group中的模块**：访问时需加 Group 名称前缀，如 `"组合模块1.图像源1"`
4. **返回值检查**：Get/Set 方法返回 0 表示成功，非 0 表示失败
5. **全局脚本限制**：无法控制硬触发、通信触发、全局触发的流程
6. **脚本模块限制**：不支持调用 SDK 接口
7. **【规范】可调参数使用全局变量管理**：脚本中的阈值、配置参数等可调常量，**不应硬编码为 `const`**，而应存放在 VM 全局变量中，脚本运行时通过 `GlobalVariableModule.GetValue()` 读取。这样做的好处是：调参时无需修改和重新编译脚本，直接在 VM 界面修改全局变量即可生效；多个脚本可共享同一套参数；方便在不同产线/品种间切换配置。示例：
   ```csharp
   // ✅ 推荐：从全局变量读取参数（带默认值兜底）
   float threshold = 1.6f;
   object val = GlobalVariableModule.GetValue("CompactnessMax");
   if (val != null) float.TryParse(val.ToString(), out threshold);
   
   // ❌ 不推荐：硬编码在脚本中
   const float COMPACTNESS_MAX = 1.6f;
   ```
8. **【规范】输入输出变量使用语义化英文命名**：脚本模块的输入输出变量**不应使用 `in0`/`out1` 等序号命名**，而应使用有明确含义的英文名称，便于在流程中快速识别数据用途和连线关系。命名建议采用 camelCase 风格，数组变量加 `Arr` 后缀。示例：
   ```
   ✅ 推荐：blobCount, centerXArr, validAreaArr, debugInfo
   ❌ 不推荐：in0, in1, out0, out1
   ```
   > **注意**：变量名在 VM 脚本模块的输入输出配置面板中设置，设置后在 Get/Set 接口和直接变量访问中使用相同名称。

---

## 七、接口速查表

### 脚本模块接口

| 接口 | 说明 |
|------|------|
| `GetIntValue` / `SetIntValue` | int 类型读写 |
| `GetFloatValue` / `SetFloatValue` | float 类型读写 |
| `GetStringValue` / `SetStringValue` | string 类型读写 |
| `GetBytesValue` / `SetBytesValue` | byte[] 类型读写 |
| `GetImageValue` / `SetImageValue` | 图像数据读写 |
| `GetRoiboxValue` / `SetRoiboxValue` | ROIBOX 数据读写 |
| `GetIntArrayValue` / `SetIntArrayValue` | int[] 数组读写 |
| `GetFloatArrayValue` / `SetFloatArrayValue` | float[] 数组读写 |
| `GetStringArrayValue` / `SetStringArrayValue` | string[] 数组读写 |
| `SetIntValueByIndex` | 按索引设置 int 数组元素 |
| `SetFloatValueByIndex` | 按索引设置 float 数组元素 |
| `SetStringValueByIndex` | 按索引设置 string 数组元素 |
| `GlobalVariableModule.GetValue` | 获取全局变量 |
| `GlobalVariableModule.SetValue` | 设置全局变量 |
| `CurrentProcess.GetModule().GetValue()` | 获取模块结果 |
| `CurrentProcess.GetModule().SetValue()` | 设置模块参数 |
| `GlobalCommunicateModule.GetDevice().SendData()` | 发送通信数据 |
| `ConsoleWrite` | 打印到 DebugView |
| `ShowMessageBox` | 弹窗提示 |

### 全局脚本接口

| 接口 | 说明 |
|------|------|
| `Init` | 初始化 |
| `Process` | 处理逻辑 |
| `InitAfterLoadSol` | 方案加载完成后初始化 |
| `Dispose` | 释放资源 |
| `GetGlobalVariableIntValue` / `SetGlobalVariableIntValue` | int 全局变量 |
| `GetGlobalVariableFloatValue` / `SetGlobalVariableFloatValue` | float 全局变量 |
| `GetGlobalVariableStringValue` / `SetGlobalVariableStringValue` | string 全局变量 |
| `SendCommDeviceData` | 发送通信数据 |
| `StartGlobalCommunicate` | 初始化通信端口 |
| `RegesiterReceiveCommunicateDataEvent` | 注册通信接收事件 |
| `UnRegesiterReceiveCommunicateDataEvent` | 注销通信接收事件 |
| `SetScriptContinuousExecuteInterval` | 设置连续执行间隔 |
| `ConsoleWrite` | 打印到 DebugView |
