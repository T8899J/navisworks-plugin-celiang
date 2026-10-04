# VirtualConnector 三维实验

继续使用现有 Port / Junction / Edge Graph，不改变几何提取或配件重建规则。启动入口仍为 `启动电缆路径实验.bat`，本阶段 DLL 和插件注册名为 V11。

## 配置与候选

`cable-path-settings.json` 的 `VirtualConnectorMaxDistance` 为独立候选半径，当前为 **0.5 米**。构建时复制到 DLL 同目录。改源配置后重新构建并关闭、重开实验窗口；设为 0 关闭新连接。直接创建 `CableNetwork` 的旧调用默认关闭 VirtualConnector。

PhysicalTolerance 仍为 2mm。严格 GapBridge 仍使用原来的 50mm、2mm WidthAxis / HeightAxis、尺寸兼容及相向条件；接受的 GapBridge 不再重复生成 VirtualConnector。

先完成所有真实连接，再对包含 InternalEdge / ConnectionEdge 的 **Port/Junction 图**求 connected components。GapBridge 和 VirtualConnector 不参与这次分量计算。物理占用、物理连接歧义以及已接受 GapBridge 的端口不进入新候选搜索。

- **PortToPort3D**：不同真实分量中的两个自由 Port，连接其世界坐标，长度为 `sqrt(dx² + dy² + dz²)`。
- **PortToSegment3D**：自由 Port 投影到另一个 Straight / Slope InternalEdge 的每个 XYZ 线段，选择该边的最近投影点。投影 station 必须严格位于边内部，距离端 station 大于 0.1 微米；端部投影由 Port-to-Port 处理。在 Q 建 VirtualJunction，并沿现有 station 机制逻辑拆分中心线。

角度、横向偏移、Z 高差、截面尺寸差异不作为这两类虚拟连接的硬条件。目标中部仅支持实际直线的 Straight / Slope 中心线。一个边的共享 polyline 顶点不会重复产生候选。

两个候选类型一起计数；每个源 Port 必须恰好只有一个跨分量候选，Port-to-Port 的目标 Port 也必须唯一。多候选记录 `Ambiguous`，不选最近者。不同自由 Port 可分别接入同一主路边的不同 station。

## 计长与诊断

新增 `CableEdgeKind.VirtualConnectorEdge`，所有此类边始终 `RequiresReview=true`。世界三维距离直接计入总长度和高亮线段；报告中的 `VirtualConnectorCount`、`VirtualConnectorTotalLength` 是本次路径实际经过的数量、长度。

开启 VirtualConnector 时，Dijkstra 严格按 `(VirtualConnectorCount, VirtualConnectorTotalLength, TotalPhysicalLength)` 排序。第三项包含所有实际经过的边长，不添加虚构惩罚。关闭新功能时保留原 GapBridge 数量优先的旧调用行为。`Find(start, finish, false)` 仍排除所有待复核边。

同一真实分量内不创建新虚拟边，记录 `SkippedSamePhysicalComponent`。如果两个真实连通端点之间还有经外部分量形成的虚拟绕路，零虚拟连接的真实路径优先。

实验读取网络后即在 `artifacts/cable-graph-*.json` 写诊断，找不到路径时也保留候选数据。`virtualConnectorCandidates` 包含搜索半径内的跨分量候选和同分量跳过记录：

- 源 / 目标构件索引、ID、名称、RunName、Port，或目标 InternalEdge / station；
- 世界坐标、3D distance、带符号 delta X / Y / Z，所有距离单位为米；
- 源朝向与连接方向夹角，目标 Port 的对应夹角、相向角，或目标中心线切向夹角，单位为度；零距离时无定义的夹角为 null；
- 两端测得的宽高及目标减源的截面差异；
- Physical component ID、源合法候选数、目标 Port 合法候选数（中部目标没有 Port，此项为 0）、Status、Reason。

`Accepted` 只表示唯一的虚拟候选，实际开口、支撑及电缆可穿行性须复核。名称与规格只用于诊断。未识别构件仍使用现有 rejected 详细日志。

## 验证

```powershell
dotnet run --project tests\CoreChecks.csproj
dotnet run --project tests\PortGraphChecks.csproj -- artifacts\virtual-connector-stage
.\build_cable_path_experiment.ps1
.\scripts\verify-cable-graph-host.ps1 -Manifest .\artifacts\virtual-connector-stage\virtual-port-fixture.json -Model .\artifacts\virtual-connector-stage\virtual-port-fixture.ifc -Report .\artifacts\virtual-connector-stage\virtual-port-host.json
.\scripts\verify-cable-graph-host.ps1 -Manifest .\artifacts\virtual-connector-stage\virtual-segment-fixture.json -Model .\artifacts\virtual-connector-stage\virtual-segment-fixture.ifc -Report .\artifacts\virtual-connector-stage\virtual-segment-host.json
```

两个新 IFC 均包含不同名称、RunName、规格的空间倾斜桥架。独立理论长度分别为 `2 + sqrt(0.1625)` 米和 `2 + 0.35 + 4.3 = 6.65` 米。宿主检查保留原来的 10 微米几何阈值，并分别核对 InternalEdge 和虚拟连接的贡献。这些是合成宿主测试，不能替代真实工程模型的长度和可穿缆验收。
