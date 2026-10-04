# V13 路径成本

V13 只修正现有单向 Dijkstra 的成本排序。目标是电缆路径的实际总长度最小，避免较短的虚拟连接使明显更长的完整路线胜出。Port Graph、V12 候选生成和全部几何规则保持不变。

## 比较顺序

开启或关闭 VirtualConnector 时使用同一字典序；只有前面的指标完全相等，才比较后一项：

1. `TotalLength` 最小。
2. `VerticalTravel` 最小。
3. `VirtualConnectorCount` 最少。
4. `VirtualConnectorTotalLength` 最小。
5. `GapBridgeCount` 最少。

`TotalLength` 是路线实际经过的全部图边长度之和，包括 InternalEdge、物理连接间隙、严格 GapBridge 和 VirtualConnector。在 `CableRoute` 报告中，`TotalLength` 与原有 `Length` 表示同一数值，单位均为米。

例如 1 条虚拟连接贡献 0.30m、总长 12m 的路线，优于同为 1 条虚拟连接、虚拟长度 0.05m、总长 80m 的路线。虚拟数量和虚拟长度不再排在总长之前。`Find(start, finish, false)` 仍先排除全部 `RequiresReview` 边；成本排序不改变严格模式的可通行边集合。

## 累计垂直移动

每条图边及实际经过的路径片段使用世界坐标中心折线：

```text
VerticalTravel = sum(abs(P[i].Z - P[i-1].Z)), i = 1..n-1
```

不是只取起终点的 Z 差。水平两点之间先升高 3m 再下降 3m，累计垂直移动为 6m。InternalEdge、ConnectionEdge、GapBridgeEdge 和 VirtualConnectorEdge 都使用自身实际中心线；起终点或连接 station 拆边时，只累计经过的裁剪片段。

仅当总长度完全相等时，较小的累计垂直移动才优先。正常单调 slope 可用于起终点有高差的路线；在等长备选中，它优于中途上下反复绕行。

`CableGraphEdge.VerticalTravel`、`CableStep.VerticalTravel` 和 `CableRoute.VerticalTravel` 输出对应累计值；JSON 的 `pathCostOrder` 明确记录五项顺序。

## 正反向成本一致

普通 `double` 连续相加会受加数顺序影响。相同的一组边反向遍历时，末位浮点尾差可能先决定总长度比较，使本应等长的路线无法进入垂直移动或虚拟数量比较。

`CableDistance` 将每个有限非负 IEEE 754 `double` 权重转换成以 `2^-1074` 米为单位的 `BigInteger` 精确整数，在搜索时精确累加和比较；发布报告时再一次转换回 `double`。总长、虚拟总长和每段绝对 Z 变化都使用这一累加方式。它不改变提取的坐标或几何边长，不引入比较 epsilon，也不把实际略有差异的长度强行视为相等。

相同无向图上的正向、反向搜索要求总长度、累计垂直移动和虚拟连接数量一致。宿主探针另外核对虚拟连接总长与 Gap 数量；`reverseCostConsistent=false` 时判定验证失败。完全同成本的不同路径可能采用不同构件序列，不要求序列唯一。

## 验证入口

```powershell
dotnet run --project tests\CoreChecks.csproj
dotnet run --project tests\PortGraphChecks.csproj -- artifacts\v13-path-cost
.\build_cable_path_experiment.ps1 -NavisworksPath 'F:\Navisworks\Navisworks Manage 2023'
.\scripts\verify-cable-graph-host.ps1 -NavisworksPath 'F:\Navisworks\Navisworks Manage 2023' -Manifest .\artifacts\v13-path-cost\virtual-port-fixture.json -Model .\artifacts\v13-path-cost\virtual-port-fixture.ifc -Report .\artifacts\v13-path-cost\virtual-port-host.json
```

安装目录可替换为本机实际路径。端口图检查包含：12m / 80m 路线比较、等长水平 / 上下绕行、起终高差下的单调 slope / 反复绕行、完整 polyline 累计、station 部分路径计长，以及正反向成本一致。原有几何和连接生成回归继续保留。

真实模型可由包含 `model`、`recognized`、`start`、`finish` 的已保存查询报告重放：

```powershell
.\scripts\probe-model-connectivity.ps1 -NavisworksPath 'F:\Navisworks\Navisworks Manage 2023' -Query .\artifacts\v13-path-cost\real-model-shorter-route-query.json -Report .\artifacts\v13-path-cost\real-detour-v13-native.json
```

查询文件和模型数据位于忽略的 `artifacts/` 中，不随源码发布。该脚本打开独立只读 Navisworks 宿主，不保存模型。候选路线仍需要复核；`incomplete=true` 表示该图没有包含完整模型。测试结果和真实模型的改善需以本轮报告为准，V12 历史验证不能代替 V13 验证。

## V13 实测记录（2026-10-04）

本轮构建通过，共享几何检查 31/31、端口图检查 663/663 通过。新增路线案例与现有完整三维链严格核对正反向五项成本；原连接生成和几何断言继续保留，仅更新与新路径目标冲突的旧排序预期。

在 TS-M09F9.29(全）.nwd 的两个独立只读 Navisworks 宿主中，使用相同 ModelItem ID 和 XYZ 起终点重放：

| 指标 | V12 旧目标 | V13 总长优先 |
| --- | ---: | ---: |
| TotalLength | 89.591977800 m | 19.222378639 m |
| VerticalTravel | 12.385387666 m | 11.256059015 m |
| VirtualConnectorCount | 9 | 9 |
| VirtualConnectorTotalLength | 2.050199834 m | 2.423152524 m |
| GapBridgeCount | 1 | 1 |

V13 选择虚拟贡献略长、实际总长明显更短的路线。正反向五项成本严格相等；独立 Python 使用 Fraction 累加整条实际 polyline 的绝对 Z 变化，结果与宿主完全一致。独立最短路 oracle 的五项成本与宿主逐项差异均为 0。

独立比较 14 类数据：识别、拒绝、构件几何、图节点和图边、真实分量及边界端口、虚拟候选、歧义、配置、识别完整性和起终点；排除新增 VerticalTravel 输出属性后全部完全一致。此次路线改善来自寻路成本改变。

查询为 `artifacts/v13-path-cost/real-model-shorter-route-query.json`；旧/新宿主报告分别为 `real-detour-v12-native.json`、`real-detour-v13-native.json`，独立比较为 `native-real-comparison.json`。这些含真实模型信息的文件均被 Git 忽略。

本次仍为 2000 件构件的局部图、928 个真实分量，达到既有上限且 `incomplete=true`；不能据此认定全模型路线最优。新路线有 9 条虚拟连接和 1 条严格 Gap，仍需要复核真实开口与可穿缆性。

额外的 8 场景合成宿主回归未完成：首个 visibility 宿主在 180 秒内没有输出报告；使用绝对 IFC 路径再次启动也未输出报告。执行命令工具同期无响应，不能将这些启动尝试记为测试通过。只停止了本轮创建的两个测试宿主，保留原有 Navisworks 进程；本轮验证依据为上述构建、694 项自动检查、两个真实模型原生重放和独立图/成本对比。

## 保留范围

本阶段不修改 VirtualConnector 生成、PortToPort3D、PortToSegment3D、Top-N、2mm PhysicalTolerance、严格 GapBridge、FittingGeometry、Tee 或 Reducer；不新增桥架类型、连接类型、主路规则、K-shortest、Bidirectional Dijkstra 或 Bidirectional A*。V12 的连通诊断说明见 [CONNECTIVITY_DIAGNOSTICS.md](CONNECTIVITY_DIAGNOSTICS.md)。
