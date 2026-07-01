# 振动盘自动排料需求文档 v3.1

## 需求背景

当振动盘中的种子出现粘连、堆积等异常情况时，持续振动无法产生可抓取的料，此时需要自动触发排料流程，将振动盘中的料全部排空，然后重新上料。

## 架构设计

### 职责划分

| 层级 | 职责 | 实现位置 |
|------|------|---------|
| **VM 流程** | 图像分析，输出白色占比数据和坐标超限检测 | VM 脚本2 + 脚本3 |
| **C# Handler** | 业务逻辑：计数器、排料决策、振动+抖料策略、排料流程、坐标超限报警 | DiskVisionHandler |
| **配方参数** | 所有阈值和时间参数，包括坐标安全范围 | RecipeService |
| **振动盘软件** | 抖料持续时间参数 | 振动盘配套软件 |

### 核心原则

1. **VM 流程只负责图像分析**：计算白色占比和坐标超限检测，不做业务决策
2. **C# Handler 负责业务逻辑**：维护计数器、判断是否排料、执行振动+抖料策略、处理坐标超限报警
3. **配方参数控制所有阈值**：白色占比阈值、空振次数阈值、排料时间、坐标安全范围等
4. **抖料只发送 Modbus 指令**：抖料时间在振动盘软件中配置，C# 只负责触发
5. **坐标超限预防**：VM脚本3在标定转换后检测坐标是否超出机器人安全空间，防止超限报警

## 核心概念说明

### 振动盘相关操作

1. **振动1（打散振动）**：短时间振动，用于打散振动盘中堆积的种子，使其分散开
   - **接口方法**：`StartVibrationAsync()` / `StopVibrationAsync()`
2. **振动2（排料振动）**：持续振动，配合挡料板打开，将振动盘中的料全部排空
   - **接口方法**：`StartVibrationGroup2Async()` / `StopVibrationGroup2Async()`
3. **抖料**：振动盘上方出料盒的操作，通过抖动使振动盘出料口增加几颗料，补充可抓的料
   - **实现方式**：通过 Modbus 发送指令到振动盘
   - **时间参数**：在振动盘配套软件中配置，C# 代码不需要传递时间参数
   - **接口方法**：`FeedOnceAsync()`

### 挡料板

- 挡料板位于振动盘出料口
- 正常情况下挡料板关闭，防止料掉落
- 排料时打开挡料板，配合振动2将料全部排空

## 完整业务流程

### 1. 主流程概览

```
┌─────────────────────────────────────────────────────────────┐
│                    DiskVisionHandler 主循环                   │
└─────────────────────────────────────────────────────────────┘
                            ↓
                    ┌───────────────┐
                    │  VM 视觉检测   │
                    │ - 种子计数     │
                    │ - 白色占比     │
                    └───────────────┘
                            ↓
                    ┌───────────────┐
                    │  有料？       │
                    └───────────────┘
                    ↙              ↘
            【有料分支】          【无料分支】
                ↓                      ↓
        重置所有计数器          进入无料处理流程
        返回成功                      ↓
                            ┌─────────────────┐
                            │ 排料决策判断     │
                            │(空振次数+白色占比)│
                            └─────────────────┘
                                    ↓
                            ┌─────────────────┐
                            │ 振动+抖料策略    │
                            │ (先振动后抖料)   │
                            └─────────────────┘
                                    ↓
                            ┌─────────────────┐
                            │ 排料流程执行     │
                            │ (挡料板+振动2)   │
                            └─────────────────┘
```

### 2. 无料处理流程（核心逻辑）

```
┌──────────────────────────────────────────────────────────────┐
│                        检测到无料                              │
└──────────────────────────────────────────────────────────────┘
                            ↓
                ┌───────────────────────┐
                │ 空振次数 >= 阈值？     │
                │ (_emptyCounter >= 5)  │
                └───────────────────────┘
                    ↙              ↘
                NO                  YES
                ↓                    ↓
    ┌───────────────────────┐  ┌─────────────────┐
    │ 振动计数器 >= 1？      │  │ 白色占比 > 阈值？│
    │ (_vibrateCounter >= 1)│  │ (默认 50%)       │
    └───────────────────────┘  └─────────────────┘
        ↙              ↘              ↙          ↘
    NO                  YES        NO              YES
    ↓                    ↓          ↓                ↓
┌─────────────┐  ┌─────────────┐  重置空振计数器  ┌──────────────┐
│ 只振动       │  │ 白色占比检查 │  (误判，无堆积)  │ 触发排料流程  │
│ (不抖料)     │  │             │                  └──────────────┘
└─────────────┘  └─────────────┘
    ↓                    ↓
振动计数器+1      白色占比 > 阈值？
_vibrateCounter++     ↙          ↘
    ↓              YES            NO
    │               ↓              ↓
    │        ┌─────────────┐  ┌─────────────┐
    │        │ 只振动       │  │ 振动+抖料    │
    │        │ (不抖料)     │  │             │
    │        └─────────────┘  └─────────────┘
    │               ↓              ↓
    │        保持振动计数器  重置振动计数器
    │        _vibrateCounter=1 _vibrateCounter=0
    │               ↓              ↓
    │        空振计数器+1    空振计数器+1
    │        _emptyCounter++ _emptyCounter++
    ↓               ↓              ↓
    └───────────────┴──────────────┘
                    ↓
    等待下次检测（本次Handler结束）
```

**计数器含义说明：**
- `_vibrateCounter`：单独振动的次数（不抖料），用于判断是否需要抖料
  - 第1次无料：只振动，`_vibrateCounter = 1`
  - 第2次无料：振动+抖料，`_vibrateCounter = 0`
  - **交替策略设计意图**：避免连续抖料导致料盘堆积，给料盘更多时间自然分散
- `_emptyCounter`：执行振动+抖料的累计次数，用于判断是否需要排料
  - 每次执行振动+抖料后，`_emptyCounter++`
  - 达到阈值（默认5次）后，下次检测无料时检查白色占比，决定是否触发排料

### 3. 排料流程详细步骤

```
┌──────────────────────────────────────────────────────────────┐
│                        触发排料流程                            │
└──────────────────────────────────────────────────────────────┘
                            ↓
                ┌───────────────────────┐
                │ 1. 打开挡料板          │
                │ SetPourDoorAsync(true)│
                └───────────────────────┘
                            ↓
                ┌───────────────────────┐
                │ 2. 延迟等待            │
                │ (BaffleOpenDelay)     │
                │ 默认 0.5 秒            │
                └───────────────────────┘
                            ↓
                ┌───────────────────────┐
                │ 3. 启动振动2           │
                │ StartVibrationGroup2()│
                └───────────────────────┘
                            ↓
                ┌───────────────────────┐
                │ 4. 持续排料            │
                │ (DrainDuration)       │
                │ 默认 5 秒              │
                └───────────────────────┘
                            ↓
                ┌───────────────────────┐
                │ 5. 停止振动2           │
                │ StopVibrationGroup2() │
                └───────────────────────┘
                            ↓
                ┌───────────────────────┐
                │ 6. 关闭挡料板          │
                │ SetPourDoorAsync(false)│
                └───────────────────────┘
                            ↓
                ┌───────────────────────┐
                │ 7. 延迟等待            │
                │ (BaffleCloseDelay)    │
                │ 默认 0.5 秒            │
                └───────────────────────┘
                            ↓
                ┌───────────────────────┐
                │ 8. 振动+抖料补充       │
                │ (不修改计数器)         │
                └───────────────────────┘
                            ↓
                ┌───────────────────────┐
                │ 9. 重置所有计数器      │
                │ _vibrateCounter = 0   │
                │ _emptyCounter = 0     │
                └───────────────────────┘
```

### 4. 状态机设计

#### 4.1 状态变量

```csharp
// 计数器
private int _vibrateCounter = 0;    // 振动计数器（用于触发抖料）
private int _emptyCounter = 0;      // 空振计数器（用于触发排料）

// 状态标志
private bool _isDraining = false;   // 是否正在排料
```

#### 4.2 状态转换表

| 当前状态 | 输入条件 | 动作 | 下一状态 | 计数器变化 |
|---------|---------|------|---------|-----------|
| 空闲 | 检测到有料 | 返回成功 | 空闲 | 重置所有计数器 |
| 空闲 | 检测到无料 且 空振次数>=阈值 且 白色占比>阈值 | 触发排料流程 | 排料中 | 无变化 |
| 空闲 | 检测到无料 且 空振次数>=阈值 且 白色占比<=阈值 | 重置空振计数器 | 空闲 | _emptyCounter = 0 |
| 空闲 | 检测到无料 且 空振次数<阈值 且 振动计数器=0 | 只振动（不抖料） | 等待下次检测 | _vibrateCounter = 1 |
| 空闲 | 检测到无料 且 空振次数<阈值 且 振动计数器>=1 且 白色占比>阈值 | 只振动（不抖料） | 等待下次检测 | _vibrateCounter=1, _emptyCounter++ |
| 空闲 | 检测到无料 且 空振次数<阈值 且 振动计数器>=1 且 白色占比<=阈值 | 振动+抖料 | 等待下次检测 | _vibrateCounter=0, _emptyCounter++ |
| 排料中 | 排料流程执行中 | 等待完成 | 排料中 | 无变化 |
| 排料中 | 排料流程完成 | 重置所有计数器 | 空闲 | 重置所有计数器 |

**示例场景：**

**场景1：料盘已满但检测不到料（堆积）**
```
第1次检测：无料，白色占比60% → 只振动 → _vibrateCounter=1, _emptyCounter=0
第2次检测：无料，白色占比60% → 白色占比>50%，只振动 → _vibrateCounter=1, _emptyCounter=1
第3次检测：无料，白色占比60% → 白色占比>50%，只振动 → _vibrateCounter=1, _emptyCounter=2
第4次检测：无料，白色占比60% → 白色占比>50%，只振动 → _vibrateCounter=1, _emptyCounter=3
第5次检测：无料，白色占比60% → 白色占比>50%，只振动 → _vibrateCounter=1, _emptyCounter=4
第6次检测：无料，白色占比60% → 白色占比>50%，只振动 → _vibrateCounter=1, _emptyCounter=5
第7次检测：无料，白色占比60% → 空振次数>=5 且 白色占比>50% → 触发排料
```

**场景2：料盘正常但缺料**
```
第1次检测：无料，白色占比30% → 只振动 → _vibrateCounter=1, _emptyCounter=0
第2次检测：无料，白色占比30% → 白色占比<=50%，振动+抖料 → _vibrateCounter=0, _emptyCounter=1
第3次检测：有料 → 重置计数器 → 正常运行
```

**场景3：料分布不均（第2次检测就有料了）**
```
第1次检测：无料 → 只振动 → _vibrateCounter=1
第2次检测：有料 → 重置计数器 → 正常运行
```

## 配方参数完整清单

### 1. Vibrator 组（振动+抖料相关）

| 参数名 | 类型 | 默认值 | 单位 | 说明 | 使用位置 |
|--------|------|--------|------|------|---------|
| VibrationDuration | int | 300 | ms | 振动1持续时间（打散振动） | 振动操作（单独振动 或 振动+抖料） |
| StabilizeDelay | int | 300 | ms | 振动+抖料后稳定等待时间 | 振动+抖料操作 |
| （抖料时间） | - | - | - | 在振动盘配套软件中配置，C#代码不传递 | FeedOnceAsync() |

**说明**：
- 抖料时间参数在振动盘配套软件中配置，C# 代码只发送 Modbus 指令触发抖料
- 第1次无料：只振动（不抖料）
- 第2次及以后无料：振动+抖料
- 参数名统一使用 `VibrationDuration`（与现有代码保持一致）

### 2. VibrationTray 组（排料相关）

| 参数名 | 类型 | 默认值 | 单位 | 说明 | 使用位置 |
|--------|------|--------|------|------|---------|
| WhiteAreaThreshold | double | 50.0 | % | 白色占比阈值（0-100），超过此值判定为堆积 | 排料决策判断 |
| EmptyVibrateCount | int | 5 | 次 | 连续空振次数阈值，达到此值触发排料 | 排料决策判断 |
| DrainDuration | double | 5.0 | 秒 | 排料持续时间（振动2运行时间） | 排料流程 |
| BaffleOpenDelay | double | 0.5 | 秒 | 挡料板打开后延迟时间 | 排料流程 |
| BaffleCloseDelay | double | 0.5 | 秒 | 挡料板关闭后延迟时间 | 排料流程 |

**说明**：
- `WhiteAreaThreshold` 和 VM 输出的 `%WhiteAreaRatio%` 都是 0-100 的百分比值，可直接比较

### 3. 参数使用示意图

```
┌─────────────────────────────────────────────────────────────┐
│                    无料处理流程参数                           │
└─────────────────────────────────────────────────────────────┘

第1次检测无料
    ↓
┌─────────────────────────┐
│ 只振动（不抖料）         │
│ 持续 [VibrationDuration]│ ← Vibrator.VibrationDuration (300ms)
└─────────────────────────┘
    ↓
等待下次检测

第2次检测无料
    ↓
┌─────────────────────────┐
│ 启动振动1                │
│ 持续 [VibrationDuration]│ ← Vibrator.VibrationDuration (300ms)
└─────────────────────────┘
    ↓
┌─────────────────────────┐
│ 发送抖料指令             │
│ (时间在振动盘软件配置)   │
└─────────────────────────┘
    ↓
┌─────────────────────────┐
│ 等待稳定                 │
│ 延迟 [StabilizeDelay]   │ ← Vibrator.StabilizeDelay (300ms)
└─────────────────────────┘
    ↓
空振计数器+1

第3次检测无料
    ↓
重复"第1次检测无料"流程（只振动）

第4次检测无料
    ↓
重复"第2次检测无料"流程（振动+抖料）
空振计数器+1

...（循环）

空振计数器达到 [EmptyVibrateCount] 次（默认5次）
    ↓
白色占比 > [WhiteAreaThreshold]（默认50%）
    ↓
触发排料流程

┌─────────────────────────────────────────────────────────────┐
│                    排料流程参数                               │
└─────────────────────────────────────────────────────────────┘

    打开挡料板
         ↓
    延迟 [BaffleOpenDelay]        ← VibrationTray.BaffleOpenDelay (0.5s)
         ↓
    启动振动2
         ↓
    持续 [DrainDuration]          ← VibrationTray.DrainDuration (5s)
         ↓
    停止振动2
         ↓
    关闭挡料板
         ↓
    延迟 [BaffleCloseDelay]       ← VibrationTray.BaffleCloseDelay (0.5s)
         ↓
    执行振动+抖料补充（使用 Vibrator 组参数）
```

## 伪代码实现

### 1. DiskVisionHandler.ExecuteAsync 主流程

```csharp
async Task<(bool, string)> ExecuteAsync(IHandlerContext ctx, CancellationToken ct)
{
    // ============ 1. 初始化和设备检查 ============
    SetBusy(true);
    FlagCondition.SetFlag("DiskVision_Busy", true);

    var vision = ctx.GetVision();
    var vibrator = ctx.GetVibrator();

    if (!vision.IsConnected || !vibrator.IsConnected)
        return Fail("设备未连接");

    // ============ 2. 读取配方参数 ============
    var recipe = ctx.GetService<IRecipeService>();

    // Vibrator 组参数
    int vibrationDuration = recipe.GetInt("Vibrator", "VibrationDuration", 300);
    int stabilizeDelay = recipe.GetInt("Vibrator", "StabilizeDelay", 300);

    // VibrationTray 组参数
    double whiteAreaThreshold = recipe.GetDouble("VibrationTray", "WhiteAreaThreshold", 50.0);
    int emptyVibrateCount = recipe.GetInt("VibrationTray", "EmptyVibrateCount", 5);
    double drainDuration = recipe.GetDouble("VibrationTray", "DrainDuration", 5.0);
    double baffleOpenDelay = recipe.GetDouble("VibrationTray", "BaffleOpenDelay", 0.5);
    double baffleCloseDelay = recipe.GetDouble("VibrationTray", "BaffleCloseDelay", 0.5);

    try
    {
        // ============ 3. 执行 VM 视觉检测 ============
        await vision.ExecuteAsync(ct);
        var procedure = vision.GetLastProcedure() as VmProcedure;

        // 提取检测结果
        int blobNum = ExtractBlobNum(procedure);           // 种子数量
        float whiteAreaRatio = ExtractWhiteAreaRatio(procedure);  // 白色占比（0-100）

        LogInfo($"检测结果: 种子数={blobNum}, 白色占比={whiteAreaRatio:F2}%");

        // ============ 4. 有料分支 ============
        if (blobNum > 0)
        {
            // 重置所有计数器
            _vibrateCounter = 0;
            _emptyCounter = 0;

            return Success($"检测到 {blobNum} 颗种子");
        }

        // ============ 5. 无料分支 ============
        LogWarning("检测无料");

        // 5.1 先判断是否需要排料（检查空振次数和白色占比）
        if (_emptyCounter >= emptyVibrateCount)
        {
            LogWarning($"达到空振阈值 ({_emptyCounter}次)，检查白色占比");

            if (whiteAreaRatio > whiteAreaThreshold)
            {
                LogWarning($"白色占比 {whiteAreaRatio:F2}% > {whiteAreaThreshold}%，触发排料");

                // 执行排料流程
                await ExecuteDrainSequenceAsync(
                    vibrator,
                    drainDuration,
                    baffleOpenDelay,
                    baffleCloseDelay,
                    vibrationDuration,
                    stabilizeDelay,
                    ct);

                // 重置所有计数器
                _vibrateCounter = 0;
                _emptyCounter = 0;

                return Success("排料完成");
            }
            else
            {
                LogInfo($"白色占比 {whiteAreaRatio:F2}% <= {whiteAreaThreshold}%，无堆积，重置空振计数器");
                _emptyCounter = 0;
            }
        }

        // 5.2 判断是只振动还是振动+抖料
        if (_vibrateCounter == 0)
        {
            // 第1次无料：只振动（不抖料）
            LogInfo("第1次无料，只振动（不抖料）");
            await ExecuteVibrateOnlyAsync(vibrator, vibrationDuration, ct);
            _vibrateCounter = 1;
            return Success("只振动完成，等待下次检测");
        }
        else
        {
            // 第2次及以后无料：先检查白色占比
            if (whiteAreaRatio > whiteAreaThreshold)
            {
                // 白色占比已经很高，只振动不抖料
                LogWarning($"白色占比 {whiteAreaRatio:F2}% > {whiteAreaThreshold}%，只振动不抖料");
                await ExecuteVibrateOnlyAsync(vibrator, vibrationDuration, ct);
                _vibrateCounter = 1;  // 保持振动计数器，下次继续检查
                _emptyCounter++;      // 空振计数器累加（因为确实无料）
                LogInfo($"空振计数器: {_emptyCounter}/{emptyVibrateCount}");
                return Success("只振动完成（白色占比高），等待下次检测");
            }
            else
            {
                // 白色占比正常，执行振动+抖料
                LogInfo($"白色占比 {whiteAreaRatio:F2}% <= {whiteAreaThreshold}%，振动+抖料");
                await ExecuteVibrateAndFeedAsync(vibrator, vibrationDuration, stabilizeDelay, ct);
                _vibrateCounter = 0;  // 重置振动计数器
                _emptyCounter++;      // 空振计数器累加
                LogInfo($"空振计数器: {_emptyCounter}/{emptyVibrateCount}");
                return Success("振动+抖料完成，等待下次检测");
            }
        }
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
        LogError(ex, "执行异常");
        return Fail($"异常: {ex.Message}");
    }
    finally
    {
        SetBusy(false);
        FlagCondition.SetFlag("DiskVision_Busy", false);
    }
}
```

### 2. ExecuteVibrateOnlyAsync（只振动，不抖料）

```csharp
private async Task ExecuteVibrateOnlyAsync(
    IVibratorDevice vibrator,
    int vibrationDuration,
    CancellationToken ct)
{
    LogInfo("开始只振动（不抖料）");

    // 启动振动1（打散振动）
    LogInfo($"启动振动1，持续 {vibrationDuration}ms");
    await vibrator.StartVibrationAsync(ct);
    await Task.Delay(vibrationDuration, ct);
    await vibrator.StopVibrationAsync(ct);

    LogInfo("只振动完成");
}
```

### 3. ExecuteVibrateAndFeedAsync（振动+抖料）

```csharp
private async Task ExecuteVibrateAndFeedAsync(
    IVibratorDevice vibrator,
    int vibrationDuration,
    int stabilizeDelay,
    CancellationToken ct)
{
    LogInfo("开始振动+抖料");

    // 1. 启动振动1（打散振动）
    LogInfo($"启动振动1，持续 {vibrationDuration}ms");
    await vibrator.StartVibrationAsync(ct);
    await Task.Delay(vibrationDuration, ct);
    await vibrator.StopVibrationAsync(ct);

    // 2. 发送抖料指令（时间在振动盘软件中配置）
    LogInfo("发送抖料指令");
    await vibrator.FeedOnceAsync(ct);  // 不传递时间参数

    // 3. 等待稳定
    LogInfo($"等待稳定 {stabilizeDelay}ms");
    await Task.Delay(stabilizeDelay, ct);

    LogInfo("振动+抖料完成");
}
```

### 4. ExecuteDrainSequenceAsync（排料流程）

```csharp
private async Task ExecuteDrainSequenceAsync(
    IVibratorDevice vibrator,
    double drainDuration,
    double baffleOpenDelay,
    double baffleCloseDelay,
    int vibrateDuration,
    int stabilizeDelay,
    CancellationToken ct)
{
    LogWarning("========== 开始排料流程 ==========");
    _isDraining = true;

    try
    {
        // 1. 打开挡料板
        LogInfo("1. 打开挡料板");
        await vibrator.SetPourDoorAsync(true, ct);

        // 2. 延迟等待挡料板完全打开
        LogInfo($"2. 延迟 {baffleOpenDelay}s");
        await Task.Delay((int)(baffleOpenDelay * 1000), ct);

        // 3. 启动振动2（排料振动）
        LogInfo("3. 启动振动2");
        await vibrator.StartVibrationGroup2Async(ct);

        // 4. 持续排料
        LogInfo($"4. 持续排料 {drainDuration}s");
        await Task.Delay((int)(drainDuration * 1000), ct);

        // 5. 停止振动2
        LogInfo("5. 停止振动2");
        await vibrator.StopVibrationGroup2Async(ct);

        // 6. 关闭挡料板
        LogInfo("6. 关闭挡料板");
        await vibrator.SetPourDoorAsync(false, ct);

        // 7. 延迟等待挡料板完全关闭
        LogInfo($"7. 延迟 {baffleCloseDelay}s");
        await Task.Delay((int)(baffleCloseDelay * 1000), ct);

        // 8. 振动+抖料补充
        LogInfo("8. 振动+抖料补充");
        await ExecuteVibrateAndFeedAsync(vibrator, vibrateDuration, stabilizeDelay, ct);

        LogWarning("========== 排料流程完成 ==========");
    }
    finally
    {
        _isDraining = false;
    }
}
```

### 4. ExecuteDrainSequenceAsync（排料流程）

```csharp
private async Task ExecuteDrainSequenceAsync(
    IVibratorDevice vibrator,
    double drainDuration,
    double baffleOpenDelay,
    double baffleCloseDelay,
    int vibrateDuration,
    int stabilizeDelay,
    CancellationToken ct)
{
    LogWarning("========== 开始排料流程 ==========");
    _isDraining = true;

    try
    {
        // 1. 打开挡料板
        LogInfo("1. 打开挡料板");
        await vibrator.SetPourDoorAsync(true, ct);

        // 2. 延迟等待挡料板完全打开
        LogInfo($"2. 延迟 {baffleOpenDelay}s");
        await Task.Delay((int)(baffleOpenDelay * 1000), ct);

        // 3. 启动振动2（排料振动）
        LogInfo("3. 启动振动2");
        await vibrator.StartVibrationGroup2Async(ct);

        // 4. 持续排料
        LogInfo($"4. 持续排料 {drainDuration}s");
        await Task.Delay((int)(drainDuration * 1000), ct);

        // 5. 停止振动2
        LogInfo("5. 停止振动2");
        await vibrator.StopVibrationGroup2Async(ct);

        // 6. 关闭挡料板
        LogInfo("6. 关闭挡料板");
        await vibrator.SetPourDoorAsync(false, ct);

        // 7. 延迟等待挡料板完全关闭
        LogInfo($"7. 延迟 {baffleCloseDelay}s");
        await Task.Delay((int)(baffleCloseDelay * 1000), ct);

        // 8. 振动+抖料补充
        LogInfo("8. 振动+抖料补充");
        await ExecuteVibrateAndFeedAsync(vibrator, vibrateDuration, stabilizeDelay, ct);

        LogWarning("========== 排料流程完成 ==========");
    }
    finally
    {
        _isDraining = false;
    }
}
```

### 5. 辅助方法

```csharp
// 提取种子数量
private int ExtractBlobNum(VmProcedure procedure)
{
    if (procedure == null) return 0;

    var blobNumVar = procedure.GlobalVars.FirstOrDefault(v => v.Name == "%BlobNum%");
    if (blobNumVar != null && int.TryParse(blobNumVar.Value, out int blobNum))
        return blobNum;

    return 0;
}

// 提取白色占比
private float ExtractWhiteAreaRatio(VmProcedure procedure)
{
    if (procedure == null) return 0f;

    var whiteAreaVar = procedure.GlobalVars.FirstOrDefault(v => v.Name == "%WhiteAreaRatio%");
    if (whiteAreaVar != null && float.TryParse(whiteAreaVar.Value, out float ratio))
        return ratio;

    return 0f;
}
```

## VM 流程修改

### 1. 全局变量新增

在现有 VM 流程的全局变量中新增以下变量：

```xml
<!-- ==================== 排料检测参数 ==================== -->
<VarItem Index="29" Name="%WhiteAreaRatio%" Type="float" Remark="白色区域占比（0-100）"/>
<VarItem Index="30" Name="%BlobNum%" Type="int" Remark="检测到的种子数量"/>
```

**说明**：
- VM 流程在脚本2中同时输出白色占比数据和种子数量
- 不需要 `%EmptyVibrateCounter%` 和 `%NeedDrain%`（由 C# Handler 维护）

### 2. 修改脚本2：在小头匹配脚本中添加白色占比计算和种子数量统计

**位置**：在现有脚本2（小头匹配+碰撞检测）的末尾添加白色占比计算逻辑

**输入变量**（新增）：
- `binImg`（image）：二值化图像，绑定来源：图像处理(二值化).输出图像

**输出变量**（新增）：
- 通过 `SetGlobalVar()` 设置 `%WhiteAreaRatio%` 和 `%BlobNum%`

**算法逻辑**（在脚本2末尾添加）：
```csharp
// ========== 8. 白色占比计算和种子数量统计 ==========
// 统计二值化图像中白色像素数量（灰度值 >= 200）
int whitePixels = 0;
int totalPixels = imgW * imgH;

for (int y = 0; y < imgH; y++)
{
    for (int x = 0; x < imgW; x++)
    {
        if (imgBuf[y * imgW + x] >= whiteThresh)
            whitePixels++;
    }
}

// 计算白色占比
float whiteAreaRatio = (float)whitePixels / totalPixels * 100.0f;

// 写入全局变量
GlobalVariableModule.SetValue("WhiteAreaRatio", whiteAreaRatio);
GlobalVariableModule.SetValue("BlobNum", safeCount);

debug.AppendLine(string.Format(
    "白色占比: {0:F2}%, 种子数量: {1}", whiteAreaRatio, safeCount));
```

### 3. 新增脚本3：坐标超限检测（集成到协议组装模块）

**功能**：在标定转换后检测机器人坐标是否超出安全空间，防止因标定误差导致的坐标超限报警。

**位置**：替代或集成到现有的协议组装模块中

**输入变量**：
- `safeCount`（int）：安全玉米数量，绑定来源：脚本模块2.safeCount
- `worldXArr`（float[]）：机器人坐标X数组，绑定来源：标定转换.输出X数组
- `worldYArr`（float[]）：机器人坐标Y数组，绑定来源：标定转换.输出Y数组
- `worldAngleArr`（float[]）：机器人角度数组，绑定来源：标定转换.输出角度数组

**输出变量**：
- `protocolStr`（string）：协议字符串，供发送数据模块订阅
- `debugInfo`（string）：调试信息

**全局变量**（新增）：
- `%SafeMinX%`（float）：X坐标安全范围下限，由C#设置
- `%SafeMaxX%`（float）：X坐标安全范围上限，由C#设置
- `%SafeMinY%`（float）：Y坐标安全范围下限，由C#设置
- `%SafeMaxY%`（float）：Y坐标安全范围上限，由C#设置
- `%CoordinateOutOfBound%`（int）：坐标超限检测结果，供C#读取（0=正常, 1=超限）

**算法逻辑**：
```csharp
// ========== 坐标超限检测 + 协议组装 ==========
public bool Process()
{
    try
    {
        // 1. 读取安全范围参数(从全局变量获取，由C#预设)
        object minXObj = GlobalVariableModule.GetValue("SafeMinX");
        object maxXObj = GlobalVariableModule.GetValue("SafeMaxX");
        object minYObj = GlobalVariableModule.GetValue("SafeMinY");
        object maxYObj = GlobalVariableModule.GetValue("SafeMaxY");

        float safeMinX = Convert.ToSingle(minXObj ?? -50.0f);
        float safeMaxX = Convert.ToSingle(maxXObj ?? 50.0f);
        float safeMinY = Convert.ToSingle(minYObj ?? -50.0f);
        float safeMaxY = Convert.ToSingle(maxYObj ?? 50.0f);

        // 2. 读取标定转换后的坐标
        int safeCount = 0;
        GetIntValue("safeCount", ref safeCount);

        float[] worldX = new float[200];
        float[] worldY = new float[200];
        float[] worldAngle = new float[200];
        int cnt;
        GetFloatArrayValue("worldXArr", ref worldX, out cnt);
        GetFloatArrayValue("worldYArr", ref worldY, out cnt);
        GetFloatArrayValue("worldAngleArr", ref worldAngle, out cnt);

        // 3. 检测坐标是否超限
        int hasOutOfBound = 0;  // 0=正常, 1=超限
        List<int> validIndices = new List<int>();

        for (int i = 0; i < safeCount; i++)
        {
            bool isValid = (worldX[i] >= safeMinX && worldX[i] <= safeMaxX &&
                           worldY[i] >= safeMinY && worldY[i] <= safeMaxY);

            if (isValid)
            {
                validIndices.Add(i);
            }
            else
            {
                hasOutOfBound = 1;
                ConsoleWrite($"坐标超限: 点#{i} X={worldX[i]:F2}, Y={worldY[i]:F2}");
            }
        }

        // 4. 设置检测结果到全局变量
        GlobalVariableModule.SetValue("CoordinateOutOfBound", hasOutOfBound);

        // 5. 组装协议字符串(仅使用有效坐标)
        StringBuilder protocol = new StringBuilder();
        int validCount = validIndices.Count;

        if (validCount == 0)
        {
            protocol.Append("NO_POINTS");
        }
        else
        {
            for (int k = 0; k < validCount; k++)
            {
                int idx = validIndices[k];
                float angleOffset = 0f;
                object val = GlobalVariableModule.GetValue("AngleOffset");
                if (val != null) float.TryParse(val.ToString(), out angleOffset);

                float angle = worldAngle[idx] + angleOffset;
                string line = string.Format("{0:F3};{1:F3};{2:F2}",
                    worldX[idx], worldY[idx], angle);

                if (k > 0) protocol.Append("\n");
                protocol.Append(line);
            }
        }

        // 6. 输出结果
        SetStringValue("protocolStr", protocol.ToString());

        return true;
    }
    catch (Exception ex)
    {
        ConsoleWrite("坐标检测异常: " + ex.Message);
        GlobalVariableModule.SetValue("CoordinateOutOfBound", 1);  // 异常时设置为超限
        SetStringValue("protocolStr", "NO_POINTS");
        return false;
    }
}
```

### 1. 全局变量新增

在现有 VM 流程的全局变量中新增以下变量：

```xml
<!-- ==================== 排料检测参数 ==================== -->
<VarItem Index="29" Name="%WhiteAreaRatio%" Type="float" Remark="白色区域占比（0-100）"/>
<VarItem Index="30" Name="%BlobNum%" Type="int" Remark="检测到的种子数量"/>
```

**说明**：
- VM 流程在脚本2中同时输出白色占比数据和种子数量
- 不需要 `%EmptyVibrateCounter%` 和 `%NeedDrain%`（由 C# Handler 维护）

### 2. 修改脚本2：在小头匹配脚本中添加白色占比计算和种子数量统计

**位置**：在现有脚本2（小头匹配+碰撞检测）的末尾添加白色占比计算逻辑

**输入变量**（新增）：
- `binImg`（image）：二值化图像，绑定来源：图像处理(二值化).输出图像

**输出变量**（新增）：
- 通过 `SetGlobalVar()` 设置 `%WhiteAreaRatio%` 和 `%BlobNum%`

**算法逻辑**（在脚本2末尾添加）：
```csharp
// ========== 8. 白色占比计算和种子数量统计 ==========
// 统计二值化图像中白色像素数量（灰度值 >= 200）
int whitePixels = 0;
int totalPixels = imgW * imgH;

for (int y = 0; y < imgH; y++)
{
    for (int x = 0; x < imgW; x++)
    {
        if (imgBuf[y * imgW + x] >= whiteThresh)
            whitePixels++;
    }
}

// 计算白色占比
float whiteAreaRatio = (float)whitePixels / totalPixels * 100.0f;

// 写入全局变量
GlobalVariableModule.SetValue("WhiteAreaRatio", whiteAreaRatio);
GlobalVariableModule.SetValue("BlobNum", safeCount);

## 实施步骤

### Step 1: 修改 DiskVisionHandler（代码实现）

**文件**：`Framework/Services/Handlers/DiskVisionHandler.cs`

**修改内容**：
1. 新增成员变量（计数器和状态标志）
2. 修改 ExecuteAsync 主流程（参考伪代码）
3. 新增 ExecuteVibrateAndFeedAsync 私有方法
4. 新增 ExecuteDrainSequenceAsync 私有方法
5. 新增 ExtractWhiteAreaRatio 辅助方法
6. 读取配方参数（Vibrator 组 + VibrationTray 组）

### Step 2: 更新 TODO.md（代码实现）

**新增手工任务**：
- 在 VisionMaster 中修改脚本2，添加白色占比计算和种子数量统计逻辑
- 在 VisionMaster 中添加全局变量 %WhiteAreaRatio% 和 %BlobNum%
- 配置 PLC 信号地址（挡料板控制）
- 测试排料流程（挡料板+振动2）
- 标定白色占比阈值参数

## 验证方案

### 单元测试
1. VM 脚本4 白色占比计算准确性
2. 空振计数器逻辑正确性
3. 排料触发条件判断

### 集成测试
1. 模拟无料场景，验证排料触发
2. 验证挡料板开关时序
3. 验证振动2启停时序
4. 验证排料完成后状态重置

### 现场测试
1. 实际料盘堆积场景测试
2. 排料效果验证（料是否排空）
3. 排料后重新上料流程验证
4. 长时间运行稳定性测试

## 风险和注意事项

1. **IVibratorDevice 接口**：需确认有 `SetPourDoorAsync()` 方法
2. **Modbus 地址配置**：挡料板的 Modbus 地址需要在 VibratorConfig 中配置
3. **计数器持久化**：当前计数器是内存变量，重启后会丢失（可接受）
4. **并发控制**：使用 `_isDraining` 标志防止排料过程中重复触发
5. **振动+抖料时机**：振动X次后才抖料（默认1次），避免过早抖料导致堆积
6. **排料后抖料**：排料完成后自动振动+抖料补充
7. **白色占比阈值标定**：需要根据实际料盘图像调整，避免误触发
8. **空振次数阈值**：设置过小会频繁排料，设置过大会延迟排料
9. **挡料板时序**：确保挡料板完全打开后再启动振动2
10. **排料持续时间**：确保料完全排空，但不要过长影响效率
11. **抖料时间参数**：在振动盘配套软件中配置，C# 代码只发送触发指令

## 后续优化方向

1. **自适应阈值**：根据历史数据自动调整白色占比阈值
2. **排料效果检测**：排料完成后检测料盘是否真的排空
3. **排料统计分析**：统计排料频率，分析料盘异常原因
4. **预测性排料**：根据料盘状态趋势，提前触发排料
5. **排料事件记录**：记录排料事件到追踪器和日志

---

**文档版本**：v3.1
**创建日期**：2026-03-10
**更新日期**：2026-03-11
**作者**：Claude Opus 4.6
**审核状态**：待审核

**v3.1 更新内容（2026-03-11）**：
- 优化抖料逻辑：在执行"振动+抖料"前增加白色占比检查
- 如果白色占比已超过阈值，只振动不抖料，避免料越加越多
- 更新无料处理流程图、状态转换表、场景示例和伪代码

## 关键文件清单

### 需要修改的文件
1. `Framework/Services/Handlers/DiskVisionHandler.cs` - 集成排料和振动+抖料逻辑
2. `TODO.md` - 添加手工任务

### 需要手工操作的任务
1. VisionMaster 中添加脚本4和全局变量
2. PLC 信号地址配置（挡料板控制）
3. 振动盘配套软件中配置抖料时间参数
4. 参数标定和测试
