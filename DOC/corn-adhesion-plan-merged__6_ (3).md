# 玉米抓取方案：多特征筛选 + DL小头检测 + 夹爪碰撞检测

> Blob 多特征筛选剔除粘连体 → DL检测小头确认方向 → 夹爪碰撞检测

---

## VM 流程

```
图像源 → 二值化 → 开运算(腐蚀3×3 + 膨胀3×3)
                          ↓
                    Blob分析1(面积/中心/外接矩形/周长/BlobROI)
                          ↓
                    脚本1(多特征粘连筛选 → validBlobROI[])
                          ↓
                    DL目标检测(玉米小头 → 小头中心X/Y)
                          ↓
                    脚本2(小头匹配 + 方向 + 夹爪碰撞 → safeBlobROI[])
                          ↓
                    几何创建(订阅 safeBlobROI)
```

### Blob分析1 输出配置

| 输出项 | 类型 | 用途 |
|--------|------|------|
| 中心X数组 | float[] | 定位 |
| 中心Y数组 | float[] | 定位 |
| 面积数组 | float[] | 面积筛选 |
| 周长数组 | float[] | 计算紧凑度 |
| 最小外接矩形宽度数组 | float[] | 计算长短边比 |
| 最小外接矩形高度数组 | float[] | 计算长短边比 |
| Blob外接矩形ROI数组 | RoiboxData[] | 脚本筛选后直接输出ROI |
| Blob数量 | int | 计数 |

### DL目标检测 输出配置（玉米小头）

| 输出项 | 类型 | 用途 |
|--------|------|------|
| 检测框中心X数组 | float[] | 小头定位 |
| 检测框中心Y数组 | float[] | 小头定位 |
| 检测框数量 | int | 小头计数 |

---

## 全局变量 XML（直接复制导入）

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Root Version="V440">
    <!-- ==================== 脚本1参数：原始图筛选 ==================== -->
    <VarItem Index="18" Name="%AreaMin%" Type="float" CommEnable="false" Combination="false" Remark="单颗玉米面积下限（像素）" GroupInfo="分组3" Guid="a1b2c3d4-1111-4aaa-b111-00000000001">
        <Value>2000.000000</Value>
    </VarItem>
    <VarItem Index="19" Name="%AreaMax%" Type="float" CommEnable="false" Combination="false" Remark="单颗玉米面积上限（像素）" GroupInfo="分组3" Guid="a1b2c3d4-2222-4aaa-b222-00000000001">
        <Value>18000.000000</Value>
    </VarItem>
    <VarItem Index="20" Name="%CompactnessMax%" Type="float" CommEnable="false" Combination="false" Remark="紧凑度阈值，粘连体一般大于此值" GroupInfo="分组3" Guid="a1b2c3d4-3333-4aaa-b333-00000000002">
        <Value>1.600000</Value>
    </VarItem>
    <VarItem Index="21" Name="%AspectRatioMax%" Type="float" CommEnable="false" Combination="false" Remark="最小外接矩形长短边比阈值" GroupInfo="分组3" Guid="a1b2c3d4-4444-4aaa-b444-00000000002">
        <Value>3.000000</Value>
    </VarItem>

    <!-- ==================== 脚本2参数：夹爪碰撞检测 ==================== -->
    <VarItem Index="22" Name="%GripperLength%" Type="float" CommEnable="false" Combination="false" Remark="夹爪长度（像素），沿玉米方向" GroupInfo="分组3" Guid="a1b2c3d4-5555-4aaa-b555-00000000002">
        <Value>120.000000</Value>
    </VarItem>
    <VarItem Index="23" Name="%GripperWidth%" Type="float" CommEnable="false" Combination="false" Remark="夹爪宽度（像素），垂直于玉米方向" GroupInfo="分组3" Guid="a1b2c3d4-6666-4aaa-b666-00000000002">
        <Value>60.000000</Value>
    </VarItem>
    <VarItem Index="24" Name="%CollisionSampleStep%" Type="int" CommEnable="false" Combination="false" Remark="碰撞检测采样步长（像素），越大越快但越粗糙" GroupInfo="分组3" Guid="a1b2c3d4-7777-4aaa-b777-00000000002">
        <Value>3</Value>
    </VarItem>
    <VarItem Index="25" Name="%WhiteThreshold%" Type="int" CommEnable="false" Combination="false" Remark="白色像素判定阈值，灰度>=此值视为白色" GroupInfo="分组3" Guid="a1b2c3d4-8888-4aaa-b888-00000000002">
        <Value>200</Value>
    </VarItem>
    <VarItem Index="27" Name="%BoxExpandPixel%" Type="int" CommEnable="false" Combination="false" Remark="自身排除框向外扩展像素数，防止边缘误判" GroupInfo="分组3" Guid="a1b2c3d4-9999-4aaa-b999-00000000002">
        <Value>30</Value>
    </VarItem>

    <!-- ==================== 原有流程变量（保留） ==================== -->
    <VarItem Index="1" Name="%gNum%" Type="int" CommEnable="false" Combination="false" Remark="" GroupInfo="分组0" Guid="6e41858c-8eab-40f4-ae25-1f5d17a1c8d">
        <Value>0</Value>
    </VarItem>
    <VarItem Index="2" Name="%catch%" Type="int" CommEnable="false" Combination="false" Remark="" GroupInfo="分组0" Guid="61b1bd23-b5d2-43f3-b9b4-944f7f15e58">
        <Value>2</Value>
    </VarItem>
    <VarItem Index="3" Name="%release%" Type="int" CommEnable="false" Combination="false" Remark="" GroupInfo="分组0" Guid="dc893220-178a-4474-9a55-f1ca33e96d5">
        <Value>0</Value>
    </VarItem>
    <VarItem Index="4" Name="%msg%" Type="string" CommEnable="false" Combination="false" Remark="" GroupInfo="分组0" Guid="4b08e109-9b33-4c5b-b656-c901c39f875">
        <Value></Value>
    </VarItem>
    <VarItem Index="5" Name="%send%" Type="string" CommEnable="false" Combination="false" Remark="" GroupInfo="分组0" Guid="294fd715-51b9-417a-95a5-2a61058f3db">
        <Value></Value>
    </VarItem>
    <VarItem Index="6" Name="%img%" Type="IMAGE" CommEnable="false" Combination="true" Remark="全局原始图像" GroupInfo="分组0" Guid="13d875cf-2b35-445f-bfcf-7c6762e8212">
        <SubIOInfo SubCount="4">
            <IOItem Name="%Image%" Type="4"/>
            <IOItem Name="%ImageWidth%" Type="0"/>
            <IOItem Name="%ImageHeight%" Type="0"/>
            <IOItem Name="%ImagePixelFormat%" Type="0"/>
        </SubIOInfo>
    </VarItem>
    <VarItem Index="7" Name="%blob%" Type="ROIBOX" CommEnable="false" Combination="true" Remark="全局blob分析" GroupInfo="分组0" Guid="ec70e5ca-ec1d-4943-a09f-06aa6ff9f71">
        <SubIOInfo SubCount="5">
            <IOItem Name="%roicenterx%" Type="1"/>
            <IOItem Name="%roicentery%" Type="1"/>
            <IOItem Name="%roiwidth%" Type="1"/>
            <IOItem Name="%roiheight%" Type="1"/>
            <IOItem Name="%roiangle%" Type="1"/>
        </SubIOInfo>
    </VarItem>
    <VarItem Index="8" Name="%DL%" Type="ROIBOX" CommEnable="false" Combination="true" Remark="全局目标检测" GroupInfo="分组0" Guid="8461bc59-ddcf-41c4-ab13-a5deae68263">
        <SubIOInfo SubCount="5">
            <IOItem Name="%roicenterx0%" Type="1"/>
            <IOItem Name="%roicentery0%" Type="1"/>
            <IOItem Name="%roiwidth0%" Type="1"/>
            <IOItem Name="%roiheight0%" Type="1"/>
            <IOItem Name="%roiangle0%" Type="1"/>
        </SubIOInfo>
    </VarItem>
    <VarItem Index="9" Name="%targetArea%" Type="int" CommEnable="false" Combination="false" Remark="切割目标面积" GroupInfo="分组0" Guid="e5dacca6-0438-4609-ad70-6ce7f4d3305">
        <Value>8000</Value>
    </VarItem>
    <VarItem Index="10" Name="%L%" Type="float" CommEnable="false" Combination="false" Remark="夹爪长（像素）" GroupInfo="分组0" Guid="df79bda5-d5a7-45fd-ac06-e4278190460">
        <Value>250.000000</Value>
    </VarItem>
    <VarItem Index="11" Name="%nearNum%" Type="int" CommEnable="false" Combination="false" Remark="重叠个数" GroupInfo="分组0" Guid="1f8d17b3-15c3-4ca0-ba47-731444be890">
        <Value>0</Value>
    </VarItem>
    <VarItem Index="12" Name="%offsetX%" Type="float" CommEnable="false" Combination="false" Remark="机械手偏移" GroupInfo="分组1" Guid="766d943d-1c55-43f2-9795-fc6adf6f6fa">
        <Value>0.600000</Value>
    </VarItem>
    <VarItem Index="13" Name="%offsetY%" Type="float" CommEnable="false" Combination="false" Remark="机械手偏移" GroupInfo="分组1" Guid="44b141a5-d7d7-40a5-8f48-0d0e8f59bd7">
        <Value>2.500000</Value>
    </VarItem>
    <VarItem Index="14" Name="%offsetC%" Type="float" CommEnable="false" Combination="false" Remark="机械手偏移" GroupInfo="分组1" Guid="ee5e0007-6d96-4760-9007-b198b9375b4">
        <Value>11.200000</Value>
    </VarItem>
    <VarItem Index="15" Name="%nearNum1%" Type="int" CommEnable="false" Combination="false" Remark="" GroupInfo="分组0" Guid="fc8e9ee2-35d4-414e-8786-fd4d9deed98">
        <Value>0</Value>
    </VarItem>
    <VarItem Index="16" Name="%nearNum2%" Type="int" CommEnable="false" Combination="false" Remark="" GroupInfo="分组0" Guid="c2459efa-5dc9-4c6c-853e-c4069e8d9ae">
        <Value>0</Value>
    </VarItem>
    <VarItem Index="17" Name="%sendLaser%" Type="string" CommEnable="false" Combination="false" Remark="发至激光的指令" GroupInfo="分组2" Guid="c0cf930e-0014-49bc-bff2-0fdba616698">
        <Value></Value>
    </VarItem>
    <VarItem Index="26" Name="%var0%" Type="IMAGE" CommEnable="false" Combination="true" Remark="" GroupInfo="分组3" Guid="6b34ec54-ef98-4cee-9b97-57ec3c144a3a">
        <SubIOInfo SubCount="4">
            <IOItem Name="%Image0%" Type="4"/>
            <IOItem Name="%ImageWidth0%" Type="0"/>
            <IOItem Name="%ImageHeight0%" Type="0"/>
            <IOItem Name="%ImagePixelFormat0%" Type="0"/>
        </SubIOInfo>
    </VarItem>

    <!-- ==================== 振动盘排料检测参数 ==================== -->
    <VarItem Index="29" Name="%WhiteAreaRatio%" Type="float" CommEnable="false" Combination="false" Remark="白色区域占比（0-100）" GroupInfo="分组3" Guid="a1b2c3d4-aa01-4aaa-b001-00000000003">
        <Value>0.000000</Value>
    </VarItem>
    <VarItem Index="30" Name="%BlobNum%" Type="int" CommEnable="false" Combination="false" Remark="检测到的种子数量" GroupInfo="分组3" Guid="a1b2c3d4-aa02-4aaa-b002-00000000003">
        <Value>0</Value>
    </VarItem>

    <GroupInfo>分组0分组1分组2分组3</GroupInfo>
</Root>
```

---

## 脚本1：多特征粘连筛选

**算法**：面积范围 → 紧凑度(周长²/4πA) → 长短边比，三层筛选剔除粘连体和碎屑。

### 输入变量

| 变量名 | 类型 | 绑定来源 |
|--------|------|----------|
| `blobCount` | `int` | Blob分析1.Blob数量 |
| `centerXArr` | `float[]` | Blob分析1.中心X数组 |
| `centerYArr` | `float[]` | Blob分析1.中心Y数组 |
| `areaArr` | `float[]` | Blob分析1.面积数组 |
| `perimeterArr` | `float[]` | Blob分析1.周长数组 |
| `rectWidthArr` | `float[]` | Blob分析1.最小外接矩形宽度数组 |
| `rectHeightArr` | `float[]` | Blob分析1.最小外接矩形高度数组 |
| `blobROI` | `RoiboxData[]` | Blob分析1.Blob外接矩形ROI数组 |

### 输出变量

| 变量名 | 类型 | 说明 |
|--------|------|------|
| `validCount` | `int` | 筛选后有效Blob数量 |
| `validCenterXArr` | `float[]` | 有效Blob中心X数组 |
| `validCenterYArr` | `float[]` | 有效Blob中心Y数组 |
| `validAreaArr` | `float[]` | 有效Blob面积数组 |
| `validBlobROI` | `RoiboxData[]` | 有效Blob的外接矩形ROI数组 |
| `debugInfo` | `string` | 调试信息 |

> **几何创建**：脚本1之后添加几何创建模块，订阅 `validBlobROI`（RoiboxData[]），画矩形框显示。

```csharp
using System;
using System.Text;
using System.Collections.Generic;
using Script.Methods;

public partial class UserScript : ScriptMethods, IProcessMethods
{
    int processCount;

    public void Init()
    {
        processCount = 0;
    }

    public bool Process()
    {
        try
        {
            // ========== 1. 从全局变量读取参数 ==========
            float areaMin = 2000f;
            float areaMax = 18000f;
            float compactnessMax = 1.6f;
            float aspectRatioMax = 3.0f;

            object val;
            val = GlobalVariableModule.GetValue("AreaMin");
            if (val != null) float.TryParse(val.ToString(), out areaMin);
            val = GlobalVariableModule.GetValue("AreaMax");
            if (val != null) float.TryParse(val.ToString(), out areaMax);
            val = GlobalVariableModule.GetValue("CompactnessMax");
            if (val != null) float.TryParse(val.ToString(), out compactnessMax);
            val = GlobalVariableModule.GetValue("AspectRatioMax");
            if (val != null) float.TryParse(val.ToString(), out aspectRatioMax);

            // ========== 2. 读取输入数据 ==========
            int blobCnt = 0;
            GetIntValue("blobCount", ref blobCnt);

            float[] cx = new float[200];
            float[] cy = new float[200];
            float[] area = new float[200];
            float[] perimeter = new float[200];
            float[] rectW = new float[200];
            float[] rectH = new float[200];
            int cnt;

            GetFloatArrayValue("centerXArr", ref cx, out cnt);
            GetFloatArrayValue("centerYArr", ref cy, out cnt);
            GetFloatArrayValue("areaArr", ref area, out cnt);
            GetFloatArrayValue("perimeterArr", ref perimeter, out cnt);
            GetFloatArrayValue("rectWidthArr", ref rectW, out cnt);
            GetFloatArrayValue("rectHeightArr", ref rectH, out cnt);

            RoiboxData[] blobROI = new RoiboxData[blobCnt > 0 ? blobCnt : 1];
            int roiTotal = blobCnt;
            GetRoiBoxArrayValue("blobROI", ref blobROI, out roiTotal);

            StringBuilder debug = new StringBuilder();
            debug.AppendLine("=== 多特征粘连筛选 ===");
            debug.AppendLine("输入Blob数: " + blobCnt);
            debug.AppendLine(string.Format(
                "参数: AreaMin={0}, AreaMax={1}, CompMax={2}, ARMax={3}",
                areaMin, areaMax, compactnessMax, aspectRatioMax));

            // ========== 3. 特征值统计（调试用） ==========
            float minArea = float.MaxValue, maxArea = 0;
            float minComp = float.MaxValue, maxComp = 0;
            float minAR = float.MaxValue, maxAR = 0;

            for (int i = 0; i < blobCnt; i++)
            {
                if (area[i] < minArea) minArea = area[i];
                if (area[i] > maxArea) maxArea = area[i];

                if (area[i] > 0 && perimeter[i] > 0)
                {
                    float comp = (perimeter[i] * perimeter[i])
                                 / (4f * 3.14159f * area[i]);
                    if (comp < minComp) minComp = comp;
                    if (comp > maxComp) maxComp = comp;
                }

                float longS = Math.Max(rectW[i], rectH[i]);
                float shortS = Math.Min(rectW[i], rectH[i]);
                float ar = shortS > 0 ? longS / shortS : 0;
                if (ar < minAR) minAR = ar;
                if (ar > maxAR) maxAR = ar;
            }

            if (blobCnt > 0)
            {
                debug.AppendLine(string.Format(
                    "统计: 面积[{0:F0}~{1:F0}] 紧凑度[{2:F2}~{3:F2}] 长短比[{4:F2}~{5:F2}]",
                    minArea, maxArea, minComp, maxComp, minAR, maxAR));
            }

            // ========== 4. 三层筛选 ==========
            List<int> validIndices = new List<int>();

            for (int i = 0; i < blobCnt; i++)
            {
                string reason = "";
                bool isValid = true;
                float compactness = 0f;

                // 第1层：面积
                if (area[i] < areaMin)
                { reason = "面积过小"; isValid = false; }
                else if (area[i] > areaMax)
                { reason = "面积过大"; isValid = false; }

                // 第2层：紧凑度
                if (isValid && area[i] > 0 && perimeter[i] > 0)
                {
                    compactness = (perimeter[i] * perimeter[i])
                                  / (4.0f * 3.14159f * area[i]);
                    if (compactness > compactnessMax)
                    {
                        reason = string.Format("紧凑度超标({0:F2}>{1:F1})",
                                               compactness, compactnessMax);
                        isValid = false;
                    }
                }

                // 第3层：长短边比
                if (isValid && rectW[i] > 0)
                {
                    float longSide = Math.Max(rectW[i], rectH[i]);
                    float shortSide = Math.Min(rectW[i], rectH[i]);
                    float aspectRatio = longSide / shortSide;
                    if (aspectRatio > aspectRatioMax)
                    {
                        reason = string.Format("长短比超标({0:F2}>{1:F1})",
                                               aspectRatio, aspectRatioMax);
                        isValid = false;
                    }
                }

                debug.AppendLine(string.Format(
                    "  Blob#{0}: 面积={1:F0}, 紧凑度={2:F2}, 矩形={3:F0}x{4:F0}",
                    i, area[i], compactness, rectW[i], rectH[i]));

                if (isValid)
                    validIndices.Add(i);
                else
                    debug.AppendLine(string.Format("    ✗ 剔除: {0}", reason));
            }

            // ========== 5. 输出 ==========
            int resultCount = validIndices.Count;
            debug.AppendLine("筛选后有效Blob数: " + resultCount);

            float[] outCx = new float[resultCount];
            float[] outCy = new float[resultCount];
            float[] outArea = new float[resultCount];
            RoiboxData[] outROI = new RoiboxData[resultCount > 0 ? resultCount : 1];

            for (int k = 0; k < resultCount; k++)
            {
                int idx = validIndices[k];
                outCx[k] = cx[idx];
                outCy[k] = cy[idx];
                outArea[k] = area[idx];
                outROI[k] = blobROI[idx];
            }

            SetIntValue("validCount", resultCount);
            SetFloatArrayValue("validCenterXArr", outCx, 0, resultCount);
            SetFloatArrayValue("validCenterYArr", outCy, 0, resultCount);
            SetFloatArrayValue("validAreaArr", outArea, 0, resultCount);
            SetRoiBoxArrayValue("validBlobROI", outROI, 0, resultCount);
            SetStringValue("debugInfo", debug.ToString());
            ConsoleWrite(debug.ToString());

            processCount++;
            return true;
        }
        catch (Exception ex)
        {
            ConsoleWrite("筛选脚本异常: " + ex.Message);
            SetIntValue("validCount", 0);
            SetStringValue("debugInfo", "异常: " + ex.Message);
            return false;
        }
    }
}
```

---

## 脚本2：小头匹配 + 方向计算 + 夹爪碰撞检测（V3 修正版）

**V3 修改说明**：
1. **修正：小头匹配改用旋转矩形局部坐标判断**——旧版将旋转矩形转为AABB，倾斜时AABB严重膨胀，导致旁边碎片的小头被错误匹配。新版将tip点转到ROI的局部坐标系下判断，精确贴合玉米真实轮廓。
2. **修正：全局最近距离匹配替代顺序贪心**——旧版按数组顺序遍历corn，先遍历的corn可能抢走别人的tip。新版先算出所有 corn-tip 距离对，按距离从小到大全局匹配，确保最近的配对优先。

**算法**：
1. 小头中心转到Blob旋转矩形局部坐标系 → 判断是否在矩形内 → 收集所有候选配对
2. 所有候选配对按距离排序 → 全局最近优先匹配（每个corn和tip只匹配一次）
3. 方向 = 小头中心 - 玉米中心 → 归一化
4. 以玉米中心为中心，沿方向轴生成 GripperLength × GripperWidth 旋转矩形
5. 在矩形AABB内按步长采样，局部坐标系判断点是否在旋转矩形内，检查白色像素（排除自身Blob框外扩 BoxExpandPixel 像素后的区域），有碰撞则不可抓

### 输入变量

| 变量名 | 类型 | 绑定来源 |
|--------|------|----------|
| `validCount` | `int` | 脚本模块1.validCount |
| `validCenterXArr` | `float[]` | 脚本模块1.validCenterXArr |
| `validCenterYArr` | `float[]` | 脚本模块1.validCenterYArr |
| `validAreaArr` | `float[]` | 脚本模块1.validAreaArr |
| `validBlobROI` | `RoiboxData[]` | 脚本模块1.validBlobROI |
| `tipCount` | `int` | DL目标检测.检测框数量 |
| `tipCenterXArr` | `float[]` | DL目标检测.检测框中心X数组 |
| `tipCenterYArr` | `float[]` | DL目标检测.检测框中心Y数组 |
| `binImg` | `image` | 图像处理(二值化).输出图像 |

### 输出变量

| 变量名 | 类型 | 说明 |
|--------|------|------|
| `safeCount` | `int` | 最终安全可抓取玉米数量 |
| `safeCenterXArr` | `float[]` | 安全玉米中心X数组 |
| `safeCenterYArr` | `float[]` | 安全玉米中心Y数组 |
| `safeTipXArr` | `float[]` | 安全玉米对应小头中心X数组 |
| `safeTipYArr` | `float[]` | 安全玉米对应小头中心Y数组 |
| `safeAngleArr` | `float[]` | 安全玉米方向角度数组（弧度，仅碰撞检测内部使用） |
| `safeAreaArr` | `float[]` | 安全玉米面积数组 |
| `safeBlobROI` | `RoiboxData[]` | 安全玉米的外接矩形ROI数组 |
| `gripperROI` | `RoiboxData[]` | 安全玉米对应的夹爪模拟框（中心=玉米中心，长宽=夹爪尺寸，角度=方向角） |
| `collisionCount` | `int` | 因碰撞被排除的玉米数量 |
| `collisionBlobROI` | `RoiboxData[]` | 因碰撞被排除的玉米外接矩形ROI数组 |
| `debugInfo` | `string` | 调试信息 |

> **几何创建**：脚本2之后添加以下几何创建模块：
> 1. **几何创建-方向线**（直线）：起点订阅 `safeCenterXArr/YArr`，终点订阅 `safeTipXArr/YArr`，输出**直线角度**供标定转换使用，同时在图像上可视化玉米方向
> 2. **几何创建-安全框**（矩形）：订阅 `safeBlobROI`（绿色）
> 3. **几何创建-夹爪框**（矩形）：订阅 `gripperROI`（蓝色）
> 4. **几何创建-碰撞框**（矩形）：订阅 `collisionBlobROI`（红色）

```csharp
using System;
using System.Text;
using System.Collections.Generic;
using Script.Methods;

public partial class UserScript : ScriptMethods, IProcessMethods
{
    int processCount;

    public void Init()
    {
        processCount = 0;
    }

    public bool Process()
    {
        try
        {
            // ========== 1. 从全局变量读取参数 ==========
            float gripperLength = 120f;
            float gripperWidth = 60f;
            int sampleStep = 3;
            int whiteThresh = 200;
            int boxExpand = 30;

            object val;
            val = GlobalVariableModule.GetValue("GripperLength");
            if (val != null) float.TryParse(val.ToString(), out gripperLength);
            val = GlobalVariableModule.GetValue("GripperWidth");
            if (val != null) float.TryParse(val.ToString(), out gripperWidth);
            val = GlobalVariableModule.GetValue("CollisionSampleStep");
            if (val != null) int.TryParse(val.ToString(), out sampleStep);
            val = GlobalVariableModule.GetValue("WhiteThreshold");
            if (val != null) int.TryParse(val.ToString(), out whiteThresh);
            val = GlobalVariableModule.GetValue("BoxExpandPixel");
            if (val != null) int.TryParse(val.ToString(), out boxExpand);

            if (sampleStep < 1) sampleStep = 1;

            // ========== 2. 读取脚本1的有效玉米数据 ==========
            int vCount = 0;
            GetIntValue("validCount", ref vCount);

            float[] vcx = new float[200];
            float[] vcy = new float[200];
            float[] vArea = new float[200];
            int cnt;
            GetFloatArrayValue("validCenterXArr", ref vcx, out cnt);
            GetFloatArrayValue("validCenterYArr", ref vcy, out cnt);
            GetFloatArrayValue("validAreaArr", ref vArea, out cnt);

            RoiboxData[] vROI = new RoiboxData[vCount > 0 ? vCount : 1];
            int roiCnt = vCount;
            GetRoiBoxArrayValue("validBlobROI", ref vROI, out roiCnt);

            // ========== 3. 读取DL检测的小头数据 ==========
            int tCount = 0;
            GetIntValue("tipCount", ref tCount);

            float[] tcx = new float[200];
            float[] tcy = new float[200];
            GetFloatArrayValue("tipCenterXArr", ref tcx, out cnt);
            GetFloatArrayValue("tipCenterYArr", ref tcy, out cnt);

            // ========== 4. 读取二值化图像 ==========
            ImageData binImg = new ImageData();
            GetImageValue("binImg", ref binImg);
            byte[] imgBuf = binImg.Buffer;
            int imgW = binImg.Width;
            int imgH = binImg.Height;

            StringBuilder debug = new StringBuilder();
            debug.AppendLine("=== V3 小头匹配(旋转矩形)+全局最近距离+夹爪碰撞 ===");
            debug.AppendLine("有效玉米: " + vCount + ", 小头: " + tCount);
            debug.AppendLine(string.Format(
                "夹爪: L={0}, W={1}, step={2}, thresh={3}, expand={4}",
                gripperLength, gripperWidth, sampleStep, whiteThresh, boxExpand));

            // ========== 5. 小头-玉米框匹配（旋转矩形 + 全局最近距离） ==========

            // 5a. 收集所有候选配对：tip落在corn的旋转矩形内
            //     每个候选记录 (cornIdx, tipIdx, distSq)
            List<int> candCorn = new List<int>();
            List<int> candTip = new List<int>();
            List<float> candDist = new List<float>();

            for (int i = 0; i < vCount; i++)
            {
                float boxCx = vcx[i];
                float boxCy = vcy[i];
                float roiW = vROI[i].Width;
                float roiH = vROI[i].Height;
                float roiAngleRad = vROI[i].Angle * 3.14159f / 180f;
                float cosNeg = (float)Math.Cos(-roiAngleRad);
                float sinNeg = (float)Math.Sin(-roiAngleRad);
                float halfRoiW = roiW / 2f;
                float halfRoiH = roiH / 2f;

                debug.AppendLine(string.Format(
                    "  玉米#{0}: center=({1:F1},{2:F1}), ROI={3:F0}x{4:F0}, angle={5:F1}°",
                    i, boxCx, boxCy, roiW, roiH, vROI[i].Angle));

                for (int t = 0; t < tCount; t++)
                {
                    // 将tip坐标转到ROI的局部坐标系
                    float dx = tcx[t] - boxCx;
                    float dy = tcy[t] - boxCy;
                    float localX = dx * cosNeg - dy * sinNeg;
                    float localY = dx * sinNeg + dy * cosNeg;

                    // 判断是否在旋转矩形内
                    if (Math.Abs(localX) <= halfRoiW && Math.Abs(localY) <= halfRoiH)
                    {
                        float distSq = dx * dx + dy * dy;
                        candCorn.Add(i);
                        candTip.Add(t);
                        candDist.Add(distSq);

                        debug.AppendLine(string.Format(
                            "    候选: tip#{0}({1:F1},{2:F1}) local=({3:F1},{4:F1}) dist²={5:F0}",
                            t, tcx[t], tcy[t], localX, localY, distSq));
                    }
                }
            }

            // 5b. 按距离从小到大排序候选配对的索引
            int candCount = candCorn.Count;
            int[] sortIdx = new int[candCount];
            for (int k = 0; k < candCount; k++) sortIdx[k] = k;

            // 简单选择排序（候选数量不会太大）
            for (int a = 0; a < candCount - 1; a++)
            {
                int minIdx = a;
                for (int b = a + 1; b < candCount; b++)
                {
                    if (candDist[sortIdx[b]] < candDist[sortIdx[minIdx]])
                        minIdx = b;
                }
                if (minIdx != a)
                {
                    int tmp = sortIdx[a];
                    sortIdx[a] = sortIdx[minIdx];
                    sortIdx[minIdx] = tmp;
                }
            }

            // 5c. 全局最近优先匹配
            int[] matchedTipIdx = new int[vCount > 0 ? vCount : 1];
            bool[] cornMatched = new bool[vCount > 0 ? vCount : 1];
            bool[] tipUsed = new bool[tCount > 0 ? tCount : 1];

            for (int i = 0; i < vCount; i++)
            {
                matchedTipIdx[i] = -1;
                cornMatched[i] = false;
            }

            for (int k = 0; k < candCount; k++)
            {
                int si = sortIdx[k];
                int ci = candCorn[si];
                int ti = candTip[si];

                if (cornMatched[ci] || tipUsed[ti]) continue;

                matchedTipIdx[ci] = ti;
                cornMatched[ci] = true;
                tipUsed[ti] = true;

                debug.AppendLine(string.Format(
                    "  ✓ 玉米#{0} 匹配小头#{1} (dist²={2:F0})",
                    ci, ti, candDist[si]));
            }

            // 输出未匹配信息
            for (int i = 0; i < vCount; i++)
            {
                if (!cornMatched[i])
                    debug.AppendLine(string.Format(
                        "  ✗ 玉米#{0} 无匹配小头", i));
            }
            for (int t = 0; t < tCount; t++)
            {
                if (!tipUsed[t])
                    debug.AppendLine(string.Format(
                        "  ⚠ 小头#{0}({1:F1},{2:F1}) 未匹配", t, tcx[t], tcy[t]));
            }

            // ========== 6. 方向计算 + 夹爪碰撞检测 ==========
            List<int> safeIndices = new List<int>();
            List<int> collisionIndices = new List<int>();
            float[] dirAngles = new float[vCount > 0 ? vCount : 1];

            float halfL = gripperLength / 2f;
            float halfW = gripperWidth / 2f;

            for (int i = 0; i < vCount; i++)
            {
                if (!cornMatched[i]) continue;

                int tIdx = matchedTipIdx[i];
                float cx = vcx[i];
                float cy = vcy[i];

                // 方向计算：小头中心 - 玉米中心
                float dirX = tcx[tIdx] - cx;
                float dirY = tcy[tIdx] - cy;
                float dirLen = (float)Math.Sqrt(dirX * dirX + dirY * dirY);

                if (dirLen < 1f)
                {
                    debug.AppendLine(string.Format(
                        "  ⚠ 玉米#{0} 小头与中心重合，跳过", i));
                    continue;
                }

                float udx = dirX / dirLen;
                float udy = dirY / dirLen;
                float perpX = -udy;
                float perpY = udx;
                float angle = (float)Math.Atan2(dirY, dirX);
                dirAngles[i] = angle;

                // 夹爪矩形四顶点 → AABB
                float p0x = cx + udx * halfL + perpX * halfW;
                float p0y = cy + udy * halfL + perpY * halfW;
                float p1x = cx + udx * halfL - perpX * halfW;
                float p1y = cy + udy * halfL - perpY * halfW;
                float p2x = cx - udx * halfL - perpX * halfW;
                float p2y = cy - udy * halfL - perpY * halfW;
                float p3x = cx - udx * halfL + perpX * halfW;
                float p3y = cy - udy * halfL + perpY * halfW;

                int startX = Math.Max(0, (int)Math.Min(Math.Min(p0x, p1x), Math.Min(p2x, p3x)));
                int endX = Math.Min(imgW - 1, (int)Math.Max(Math.Max(p0x, p1x), Math.Max(p2x, p3x)));
                int startY = Math.Max(0, (int)Math.Min(Math.Min(p0y, p1y), Math.Min(p2y, p3y)));
                int endY = Math.Min(imgH - 1, (int)Math.Max(Math.Max(p0y, p1y), Math.Max(p2y, p3y)));

                // 自身Blob排除框：用旋转矩形局部坐标判断（与匹配逻辑一致），外扩boxExpand像素
                float selfAngleRad = vROI[i].Angle * 3.14159f / 180f;
                float selfCosNeg = (float)Math.Cos(-selfAngleRad);
                float selfSinNeg = (float)Math.Sin(-selfAngleRad);
                float selfHalfW = vROI[i].Width / 2f + boxExpand;
                float selfHalfH = vROI[i].Height / 2f + boxExpand;

                // 碰撞采样
                bool collision = false;
                for (int py = startY; py <= endY && !collision; py += sampleStep)
                {
                    for (int px = startX; px <= endX && !collision; px += sampleStep)
                    {
                        // 判断采样点是否在夹爪旋转矩形内
                        float dxG = px - cx;
                        float dyG = py - cy;
                        float localXG = dxG * udx + dyG * udy;
                        float localYG = dxG * perpX + dyG * perpY;

                        if (localXG >= -halfL && localXG <= halfL &&
                            localYG >= -halfW && localYG <= halfW)
                        {
                            int pixelVal = (int)imgBuf[py * imgW + px];
                            if (pixelVal >= whiteThresh)
                            {
                                // 判断是否为自身Blob的像素（旋转矩形局部坐标判断，外扩boxExpand）
                                float dxS = px - cx;
                                float dyS = py - cy;
                                float selfLocalX = dxS * selfCosNeg - dyS * selfSinNeg;
                                float selfLocalY = dxS * selfSinNeg + dyS * selfCosNeg;

                                if (Math.Abs(selfLocalX) <= selfHalfW &&
                                    Math.Abs(selfLocalY) <= selfHalfH)
                                    continue; // 自身像素，跳过

                                collision = true;
                                debug.AppendLine(string.Format(
                                    "  ✗ 玉米#{0} 碰撞@({1},{2})", i, px, py));
                            }
                        }
                    }
                }

                if (!collision)
                {
                    safeIndices.Add(i);
                    debug.AppendLine(string.Format(
                        "  ✓ 玉米#{0} 安全，角度={1:F1}°",
                        i, angle * 180f / 3.14159f));
                }
                else
                {
                    collisionIndices.Add(i);
                }
            }

            // ========== 7. 输出安全玉米 ==========
            int safeCount = safeIndices.Count;
            int collisionCount = collisionIndices.Count;
            debug.AppendLine("安全可抓取数: " + safeCount + ", 碰撞排除数: " + collisionCount);

            float[] safeCx = new float[safeCount];
            float[] safeCy = new float[safeCount];
            float[] safeTipX = new float[safeCount];
            float[] safeTipY = new float[safeCount];
            float[] safeAngle = new float[safeCount];
            float[] safeArea = new float[safeCount];
            RoiboxData[] safeROI = new RoiboxData[safeCount > 0 ? safeCount : 1];
            RoiboxData[] gripROI = new RoiboxData[safeCount > 0 ? safeCount : 1];

            for (int k = 0; k < safeCount; k++)
            {
                int idx = safeIndices[k];
                safeCx[k] = vcx[idx];
                safeCy[k] = vcy[idx];
                safeAngle[k] = dirAngles[idx];
                safeArea[k] = vArea[idx];
                safeROI[k] = vROI[idx];

                // 输出匹配的小头中心坐标（供几何创建画方向线）
                int tIdx = matchedTipIdx[idx];
                safeTipX[k] = tcx[tIdx];
                safeTipY[k] = tcy[tIdx];

                // 夹爪模拟框：中心=玉米中心，长宽=夹爪尺寸，角度=方向角(度)
                gripROI[k] = new RoiboxData();
                gripROI[k].CenterX = vcx[idx];
                gripROI[k].CenterY = vcy[idx];
                gripROI[k].Width = gripperLength;
                gripROI[k].Height = gripperWidth;
                gripROI[k].Angle = dirAngles[idx] * 180f / 3.14159f;
            }

            SetIntValue("safeCount", safeCount);
            SetFloatArrayValue("safeCenterXArr", safeCx, 0, safeCount);
            SetFloatArrayValue("safeCenterYArr", safeCy, 0, safeCount);
            SetFloatArrayValue("safeTipXArr", safeTipX, 0, safeCount);
            SetFloatArrayValue("safeTipYArr", safeTipY, 0, safeCount);
            SetFloatArrayValue("safeAngleArr", safeAngle, 0, safeCount);
            SetFloatArrayValue("safeAreaArr", safeArea, 0, safeCount);
            SetRoiBoxArrayValue("safeBlobROI", safeROI, 0, safeCount);
            SetRoiBoxArrayValue("gripperROI", gripROI, 0, safeCount);

            // 输出碰撞排除框
            RoiboxData[] collisionROI = new RoiboxData[collisionCount > 0 ? collisionCount : 1];
            for (int k = 0; k < collisionCount; k++)
            {
                collisionROI[k] = vROI[collisionIndices[k]];
            }
            SetIntValue("collisionCount", collisionCount);
            SetRoiBoxArrayValue("collisionBlobROI", collisionROI, 0, collisionCount);

            SetStringValue("debugInfo", debug.ToString());
            ConsoleWrite(debug.ToString());

            // ========== 8. 白色占比计算和种子数量统计 ==========
            // 统计二值化图像中白色像素数量（灰度值 >= whiteThresh）
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
            GlobalVariableModule.SetValue("WhiteAreaRatio", whiteAreaRatio.ToString("F2"));
            GlobalVariableModule.SetValue("BlobNum", safeCount.ToString());

            debug.AppendLine(string.Format(
                "白色占比: {0:F2}%, 种子数量: {1}", whiteAreaRatio, safeCount));
            ConsoleWrite(string.Format("白色占比: {0:F2}%, 种子数量: {1}", whiteAreaRatio, safeCount));

            processCount++;
            return true;
        }
        catch (Exception ex)
        {
            ConsoleWrite("脚本2异常: " + ex.Message + "\n" + ex.StackTrace);
            SetIntValue("safeCount", 0);
            SetIntValue("collisionCount", 0);
            SetStringValue("debugInfo", "异常: " + ex.Message);
            return false;
        }
    }
}
```

---

## 参数标定参考

| 参数 | 标定方法 |
|------|----------|
| `AreaMin` | 统计20颗最小玉米面积，取最小值×80% |
| `AreaMax` | 统计20颗最大玉米面积，取最大值×130% |
| `CompactnessMax` | 初始1.6，通过debug输出微调，单颗≈1.0~1.4，粘连≈1.6~3.0+ |
| `AspectRatioMax` | 建议2.5~3.5 |
| `GripperLength/Width` | 实际夹爪尺寸(mm) / 像素当量(mm/px)，建议放大10%~20%作安全余量 |
| `BoxExpandPixel` | 自身排除框外扩像素，建议20~40，值越大对边缘越宽容 |
| `CollisionSampleStep` | 建议2~5，值越小越精确越慢 |
| `WhiteThreshold` | 根据二值化图白色灰度设定，一般200 |

---

## 更新后的 VM 流程（取消协议组装模块）

> 原流程用"协议组装模块 + 附加脚本"拼字符串，逻辑分散且难以维护。
> 现在用一个脚本模块（脚本3）直接替代，从标定转换的输出一步到位拼成 AR 所需协议。

```
图像源 → 二值化 → 开运算(腐蚀3×3 + 膨胀3×3)
                      ↓
                Blob分析1(面积/中心/外接矩形/周长/BlobROI)
                      ↓
                脚本1(多特征粘连筛选 → validBlobROI[])
                      ↓
                DL目标检测(玉米小头 → 小头中心X/Y)
                      ↓
                脚本2(小头匹配 + 方向 + 夹爪碰撞 → safeCenterXY/safeTipXY)
                      ↓
                几何创建-方向线(safeCenterXY → safeTipXY, 输出直线角度)  ← ★ 可视化+角度
                      ↓
                标定转换(safeCenterXY + 直线角度 → worldXArr/YArr/AngleArr)
                      ↓
                脚本3(协议组装 → protocolStr)      ← ★ 替代协议组装模块
                      ↓
                发送数据模块(订阅 protocolStr → 发送给 AR)
                      ↓
                几何创建(订阅 safeBlobROI / gripperROI / collisionBlobROI)
```

### 标定转换模块 配置说明

标定转换模块订阅脚本2的安全玉米像素坐标和几何创建的直线角度，输出机械坐标。配置如下：

| 输入项 | 绑定来源 | 说明 |
|--------|----------|------|
| 输入X数组 | 脚本模块2.safeCenterXArr | 安全玉米像素中心X |
| 输入Y数组 | 脚本模块2.safeCenterYArr | 安全玉米像素中心Y |
| 输入角度数组 | 几何创建-方向线.直线角度数组 | 从玉米中心到小头的方向角 |

> **为什么用几何创建的角度而不是脚本2的 safeAngleArr？**
> - 脚本2 内部用 `Math.Atan2` 算的弧度角度，仅供碰撞检测使用
> - 几何创建模块输出的直线角度与 VM 标定转换的角度输入格式天然一致，无需手动转换
> - 方向线在图像上可直接看到，方便确认角度是否正确

---

## 脚本3：协议组装 + 坐标超限检测（替代协议组装模块 + 旧附加脚本）

**功能**：将标定转换后的机械坐标数组格式化为 AR 协议字符串，同时检测坐标是否超出机器人安全空间，防止标定转换误差导致的超限报警。

### AR 协议格式说明

根据 AR 程序 `processVisionData()` 的解析逻辑，协议格式如下：

**抓取坐标协议**（主流程 → HeadCam 通道）：
```
X1;Y1;C1
X2;Y2;C2
X3;Y3;C3
```
- 每行一个抓取点，分号分隔
- X、Y 为机械坐标（mm），C 为角度（度）
- 多行之间用 `\n` 换行
- 无可抓点时发送字符串 `NO_POINTS`

### 输入变量

| 变量名 | 类型 | 绑定来源 |
|--------|------|----------|
| `safeCount` | `int` | 脚本模块2.safeCount |
| `worldXArr` | `float[]` | 标定转换.输出X数组 |
| `worldYArr` | `float[]` | 标定转换.输出Y数组 |
| `worldAngleArr` | `float[]` | 标定转换.输出角度数组 |

### 输出变量

| 变量名 | 类型 | 说明 |
|--------|------|------|
| `protocolStr` | `string` | 组装后的协议字符串，供发送数据模块订阅 |
| `debugInfo` | `string` | 调试信息 |

### 全局变量

```xml
    <!-- ==================== 脚本3参数：协议组装 + 坐标检测 ==================== -->
    <VarItem Index="28" Name="%AngleOffset%" Type="float" CommEnable="false" Combination="false" Remark="角度全局补偿(度)，用于微调" GroupInfo="分组3" Guid="a1b2c3d4-aa02-4aaa-b002-00000000003">
        <Value>0.000000</Value>
    </VarItem>

    <!-- ==================== 坐标超限检测参数 ==================== -->
    <VarItem Index="31" Name="%SafeMinX%" Type="float" CommEnable="false" Combination="false" Remark="X坐标安全范围下限(mm)" GroupInfo="分组3" Guid="a1b2c3d4-aa03-4aaa-b003-00000000003">
        <Value>-50.000000</Value>
    </VarItem>
    <VarItem Index="32" Name="%SafeMaxX%" Type="float" CommEnable="false" Combination="false" Remark="X坐标安全范围上限(mm)" GroupInfo="分组3" Guid="a1b2c3d4-aa04-4aaa-b004-00000000003">
        <Value>50.000000</Value>
    </VarItem>
    <VarItem Index="33" Name="%SafeMinY%" Type="float" CommEnable="false" Combination="false" Remark="Y坐标安全范围下限(mm)" GroupInfo="分组3" Guid="a1b2c3d4-aa05-4aaa-b005-00000000003">
        <Value>-50.000000</Value>
    </VarItem>
    <VarItem Index="34" Name="%SafeMaxY%" Type="float" CommEnable="false" Combination="false" Remark="Y坐标安全范围上限(mm)" GroupInfo="分组3" Guid="a1b2c3d4-aa06-4aaa-b006-00000000003">
        <Value>50.000000</Value>
    </VarItem>
    <VarItem Index="35" Name="%CoordinateOutOfBound%" Type="int" CommEnable="false" Combination="false" Remark="坐标超限检测结果（0=正常, 1=超限）" GroupInfo="分组3" Guid="a1b2c3d4-aa07-4aaa-b007-00000000003">
        <Value>0</Value>
    </VarItem>
```

### 脚本代码

```csharp
using System;
using System.Text;
using System.Collections.Generic;
using Script.Methods;

public partial class UserScript : ScriptMethods, IProcessMethods
{
    int processCount;

    public void Init()
    {
        processCount = 0;
    }

    public bool Process()
    {
        try
        {
            // ========== 1. 从全局变量读取参数 ==========
            float angleOffset = 0f;

            object val;
            val = GlobalVariableModule.GetValue("AngleOffset");
            if (val != null) float.TryParse(val.ToString(), out angleOffset);

            // 读取坐标安全范围参数
            object minXObj = GlobalVariableModule.GetValue("SafeMinX");
            object maxXObj = GlobalVariableModule.GetValue("SafeMaxX");
            object minYObj = GlobalVariableModule.GetValue("SafeMinY");
            object maxYObj = GlobalVariableModule.GetValue("SafeMaxY");

            float safeMinX = Convert.ToSingle(minXObj ?? -50.0f);
            float safeMaxX = Convert.ToSingle(maxXObj ?? 50.0f);
            float safeMinY = Convert.ToSingle(minYObj ?? -50.0f);
            float safeMaxY = Convert.ToSingle(maxYObj ?? 50.0f);

            // ========== 2. 读取输入数据 ==========
            // worldXArr / worldYArr / worldAngleArr 来自标定转换模块
            // 标定转换的角度输入来自几何创建-方向线的直线角度，单位已统一
            int safeCount = 0;
            GetIntValue("safeCount", ref safeCount);

            float[] wx = new float[200];
            float[] wy = new float[200];
            float[] wAngle = new float[200];
            int cnt;
            GetFloatArrayValue("worldXArr", ref wx, out cnt);
            GetFloatArrayValue("worldYArr", ref wy, out cnt);
            GetFloatArrayValue("worldAngleArr", ref wAngle, out cnt);

            StringBuilder debug = new StringBuilder();
            debug.AppendLine("=== 脚本3：坐标检测 + 协议组装 ===");
            debug.AppendLine("安全玉米数: " + safeCount);
            debug.AppendLine("角度补偿: " + angleOffset + "°");
            debug.AppendLine(string.Format("安全范围: X[{0:F1}~{1:F1}], Y[{2:F1}~{3:F1}]",
                safeMinX, safeMaxX, safeMinY, safeMaxY));

            // ========== 3. 坐标超限检测 ==========
            int hasOutOfBound = 0;  // 0=正常, 1=超限
            List<int> validIndices = new List<int>();

            for (int i = 0; i < safeCount; i++)
            {
                bool isValid = (wx[i] >= safeMinX && wx[i] <= safeMaxX &&
                               wy[i] >= safeMinY && wy[i] <= safeMaxY);

                if (isValid)
                {
                    validIndices.Add(i);
                }
                else
                {
                    hasOutOfBound = 1;
                    debug.AppendLine(string.Format(
                        "  ✗ 坐标超限: 点#{0} X={1:F2}, Y={2:F2}", i, wx[i], wy[i]));
                    ConsoleWrite(string.Format(
                        "坐标超限: 点#{0} X={1:F2}, Y={2:F2}", i, wx[i], wy[i]));
                }
            }

            // 设置检测结果到全局变量
            GlobalVariableModule.SetValue("CoordinateOutOfBound", hasOutOfBound);

            int validCount = validIndices.Count;
            debug.AppendLine("有效坐标数: " + validCount + "/" + safeCount);

            // ========== 4. 无可抓点 → 发送 NO_POINTS ==========
            if (validCount <= 0)
            {
                debug.AppendLine("无有效坐标点位，输出 NO_POINTS");
                SetStringValue("protocolStr", "NO_POINTS");
                SetStringValue("debugInfo", debug.ToString());
                ConsoleWrite(debug.ToString());
                processCount++;
                return true;
            }

            // ========== 5. 组装协议字符串（仅使用有效坐标） ==========
            // AR协议格式: 每行 X;Y;C（分号分隔），多行用 \n 换行
            StringBuilder protocol = new StringBuilder();

            for (int k = 0; k < validCount; k++)
            {
                int idx = validIndices[k];
                float x = wx[idx];
                float y = wy[idx];
                float angle = wAngle[idx] + angleOffset;

                // 格式: X;Y;C
                string line = string.Format("{0:F3};{1:F3};{2:F2}", x, y, angle);

                if (k > 0)
                    protocol.Append("\n");
                protocol.Append(line);

                debug.AppendLine(string.Format(
                    "  ✓ 点#{0}: X={1:F3}, Y={2:F3}, C={3:F2}°", idx, x, y, angle));
            }

            string result = protocol.ToString();

            debug.AppendLine("协议字符串长度: " + result.Length);
            debug.AppendLine("--- 协议内容 ---");
            debug.AppendLine(result);
            debug.AppendLine("--- 结束 ---");

            // ========== 6. 输出 ==========
            SetStringValue("protocolStr", result);
            SetStringValue("debugInfo", debug.ToString());
            ConsoleWrite(debug.ToString());

            processCount++;
            return true;
        }
        catch (Exception ex)
        {
            ConsoleWrite("脚本3异常: " + ex.Message + "\n" + ex.StackTrace);
            GlobalVariableModule.SetValue("CoordinateOutOfBound", 1);  // 异常时设置为超限
            SetStringValue("protocolStr", "NO_POINTS");
            SetStringValue("debugInfo", "异常: " + ex.Message);
            return false;
        }
    }
}
```

### 发送数据模块配置

脚本3后面接一个**发送数据模块**，配置如下：

| 配置项 | 设置 |
|--------|------|
| 通信设备 | 选择连接 AR 的 TCP 设备（即 HeadCam 通道对应的设备） |
| 发送内容 | 订阅 → 脚本模块3.protocolStr |
| 数据类型 | String |

> **说明**：不再需要协议组装模块。脚本3 直接输出完整协议字符串，发送数据模块只负责"透传"即可。