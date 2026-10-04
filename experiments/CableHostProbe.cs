using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Navisworks.Api.Plugins;
using JiePinPai.TrayMeasurement.Core;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace TrayRouteExperiment
{
    [Plugin("CableGraphProbeV10", "JPPM")]
    public sealed class CableHostProbe : AddInPlugin
    {
        static Vec Point(string value)
        {
            var p = value.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (p.Length != 3) throw new ArgumentException("Expected x,y,z in metres");
            return new Vec(p[0], p[1], p[2]);
        }

        public override int Execute(params string[] args)
        {
            if (args.Length == 0) return 1;
            HostNetwork network = null;
            try
            {
                var document = NavApp.ActiveDocument;
                network = HostNetwork.Read(document, null);
                CableRoute route = null; CableLocation start = null, finish = null;
                if (args.Length >= 5)
                {
                    // Exact names locate test endpoints only; graph connectivity never compares these names.
                    int a = network.Graph.Pieces.FindIndex(p => p.Shape.Name == args[1]);
                    int b = network.Graph.Pieces.FindIndex(p => p.Shape.Name == args[2]);
                    if (a < 0 || b < 0) throw new InvalidOperationException("Test endpoint component was not reconstructed; see rejected diagnostics");
                    start = network.Graph.Project(a, Point(args[3])); finish = network.Graph.Project(b, Point(args[4]));
                    route = network.Graph.Find(start, finish);
                }
                Write(args[0], new { success = true, model = document.FileName, start, finish, result = route,
                    recognized = network.Recognized, rejected = network.Rejected, incomplete = network.Incomplete,
                    scan = new { network.ScanNodeCount, network.ScanCandidateCount, network.ScanElapsedSeconds },
                    settings = new { network.Graph.PhysicalTolerance, network.Graph.GapBridgeMaxDistance,
                        network.Graph.GapBridgeWidthAxisTolerance, network.Graph.GapBridgeHeightAxisTolerance, network.Graph.GapBridgeSizeTolerance },
                    parts = network.Graph.Pieces.Select(p => p.Shape), graphNodes = network.Graph.GraphNodes,
                    graphEdges = network.Graph.GraphEdges, ambiguities = network.Graph.Ambiguities });
                return 0;
            }
            catch (Exception error)
            {
                Write(args[0], new { success = false, error = error.ToString(), rejected = network == null ? null : network.Rejected,
                    recognized = network == null ? null : network.Recognized });
                return 1;
            }
        }

        static void Write(string path, object report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, new JavaScriptSerializer { MaxJsonLength = 100000000 }.Serialize(report));
        }
    }
}
