# VirtualConnector 三维实验（V13）

继续使用现有 Port / Junction / Edge Graph。当前 DLL、拾取工具、渲染插件和宿主探针注册名均为 V13；唯一用户入口仍为 `启动电缆路径实验.bat`。当前将 Top-N 诊断与自动入图分离；V13 的 TotalLength-first 成本、配件几何及严格 GapBridge 保持原有规则。

## 配置

根目录 `cable-path-settings.json` 构建后复制到 DLL 同目录；改源配置后重新构建，关闭并重开实验窗口以重新建图。

| 配置 | 当前值 | 含义 |
| --- | ---: | --- |
| VirtualConnectorMaxDistance | 0.5 | 入图候选的独立三维距离上限，单位米；设 0 关闭 |
| VirtualConnectorExperimentalTopN | true | 保留兼容配置；开启或关闭都使用相同的单候选准入规则 |
| VirtualConnectorTopN | 5 | 每个自由 Port 的诊断/UI 候选数量，不控制入图数量 |
| VirtualConnectorMaxAngle | 0 | 保留兼容配置；准入不使用固定锥角，仅判断前向半空间 |
| VirtualConnectorRejectParallelOffset | true | 保留历史配置名；仅记录 `ParallelOffsetRisk`，不删除候选 |

PhysicalTolerance 仍为 2mm。严格 GapBridge 仍使用 50mm 距离、2mm WidthAxis / HeightAxis 偏差及原有尺寸/相向条件。真实占用、真实连接歧义、已接受 GapBridge 的端口不参与虚拟连接入图搜索。

先建立 InternalEdge / Physical Connection 图并求真实 connected components，再生成跨真实分量的虚拟候选。GapBridge 和 VirtualConnector 不参与这次分量计算；同一真实分量不生成虚拟连接捷径。

- **PortToPort3D**：两个自由 Port 世界 XYZ 之间的直线，长度 `sqrt(dx² + dy² + dz²)`。
- **PortToSegment3D**：自由 Port 对另一个 Straight / Slope InternalEdge 的每个 XYZ 线段作最近投影，选该边最近点 Q。station 严格在边内部时创建 VirtualJunction 并逻辑拆边；端部投影由 Port-to-Port 处理。

两种候选共同按实际 3D 距离形成诊断 Top-N；排名、距离、源/目标方向角、风险和状态全部保留。自动入图独立扫描搜索半径内的全部候选，不受诊断 Top-N 截断影响：每个源 Port 最多一个自动 VirtualConnector，优先最近的合法 PortToPort3D；只有没有合法端口候选时，才选择最近的合法 PortToSegment3D。即使合法端口排在五个更近的线段投影之后，也优先该端口；其完整诊断保存在选中图边的 `Join.VirtualConnector` 中。

合法候选必须满足 `dot(SourcePort.Outward, TargetPoint - SourcePoint) > 1e-7` 米。PortToPort3D 还要求目标 Port 面向源点，即目标端的反向投影也严格大于该 epsilon。方向已归一化；不使用 60° 等固定锥角。背向及纯侧向/纯垂直（前向投影为零）候选保留在诊断中，分别标记 `BehindSourcePort` 或 `BehindTargetPort`，不进入图。

PortToPort3D 占用两端各一个自动连接名额，要求双方互为最近合法端口。选择冲突时保留 `DiagnosticOnly`，不回退到线段或次近端口，避免目标端口因多个源端口的选择而获得多条自动边。其他未选候选均为 `DiagnosticOnly`；只有选中的连接标记 `Candidate` 并生成一条无向图边。多个不同源 Port 仍可各自接入主路不同 station。

候选新增 `ForwardOffset`、`WidthOffset`、`HeightOffset`，分别为目标点减源点在源 Port 的 `Outward`、`WidthAxis`、`HeightAxis` 单位方向上的有符号投影，单位米。宽高轴优先使用 Port，其次使用构件轴；两者都缺失时对应值为 `null`，不据此删除候选。局部高度不混入宽度偏移。

开启 `VirtualConnectorRejectParallelOffset` 时，近平行（轴夹角距 0° 或 180° 不超过 15°）候选的绝对宽度偏移超过双方最大宽度的一半，或绝对高度偏移超过双方最大高度的一半，标记 `ParallelOffsetRisk=true`。高度风险也覆盖端口垂直跳接到主路的诊断。风险本身不拒绝候选，不改变 Top-N 排名或寻路成本；候选及其图边始终 `RequiresReview=true`。有正向位移的高差、横向错位端部连接仍可入图。

## 待复核路径和失败诊断

全部 `VirtualConnectorEdge` 都是 `RequiresReview=true`、`Confirmed=false`、`Status=Candidate`，不确认物理可通行性。长度、坐标和高亮线段全部使用真实世界 XYZ。

Dijkstra 在所有模式下统一按 `(TotalLength, VerticalTravel, VirtualConnectorCount, VirtualConnectorTotalLength, GapBridgeCount)` 字典序选择路径。总长是第一目标，较短的虚拟连接不能使明显更长的路线胜出；只有总长完全相同时，才比较后续指标。总长包含实际经过的 InternalEdge、物理小间隙、GapBridge、VirtualConnector。`VerticalTravel` 沿全部实际经过的中心折线累计每段绝对 Z 变化，包括中途升高后下降；station 裁剪的边只累计经过部分。`Find(start, finish, false)` 继续排除全部待复核边。详细成本定义和验证方法见 [PATH_COST.md](PATH_COST.md)。

找到待复核路线后显示 **“候选路径，需要复核”**。未找到时抛出带 `CableConnectivityDiagnostics` 的 `CablePathNotFoundException`，记录起终真实分量、所有分量的边界自由端口，并为起点分量每个自由 Port 搜索全模型其他分量的最近 Top 5。失败诊断不受入图候选半径限制，也不改变图；半径外候选标记 `DiagnosticOnly`。

完整字段、红线定位、可见性验证和真实模型重放方法见 [CONNECTIVITY_DIAGNOSTICS.md](CONNECTIVITY_DIAGNOSTICS.md)。

## 兼容行为

直接调用 `new CableNetwork(...)` 默认关闭 VirtualConnector。显式开启半径后，两种 `VirtualConnectorExperimentalTopN` 配置均使用上述前向、端口优先、每端口最多一条自动连接的准入规则；不再因配置差异恢复全部 Top-N 入图或旧的唯一候选策略。

## 验证边界

当前测试包含不同名称、RunName、规格和任意 XYZ 倾斜的桥架，以及水平/带 Z 高差的 Port-to-Segment、多候选和失败断点。独立 Navisworks 合成宿主逐件核对中心线贡献、虚拟三维距离、严格 GapBridge 和真实小间隙，保留 10 微米几何阈值。

`NoVirtualTriangleShortcut` 合成端口图包含上方竖直桥架、下方竖直桥架及真实 bend/main 链。上、下端口互为最近相向端口，主路线段也在 0.5m 内；图中只允许 TopPort → LowerPort，主路投影保留为诊断，路线沿下方桥架及真实链行走。另覆盖前向 epsilon、超过旧锥角的错位端部、Top-N 外合法端口优先、多个源端口争用同一目标，以及独立 Top-5 JSON 字段检查。合成端口图检查不代表本轮已重新运行真实模型或 Navisworks GUI。

合成验证不替代真实工程模型的开口、可穿缆性和敷设长度验收。未识别的配件仍明确列出，不使用 BoundingBox 猜测长度；本阶段未增加 Tee、Reducer、Elbow、主路优先或其他工程规则。
