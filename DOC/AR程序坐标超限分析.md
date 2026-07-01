# AR程序坐标超限相关代码分析

## 一、程序中现有的坐标限制代码

### 1.1 偏差阈值定义（第53-55行）

```lua
local MAX_OFFSET_X = 5.0   -- X轴最大偏差 ±5mm
local MAX_OFFSET_Y = 5.0   -- Y轴最大偏差 ±5mm
local MAX_OFFSET_C = 10.0  -- C轴最大偏差 ±10度
```

这三个常量定义了**飞拍纠偏**阶段允许的最大偏差范围。注意：这**仅用于飞拍偏差校验**，与视觉抓取坐标本身无关。

### 1.2 偏差有效性检查函数（第479-493行）

```lua
function isOffsetValid(x, y, c)
    if math.abs(x) > MAX_OFFSET_X then
        print("警告: X偏差超限! " .. x .. " > " .. MAX_OFFSET_X)
        return false
    end
    if math.abs(y) > MAX_OFFSET_Y then
        print("警告: Y偏差超限! " .. y .. " > " .. MAX_OFFSET_Y)
        return false
    end
    if math.abs(c) > MAX_OFFSET_C then
        print("警告: C偏差超限! " .. c .. " > " .. MAX_OFFSET_C)
        return false
    end
    return true
end
```

该函数在两个位置被调用：

- **`parseOffsetData()`（第555行）**：解析飞拍返回的偏差数据时，如果超限，偏差置零（不纠偏）。
- **`executeCorrectedPlace()`（第592行）**：执行纠偏放料前再次检查（双重保险）。

### 1.3 角度溢出处理（第315-328行）

```lua
local anglePI = 180.0
local angleCircle = 360.0
local threshold = 300.0

if point.c - anglePI > threshold then
    catchReady.c = point.c - anglePI - angleCircle
    catch.c = point.c - anglePI - angleCircle
elseif point.c - anglePI < -threshold then
    catchReady.c = point.c - anglePI + angleCircle
    catch.c = point.c - anglePI + angleCircle
else
    catchReady.c = point.c - anglePI
    catch.c = point.c - anglePI
end
```

该段代码对 C 轴（旋转角度）做了溢出处理，防止角度超出 ±300° 的范围。这**仅针对 C 轴角度**，不涉及 X/Y 坐标。

---

## 二、核心问题：视觉抓取坐标没有做范围校验

### 2.1 问题定位

在 `processVisionData()` 函数（第95-176行）中，程序从 HeadCam 接收到视觉坐标后，直接解析并存入队列：

```lua
table.insert(grabPointsQueue, {
    x = numX,
    y = numY,
    c = numC,
    processed = false
})
```

**没有对 `numX` 和 `numY` 做任何范围检查。**

随后在 `processCurrentPoint()` 函数（第299-348行）中，这些坐标被**直接赋值**给运动目标：

```lua
catchReady.x = point.x
catchReady.y = point.y
catch.x = point.x
catch.y = point.y
```

然后机器人就会执行 `MArchP(catchReady, readyZ, 10, 10)` 运动到该坐标。如果坐标超出机器人安全空间，控制器就会触发**超限报警**。

### 2.2 总结一句话

> **程序只校验了飞拍偏差（±5mm），但完全没有校验视觉返回的抓取坐标本身是否在机器人安全空间内。**

---

## 三、为什么 ROI 框比安全空间小，还是会超限报警？

你提到 VM 流程只画了一个 ROI 框，且 ROI 比安全空间小得多，但仍频繁超限。可能原因如下：

### 3.1 VM 返回的是像素坐标经标定转换后的物理坐标，而非 ROI 边界坐标

ROI 框限制的是**相机在图像中的搜索区域（像素范围）**，但 VM 最终返回的是经过**手眼标定（Eye-Hand Calibration）矩阵**转换后的**机器人坐标系下的物理坐标**。如果标定存在误差（尤其是边缘区域畸变），转换出的物理坐标可能远超 ROI 对应的物理范围。

### 3.2 标定精度在边缘区域退化

相机镜头天然存在畸变，标定矩阵在图像中心区域精度高、边缘区域精度低。当物料出现在 ROI 边缘时，转换后的坐标偏差会显著放大，可能超出安全空间。

### 3.3 VM 坐标系与机器人坐标系存在偏移或旋转

如果标定时的参考坐标系与机器人当前的工件坐标系之间有累积误差（比如工装夹具移动过、重新标定不准确），即使 ROI 很小，所有输出坐标也可能整体偏移，导致部分点落在安全空间外。

### 3.4 振动盘导致物料位置变化

振动盘工作时物料可能移动到 ROI 边缘甚至部分超出 ROI，VM 仍然能检测到（只要特征中心在 ROI 内），但输出的坐标已经接近或超出安全空间。

---

## 四、建议修复方案

### 4.1 在 `processVisionData()` 中增加坐标范围校验

在解析坐标后、入队前，增加对 X/Y 的安全空间判断：

```lua
-- 建议新增：安全空间范围定义
local SAFE_MIN_X = ???   -- 根据实际安全空间填写
local SAFE_MAX_X = ???
local SAFE_MIN_Y = ???
local SAFE_MAX_Y = ???

-- 在 table.insert 前增加：
if numX >= SAFE_MIN_X and numX <= SAFE_MAX_X and
   numY >= SAFE_MIN_Y and numY <= SAFE_MAX_Y then
    table.insert(grabPointsQueue, {
        x = numX, y = numY, c = numC, processed = false
    })
else
    print("警告: 视觉坐标超出安全空间! X=" .. numX .. ", Y=" .. numY)
end
```

### 4.2 在 `processCurrentPoint()` 运动前增加二次校验

在 `MArchP` 调用前，再次确认目标坐标在安全范围内：

```lua
if catchReady.x < SAFE_MIN_X or catchReady.x > SAFE_MAX_X or
   catchReady.y < SAFE_MIN_Y or catchReady.y > SAFE_MAX_Y then
    print("坐标超出安全空间，跳过该点")
    grabPointsQueue[currentPointIndex].processed = true
    currentState = "READY"
    checkNextPoint()
    return
end
```

### 4.3 在 `executeCorrectedPlace()` 纠偏后也检查放料坐标

纠偏后的放料位 `correctedDropPos` 也应做范围检查，防止偏差叠加后超限。

### 4.4 排查 VM 标定精度

在 VM 中对 ROI 四个角点分别做一次坐标输出测试，确认转换后的物理坐标都在安全空间内。如果角点转换出的坐标已经逼近安全边界，说明 ROI 需要进一步缩小，或者需要重新标定。

---

## 五、相关代码位置速查表

| 内容 | 所在函数 | 行号 |
|------|----------|------|
| 偏差阈值定义 | 全局变量 | 53-55 |
| 偏差校验函数 | `isOffsetValid()` | 479-493 |
| 飞拍偏差解析与校验 | `parseOffsetData()` | 528-583 |
| 纠偏放料前二次校验 | `executeCorrectedPlace()` | 587-653 |
| C轴角度溢出处理 | `processCurrentPoint()` | 315-328 |
| **⚠️ 视觉坐标入队（无校验）** | `processVisionData()` | 152-159 |
| **⚠️ 坐标直接赋值运动（无校验）** | `processCurrentPoint()` | 306-313, 335 |
