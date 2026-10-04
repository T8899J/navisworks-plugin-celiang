# Splice Connector 桥接

`Horizontal / Vertical (Adjustable) Splice Connector` 是两根桥架之间的铰接连接件，没有可重建的中心线。此前它们被判为 Unknown，跨过它们的间隙只能由待复核的 VirtualConnector 补上。现在识别为连接件提示，在满足严格条件时把它连接的两个桥架端口直接接成真实连接。

## 识别

`FittingGeometry.Classify` 对 Description / Name 中的 `splice`（单词边界）、`连接片`、`连接板` 返回 `SpliceConnector`。宿主建图时这类构件：

- 不重建网格、不占 2000 构件上限；
- 以世界坐标包围盒和 Size 解析出的标称宽高生成 `CableJointHint`；
- 仍列在 Rejected 中，原因 `SPLICE_CONNECTOR_HINT`，便于核对。

## 桥接条件

在物理连接建立之后、求真实连通分量之前执行，全部满足才桥接：

1. 包围盒外扩 30mm 内的自由端口，按标称截面筛选：端口高度与标称高度相差不超过 10mm，宽度不小于标称宽度减 10mm（实测宽度含翼缘）。穿过盒子的小规格桥架被忽略，仍可参与虚拟候选。
2. 筛选后恰好 2 个端口，且属于不同构件。多于 2 个、同一构件、端口已被其他连接件占用时不桥接，写入 Ambiguities。
3. 两个端口都朝向对方（`Outward · Δ ≥ 0`）。可调连接件转角可达约 100°，因此不设更窄的角度限制。

满足条件的连接为 `Kind="splice-bridge"` 的 ConnectionEdge：`RequiresReview=false`，长度为两端口实际三维距离，计入 TotalLength 和 VerticalTravel，参与真实分量计算，不计入 VirtualConnectorCount。严格模式 `Find(start, finish, false)` 可以通过。路线报告新增 `SpliceBridgeCount`、`SpliceBridgeLength`；宿主报告新增 `spliceBridges`。

## 相关诊断与过滤

- 报告新增 `rejectedNearRoute`（路线 1m 内的被拒构件）和 `virtualConnectorSuspects`（每条虚拟连接 0.5m 内的被拒构件），用于定位虚拟连接在给哪些构件补洞。
- `VirtualConnectorRejectParallelOffset`（当前开启）：仅标记局部宽度/高度偏移的 `ParallelOffsetRisk`，不删除虚拟候选；具体规则见 [VIRTUAL_CONNECTOR.md](VIRTUAL_CONNECTOR.md)。
- `VirtualConnectorMaxAngle`（当前 0）：保留兼容配置；候选准入已统一使用前向半空间，不使用固定锥角。
- 诊断 Top-N 与自动入图分离：前向合法端口优先，每端口最多一个自动 VirtualConnector；平行偏移仅记录风险，候选继续保持待复核。Splice 桥接算法不受此准入修改影响。

## 空间范围建图

拾取起终点后，只提取起终点包围盒外扩 `SpatialRegionMargin`（20m）范围内的构件；找不到路时范围加倍，直到 `SpatialRegionMaxMargin`（160m）、图已截断或范围外已无构件。达到构件上限时按到起终点线段的距离由近到远保留，嵌套构件组共享最外层的距离、组内仍按深度优先。重放脚本使用 `-Spatial` 开启。

## 实测记录（2026-10-04）

端口图检查 692/692、共享几何检查 31/31 通过。TS-M09F9.29(全）.nwd 同一查询，独立只读宿主重放：

| 指标 | V13 | 本阶段 |
| --- | ---: | ---: |
| TotalLength | 19.222379 m | 19.222379 m |
| VerticalTravel | 11.256059 m | 11.256059 m |
| VirtualConnectorCount | 9 | 4 |
| SpliceBridgeCount | — | 5 |
| 真实分量 | 928 | 862 |

全模型 167 个 Splice：79 个桥接，28 个歧义。总长不变，说明桥接替换的正是原虚拟连接经过的同一几何间隙。剩余 4 条虚拟连接都跨过几何重建失败的梯式直段（`Straight Lengths ... 300mm rung`，`STRAIGHT_SECTION_FAILED`），需修复直段重建后才能消除。该模型仍为 2000 构件局部图（`incomplete=true`），路线仍需复核。

## 短直段重建实验：默认关闭（2026-10-04）

路线上剩余的 4 条虚拟连接都跨过两个几何重建失败的梯式直段（`TRAY-0232` 0.228m、`TRAY-0018` 0.253m）。两者都是 150×150 桥架的截短件，长度接近截面尺寸，PCA 无法确定纵向轴。

新增 `StraightMeasurement.MeasureFrame`：按调用方给定的正交标架测量，并新增 `NominalStub` 用主面法向两两组合试标架，只接受截面与 `Size` 标称值吻合、且长度大于自身截面 1.15 倍的唯一解。实测这两个真实构件都能正确重建（轴分别沿 Z 和 X，长度 0.2280 / 0.2527 m，正反向与本机一致）。

单个构件上修复是有效的：`TRAY-0232` 与 `0212` 形成 0.0001m 真实连接，其自由端口到 `0230` 的虚拟距离 0.185m，比原来 `0212→0230` 的 0.409m 更短。

但整条路线反而变差。同一查询、同一 DLL、只切换 `StraightStubByNominalSection`：

| 配置 | 已识别 | 真实分量 | 总长 | 虚拟连接 | Splice 桥接 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 关闭（默认） | 2000 | 847 | 19.222379 m | 4 | 5 |
| 开启 | 2000 | 855 | 39.883023 m | 12 | 6 |
| 关闭，上限 4000 | 2372 | 1008 | 19.222379 m | 4 | 5 |
| 开启，上限 4000 | 2430 | 1027 | 39.883024 m | 11 | 6 |

上限 4000 时图已完整（`incomplete=false`），结论与上限无关。开启后多识别 58 个构件，其中 28 个不足 10cm、多为 5cm 级的 `Cableway Straight Feature` 碎片。

原因是端口图的连通质量：这批碎片各自带来自由端口，虚拟候选按每个端口 Top-5 排名，而 `BuildJoins` 对同一 socket 的多个候选直接丢弃（`端口存在重叠候选` 由 29 增至 43）。碎片挤掉了原路线依赖的 `0128→0230`、`0002→0128`，`0128` 的端口被一条 Splice 桥接占用后剩余端口 0.5m 内无候选，链路断开，寻路改走 39.883m 的绕行。

因此该开关默认关闭。要真正消掉这 4 条虚拟连接，需要先改善掉件导致的图碎片化（当前 2000 个构件对应 847 个真实分量，平均每个分量约 2.4 个构件），而不是继续放宽单个构件的识别。
