# 直线桥架 Port-to-Port 断截桥接

严格 GapBridge 只桥接两个直线桥架（包含垂直或空间倾斜直段）的空闲端口。现有 Tee、branch-to-middle、弯头识别和物理连接规则不因 Gap 配置而放宽。

## 配置

项目根目录的 `cable-path-settings.json` 在构建时复制到 DLL 同目录，Navisworks 建图时读取该副本。距离单位均为世界坐标中的米。调整源配置后重新构建，并重新打开实验窗口以重建网络。

| 配置项 | 当前值 | 含义 |
| --- | ---: | --- |
| GapBridgeMaxDistance | 0.05 | 最大断截距离 50mm；设为 0 关闭桥接 |
| GapBridgeWidthAxisTolerance | 0.002 | 两端 WidthAxis 上的最大偏移 2mm |
| GapBridgeHeightAxisTolerance | 0.002 | 两端 HeightAxis 上的最大偏移 2mm |
| GapBridgeSizeTolerance | 0.003 | 几何宽度和高度分别允许相差 3mm |

PhysicalTolerance 保持 0.002m，不受 GapBridgeMaxDistance 影响。原有直接创建 CableNetwork 的调用默认关闭 Gap 桥接；需要时显式传入 CableNetworkOptions。

```csharp
var network = new CableNetwork(pieces, options: new CableNetworkOptions {
    GapBridgeMaxDistance = 0.05
});
```

## 接受条件

- 两个构件均有真实直线中心线、两个 Port 和有效截面坐标轴。
- 两端都没有物理连接；存在物理连接歧义的端口也不会进入 Gap 搜索。
- Port 相向，位移分别沿两个 Port 的向外纵向，角度偏差不超过现有 3 度容差。
- 横向、竖向偏移在各自容差内，宽高兼容，截面朝向兼容。
- 世界坐标距离大于 2mm 且不超过 GapBridgeMaxDistance。
- 完整候选集合中，两端各自都恰好只有一个合法候选。多个候选全部拒绝并写入 Ambiguities。

## 计长与寻路

GapBridgeEdge 的 Length 为两个 Port 世界坐标的实际距离，并计入总长度。它始终 RequiresReview=true；ReviewReason 包含 gap、横向偏差和竖向偏差。报告中的 GapBridgeCount、GapBridgeLength 分别记录本路径的桥接数量和实际桥接总长。

V13 在开启或关闭 VirtualConnector 时均按 `(TotalLength, VerticalTravel, VirtualConnectorCount, VirtualConnectorTotalLength, GapBridgeCount)` 排序。实际总长度是第一目标；Gap 数量仅在前四项完全相同时比较。`VerticalTravel` 是实际经过中心折线的每段绝对 Z 变化之和。严格模式 `Find(start, finish, false)` 仍排除 Gap 及其他待复核边；本阶段未放宽任何 Gap 接受条件或容差。成本定义见 [PATH_COST.md](PATH_COST.md)，三维候选生成见 [VIRTUAL_CONNECTOR.md](VIRTUAL_CONNECTOR.md)。

## 验证

```powershell
dotnet run --project tests\CoreChecks.csproj
dotnet run --project tests\PortGraphChecks.csproj -- artifacts\gap-bridge-stage
.\build_cable_path_experiment.ps1
.\scripts\verify-cable-graph-host.ps1 -Manifest .\artifacts\gap-bridge-stage\gap-bridge-fixture.json -Model .\artifacts\gap-bridge-stage\gap-bridge-fixture.ifc -Report .\artifacts\gap-bridge-stage\gap-bridge-host.json
```

生成的 IFC 是合成验证模型，含不同名称和 RunName 的两个任意空间倾斜直段及 30mm 断截。它验证宿主提取和计长流程；真实模型中的电缆可穿行性及长度仍需要人工核实。
