# VirtualConnector 三维实验（V13）

继续使用现有 Port / Junction / Edge Graph。当前 DLL、拾取工具、渲染插件和宿主探针注册名均为 V13；唯一用户入口仍为 `启动电缆路径实验.bat`。V12 的真实模型连通诊断、Top-N 实验策略和候选生成保持不变；V13 只修改寻路成本，配件几何及严格 GapBridge 保持原有规则。

## 配置

根目录 `cable-path-settings.json` 构建后复制到 DLL 同目录；改源配置后重新构建，关闭并重开实验窗口以重新建图。

| 配置 | 当前值 | 含义 |
| --- | ---: | --- |
| VirtualConnectorMaxDistance | 0.5 | 入图候选的独立三维距离上限，单位米；设 0 关闭 |
| VirtualConnectorExperimentalTopN | true | V12 多候选实验模式 |
| VirtualConnectorTopN | 5 | 每个自由 Port 保留的最近候选数量 |

PhysicalTolerance 仍为 2mm。严格 GapBridge 仍使用 50mm 距离、2mm WidthAxis / HeightAxis 偏差及原有尺寸/相向条件。真实占用、真实连接歧义、已接受 GapBridge 的端口不参与虚拟连接入图搜索。

先建立 InternalEdge / Physical Connection 图并求真实 connected components，再生成跨真实分量的虚拟候选。GapBridge 和 VirtualConnector 不参与这次分量计算；同一真实分量不生成虚拟连接捷径。

- **PortToPort3D**：两个自由 Port 世界 XYZ 之间的直线，长度 `sqrt(dx² + dy² + dz²)`。
- **PortToSegment3D**：自由 Port 对另一个 Straight / Slope InternalEdge 的每个 XYZ 线段作最近投影，选该边最近点 Q。station 严格在边内部时创建 VirtualJunction 并逻辑拆边；端部投影由 Port-to-Port 处理。

两种候选共同按实际 3D 距离排序。角度、Z 高差、截面尺寸差异只记录诊断，不新增硬条件。全部 Top-N 可以进入实验图；反向 Port-to-Port 记录对应的源排名，但同一个无向连接只建一条图边。多个源 Port 可分别接入主路不同 station。

## 待复核路径和失败诊断

全部 `VirtualConnectorEdge` 都是 `RequiresReview=true`、`Confirmed=false`、`Status=Candidate`，不确认物理可通行性。长度、坐标和高亮线段全部使用真实世界 XYZ。

Dijkstra 在所有模式下统一按 `(TotalLength, VerticalTravel, VirtualConnectorCount, VirtualConnectorTotalLength, GapBridgeCount)` 字典序选择路径。总长是第一目标，较短的虚拟连接不能使明显更长的路线胜出；只有总长完全相同时，才比较后续指标。总长包含实际经过的 InternalEdge、物理小间隙、GapBridge、VirtualConnector。`VerticalTravel` 沿全部实际经过的中心折线累计每段绝对 Z 变化，包括中途升高后下降；station 裁剪的边只累计经过部分。`Find(start, finish, false)` 继续排除全部待复核边。详细成本定义和验证方法见 [PATH_COST.md](PATH_COST.md)。

找到待复核路线后显示 **“候选路径，需要复核”**。未找到时抛出带 `CableConnectivityDiagnostics` 的 `CablePathNotFoundException`，记录起终真实分量、所有分量的边界自由端口，并为起点分量每个自由 Port 搜索全模型其他分量的最近 Top 5。失败诊断不受入图候选半径限制，也不改变图；半径外候选标记 `DiagnosticOnly`。

完整字段、红线定位、可见性验证和真实模型重放方法见 [CONNECTIVITY_DIAGNOSTICS.md](CONNECTIVITY_DIAGNOSTICS.md)。

## 兼容行为

直接调用 `new CableNetwork(...)` 默认关闭 VirtualConnector。现有调用显式开启半径、但未开启 `VirtualConnectorExperimentalTopN` 时，保留 V11 的双向唯一候选要求和 `Ambiguous` 拒绝行为；全部原有回归检查仍保留。V11 中的 `Accepted` 仅表示算法接受唯一虚拟候选，同样需要复核，不是 confirmed。

## 验证边界

当前测试包含不同名称、RunName、规格和任意 XYZ 倾斜的桥架，以及水平/带 Z 高差的 Port-to-Segment、多候选和失败断点。独立 Navisworks 合成宿主逐件核对中心线贡献、虚拟三维距离、严格 GapBridge 和真实小间隙，保留 10 微米几何阈值。

合成验证不替代真实工程模型的开口、可穿缆性和敷设长度验收。未识别的配件仍明确列出，不使用 BoundingBox 猜测长度；本阶段未增加 Tee、Reducer、Elbow、主路优先或其他工程规则。
