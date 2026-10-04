using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Navisworks.Api.Plugins;
using JiePinPai.TrayMeasurement.Core;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace TrayRouteExperiment
{
    [Plugin("CableGraphProbeV13", "JPPM")]
    public sealed class CableHostProbe : AddInPlugin
    {
        static Vec Point(string value)
        {
            if(value.StartsWith("xyz:",StringComparison.Ordinal))value=value.Substring(4);
            var p=value.Split(',').Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
            if(p.Length!=3)throw new ArgumentException("Expected x,y,z in metres");return new Vec(p[0],p[1],p[2]);
        }
        internal static CableLocation Location(HostNetwork network,string idOrName,string xyz)
        {
            // Names or instance IDs locate probe endpoints only; topology never compares them.
            int piece=network.Graph.Pieces.FindIndex(p=>p.Shape.Id==idOrName||p.Shape.Name==idOrName);
            if(piece<0)throw new InvalidOperationException("Probe endpoint was not reconstructed: "+idOrName+"; inspect rejected diagnostics");
            return network.Graph.Project(piece,Point(xyz));
        }
        public override int Execute(params string[] args)
        {
            if(args.Length==0)return 1;
            var document=NavApp.ActiveDocument;
            // Navisworks swallows leading-dash tokens as its own switches, so the flag has no dashes.
            if(args.Contains("dump-mesh")||args.Contains("--dump-mesh"))
            {
                // Raw mesh diagnostic: no graph is built, so a rejected reconstruction can be inspected quickly.
                Write(args[0],new Dictionary<string,object>{{"success",true},{"model",document.FileName},{"pluginAssembly",PluginAssemblyInfo()},{"probeArguments",args},
                    {"meshDump",HostNetwork.DumpMesh(document,args.Where(a=>a.StartsWith("1/")),args.Where(a=>a.StartsWith("name:")).Select(a=>a.Substring(5)))}});
                return 0;
            }
            HostNetwork network=null;CableRoute route=null,reverseResult=null;bool? reverseCostConsistent=null;CableLocation start=null,finish=null;CableConnectivityDiagnostics connectivity=null;
            object visibility=null;string error=null;List<VirtualConnectorSuspect> suspects=null;
            try
            {
                if(args.Length>=5&&(args.Contains("spatial")||args.Contains("--spatial")))
                {
                    CableRoute ignored;CablePathNotFoundException notFound;
                    network=HostNetwork.ReadAround(document,Point(args[3]),Point(args[4]),n=>n.Graph.Find(Location(n,args[1],args[3]),Location(n,args[2],args[4])),out ignored,out notFound);
                }
                else network=HostNetwork.Read(document,null);
                if(args.Length>=5)
                {
                    start=Location(network,args[1],args[3]);finish=Location(network,args[2],args[4]);
                    try{route=network.Graph.Find(start,finish);}
                    catch(CablePathNotFoundException failure){error=failure.Message;connectivity=failure.Diagnostics;}
                    if(route!=null)
                    {
                        reverseResult=network.Graph.Find(finish,start);
                        reverseCostConsistent=route.Length==reverseResult.Length&&route.VerticalTravel==reverseResult.VerticalTravel&&
                            route.VirtualConnectorCount==reverseResult.VirtualConnectorCount&&route.VirtualConnectorTotalLength==reverseResult.VirtualConnectorTotalLength&&route.GapBridgeCount==reverseResult.GapBridgeCount;
                        if(reverseCostConsistent!=true)throw new InvalidOperationException("Forward/reverse route costs differ; inspect both cost tuples.");
                        suspects=network.AnnotateRoute(route);
                    }
                    if(args.Contains("diagnose-physical")||args.Contains("--diagnose-physical"))connectivity=network.Graph.DiagnoseConnectivity(start,finish);
                    if(args.Contains("check-visibility")||args.Contains("--check-visibility"))visibility=CableVisibilityChecks.Run(document,args);
                }
            }
            catch(Exception failure){error=failure.ToString();}
            // Report building is outside the probe try above; a fault here must still leave a readable report.
            try
            {
            var report=new Dictionary<string,object>{{"success",error==null},{"model",document.FileName},{"start",start},{"finish",finish},
                {"result",route},{"reverseResult",reverseResult},{"reverseCostConsistent",reverseCostConsistent},{"pathCostOrder",new[]{"TotalLength","VerticalTravel","VirtualConnectorCount","VirtualConnectorTotalLength","GapBridgeCount"}},{"error",error},{"connectivityDiagnostics",connectivity},{"virtualConnectorSuspects",suspects},{"visibilityChecks",visibility},{"probeArguments",args}};
            if(network!=null)
            {
                var graph=network.Graph;
                report.Add("recognized",network.Recognized);report.Add("rejected",network.Rejected);report.Add("incomplete",network.Incomplete);
                report.Add("spatialRegion",network.Region==null?null:new{network.Region.Min,network.Region.Max,network.Region.Margin,network.RegionMarginsTried,network.RegionCandidateCount,network.OutsideRegionCount});
                report.Add("rejectionSummary",network.Rejected.GroupBy(r=>r.GeometryFailureReason).Select(g=>new{reason=g.Key,count=g.Count()}));
                report.Add("scan",new{network.ScanNodeCount,network.ScanCandidateCount,network.ScanElapsedSeconds});
                report.Add("settings",new{graph.PhysicalTolerance,graph.GapBridgeMaxDistance,graph.GapBridgeWidthAxisTolerance,graph.GapBridgeHeightAxisTolerance,
                    graph.GapBridgeSizeTolerance,graph.VirtualConnectorMaxDistance,graph.VirtualConnectorExperimentalTopN,graph.VirtualConnectorTopN,graph.VirtualConnectorMaxAngle,graph.VirtualConnectorRejectParallelOffset});
                report.Add("spliceBridges",new{jointHints=network.JointHints.Count,bridged=graph.SpliceBridgeCount,ambiguous=graph.Ambiguities.Count(a=>a.StartsWith("SpliceConnector"))});
                report.Add("virtualFilterRejections",new{graph.VirtualConnectorDirectionRejected,graph.VirtualConnectorParallelRejected});
                report.Add("rejectedNearRoute",network.Rejected.Where(r=>r.DistanceToRoute.HasValue&&r.DistanceToRoute.Value<=1).OrderBy(r=>r.DistanceToRoute.Value));
                report.Add("parts",graph.Pieces.Select(p=>p.Shape));report.Add("graphNodes",graph.GraphNodes);report.Add("graphEdges",graph.GraphEdges);
                report.Add("physicalComponents",graph.PhysicalPieceComponents);report.Add("physicalComponentCount",graph.PhysicalComponentCount);
                report.Add("physicalBoundaryPorts",graph.PhysicalBoundaryPorts);report.Add("virtualConnectorCandidates",graph.VirtualConnectorCandidates);report.Add("ambiguities",graph.Ambiguities);
            }
            Write(args[0],report);return error==null?0:1;
            }
            catch(Exception reportFailure){Write(args[0],new Dictionary<string,object>{{"success",false},{"fatal",reportFailure.ToString()}});return 1;}
        }
        // Which file Navisworks actually executed; a stale copy would silently invalidate a diagnostic.
        internal static string PluginAssemblyInfo()
        {
            var file=new System.IO.FileInfo(typeof(CableHostProbe).Assembly.Location);
            return file.FullName+" | written "+(file.Exists?file.LastWriteTimeUtc.ToString("o"):"missing")+" | loaded "+System.IO.File.GetLastWriteTimeUtc(typeof(CableHostProbe).Assembly.Location).ToString("o");
        }
        static void Write(string path,object report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path,new JavaScriptSerializer{MaxJsonLength=100000000}.Serialize(report));
        }
    }
}