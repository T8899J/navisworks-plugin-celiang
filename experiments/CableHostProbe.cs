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
    [Plugin("CableGraphProbeV12", "JPPM")]
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
            HostNetwork network=null;CableRoute route=null;CableLocation start=null,finish=null;CableConnectivityDiagnostics connectivity=null;
            object visibility=null;string error=null;var document=NavApp.ActiveDocument;
            try
            {
                network=HostNetwork.Read(document,null);
                if(args.Length>=5)
                {
                    start=Location(network,args[1],args[3]);finish=Location(network,args[2],args[4]);
                    try{route=network.Graph.Find(start,finish);}
                    catch(CablePathNotFoundException failure){error=failure.Message;connectivity=failure.Diagnostics;}
                    if(args.Contains("diagnose-physical")||args.Contains("--diagnose-physical"))connectivity=network.Graph.DiagnoseConnectivity(start,finish);
                    if(args.Contains("check-visibility")||args.Contains("--check-visibility"))visibility=CableVisibilityChecks.Run(document,args);
                }
            }
            catch(Exception failure){error=failure.ToString();}
            var report=new Dictionary<string,object>{{"success",error==null},{"model",document.FileName},{"start",start},{"finish",finish},
                {"result",route},{"error",error},{"connectivityDiagnostics",connectivity},{"visibilityChecks",visibility},{"probeArguments",args}};
            if(network!=null)
            {
                var graph=network.Graph;
                report.Add("recognized",network.Recognized);report.Add("rejected",network.Rejected);report.Add("incomplete",network.Incomplete);
                report.Add("rejectionSummary",network.Rejected.GroupBy(r=>r.GeometryFailureReason).Select(g=>new{reason=g.Key,count=g.Count()}));
                report.Add("scan",new{network.ScanNodeCount,network.ScanCandidateCount,network.ScanElapsedSeconds});
                report.Add("settings",new{graph.PhysicalTolerance,graph.GapBridgeMaxDistance,graph.GapBridgeWidthAxisTolerance,graph.GapBridgeHeightAxisTolerance,
                    graph.GapBridgeSizeTolerance,graph.VirtualConnectorMaxDistance,graph.VirtualConnectorExperimentalTopN,graph.VirtualConnectorTopN});
                report.Add("parts",graph.Pieces.Select(p=>p.Shape));report.Add("graphNodes",graph.GraphNodes);report.Add("graphEdges",graph.GraphEdges);
                report.Add("physicalComponents",graph.PhysicalPieceComponents);report.Add("physicalComponentCount",graph.PhysicalComponentCount);
                report.Add("physicalBoundaryPorts",graph.PhysicalBoundaryPorts);report.Add("virtualConnectorCandidates",graph.VirtualConnectorCandidates);report.Add("ambiguities",graph.Ambiguities);
            }
            Write(args[0],report);return error==null?0:1;
        }
        static void Write(string path,object report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path,new JavaScriptSerializer{MaxJsonLength=100000000}.Serialize(report));
        }
    }
}