# V12 Real Model Connectivity Diagnostics

本阶段保持现有 Port Graph、2mm PhysicalTolerance、严格 GapBridge 和配件重建规则，诊断真实模型断图；不增加桥架类型、主路优先、K-shortest 或工程规则。

## 建图和显示状态

`HostNetwork.Discover`、`VisibleGeometry` 不读取 `IsHidden` 决定入图。Maintenance Volume 及其子树仍排除；显示隔离只影响渲染，不改变几何识别和连通性。原有 6000 候选、60 秒扫描、120 秒提取、2000 个已识别构件上限仍保留；`incomplete=true` 的报告不能当成完整模型验收。

## 候选图

当前配置 `VirtualConnectorExperimentalTopN=true`、`VirtualConnectorTopN=5`、`VirtualConnectorMaxDistance=0.5` 米。真实连接建立后先计算 Physical Connected Components，只有跨分量的自由 Port 才生成虚拟候选；Port-to-Port 与 Port-to-Segment 一起按世界 XYZ 距离排名。保留 Top-N 的全部候选，不因候选数超过一而全部拒绝。

全部虚拟连接为 `Candidate`、`RequiresReview=true`、`Confirmed=false`。路径代价仍按虚拟连接数量、虚拟连接总长、路线实际总长排序。找到后显示“候选路径，需要复核”；长度包含虚拟连接的三维欧氏距离，不等于施工可通行性已确认。

## 找不到路径时

`CablePathNotFoundException.Diagnostics` 包含起终 Physical Component ID、所有分量的边界自由端口，以及起点分量每个自由 Port 对其他分量的最近 Top 5。诊断搜索不受 0.5m 入图半径限制，不修改或确认 Graph。真正入图的候选仍受独立配置半径限制。

每条候选包含：源/目标构件 ID、名称和 RunName，源 Port，目标 Port 或 InternalEdge + station；源/目标真实分量；世界 XYZ、带符号 DeltaX/Y/Z、Distance3D；源方向角、目标 Port 方向角/相向角或目标中心线切向角；两端几何宽高及尺寸差；CandidateRank、CandidateCount、是否在入图半径内。零距离时无定义的角度为 null。

失败后默认定位全部已记录候选中距离最近的一条，并画红色 3D 线和端点标记。展开“查看明细与断点诊断”的“断点诊断”页可查看其余候选，点击行更换红线。表格每页 100 条，横向滚动可看完整参数，JSON 保留全部记录。

诊断线使用 Navisworks RenderPlugin，按世界米转换到文档单位，关闭深度遮挡。定位只改变当前视点；“恢复原视图”或关闭面板恢复实验前视点/选择，模型文件不会被保存。

## 日志

用户入口输出 `artifacts/cable-graph-*.json`，新增 `physicalBoundaryPorts`、`connectivityDiagnostics`、`highlightedBreakpoint` 和拒绝原因汇总 `rejectionSummary`。未找到路线仍保留全图、拒绝清单和诊断候选。

原有 `RejectedComponent` 字段继续记录 ModelItem ID、DisplayName、RunName、Description、Size、分类和具体几何失败原因。未识别配件继续跳过，本阶段没有用弦长或 BoundingBox 猜测配件长度。

## 可复现验证

```powershell
dotnet run --project tests\CoreChecks.csproj
dotnet run --project tests\PortGraphChecks.csproj -- artifacts\v12-connectivity
.\build_cable_path_experiment.ps1
.\scripts\verify-cable-graph-host.ps1 -Manifest .\artifacts\v12-connectivity\visibility-fixture.json -Model .\artifacts\v12-connectivity\visibility-fixture.ifc -Report .\artifacts\v12-connectivity\visibility-host.json -VerifyVisibility
.\scripts\verify-cable-graph-host.ps1 -Manifest .\artifacts\v12-connectivity\multiple-candidate-fixture.json -Model .\artifacts\v12-connectivity\multiple-candidate-fixture.ifc -Report .\artifacts\v12-connectivity\multiple-candidate-host.json
.\scripts\verify-cable-graph-host.ps1 -Manifest .\artifacts\v12-connectivity\disconnected-fixture.json -Model .\artifacts\v12-connectivity\disconnected-fixture.ifc -Report .\artifacts\v12-connectivity\disconnected-host.json
```

`-NavisworksPath` 可指定非默认安装目录。测试开独立宿主，不改变正在使用的文档。坐标参数使用 `xyz:` 前缀，避免 Navisworks 将负 X 坐标当成命令行选项。

V12 自动验证：31 项共享几何检查、381 项端口图检查，包含全部原有回归；8 个独立 Navisworks 合成宿主场景通过。可见性场景对隐藏构件、实体、祖先和全部根节点重新提取几何、重建图，逐状态比较完整识别列表、图内容和路线，均完全一致；Maintenance Volume 保持排除。多候选、水平接入、带高差接入、任意 XYZ 和失败诊断均在宿主验证。

GUI 验证：独立断截 IFC 中默认红线为 0.838153m，表格第二行定位后为 2.015564m，红线随行切换。该案例全部候选均为 DiagnosticOnly，未确认连接。

## 真实模型重放（2026-10-04）

使用现有失败报告保存的 ModelItem ID 和三维起终点，在独立只读宿主重放：

```powershell
.\scripts\probe-model-connectivity.ps1 -Query .\artifacts\cable-graph-20261004-125555.json -Report .\artifacts\v12-connectivity\real-model-failure-replay.json
```

结果只保留在忽略的 `artifacts/` 中，不包含在源码发布。此次真实模型中识别从 V11 的 877 件增至 2000 件，Rejected 从 2559 降至 1436，“没有可见实体”拒绝消失。起终真实分量仍不同，候选图可找到 4.905626m、7 件构件的路线，其中 2 条虚拟连接贡献 0.329053m，全部需要复核。

本次达到原有 2000 件上限，753 件未加入，报告 `incomplete=true`；另外 683 件仍因分类或几何失败而拒绝。候选路径和断点统计基于本次已识别范围，不能宣称全模型已经贯通。真实开口、可穿缆性和长度仍需人工验收。本阶段在上述诊断交付后停止。
