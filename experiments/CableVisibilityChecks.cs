using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Autodesk.Navisworks.Api;

namespace TrayRouteExperiment
{
    // Runs only when explicitly requested by a separate fixture host. Never called by the user UI.
    internal static class CableVisibilityChecks
    {
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=100000000};
        static void Hide(Document doc,IEnumerable<ModelItem> items,bool hidden)
        {
            using(var collection=new ModelItemCollection()){collection.AddRange(items);doc.Models.SetHidden(collection,hidden);}
        }
        static string Hash(string value)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");
        }
        static CableRoute Route(HostNetwork network,string[] args)
        {
            var a=CableHostProbe.Location(network,args[1],args[3]);var b=CableHostProbe.Location(network,args[2],args[4]);
            return network.Graph.Find(a,b);
        }
        static string Topology(HostNetwork network)
        {
            return Json.Serialize(new{nodes=network.Graph.GraphNodes,edges=network.Graph.GraphEdges,
                components=network.Graph.PhysicalPieceComponents,boundary=network.Graph.PhysicalBoundaryPorts,candidates=network.Graph.VirtualConnectorCandidates});
        }
        internal static object Run(Document doc,string[] args)
        {
            var all=doc.Models.RootItems.SelectMany(root=>root.DescendantsAndSelf).ToArray();var hidden=all.Where(i=>i.IsHidden).ToArray();
            try
            {
                Hide(doc,all,false);var visible=HostNetwork.Read(doc,null);var visibleRoute=Route(visible,args);
                string recognized=Json.Serialize(visible.Recognized),topology=Topology(visible),route=Json.Serialize(visibleRoute);
                var firstGeometry=visible.Geometry.First();var component=HostNetwork.Component(firstGeometry.First());
                var cases=new[]{
                    new{label="component hidden",items=new[]{component}},
                    new{label="geometry hidden",items=firstGeometry},
                    new{label="ancestor hidden",items=new[]{component.Parent??component}},
                    new{label="all model roots hidden",items=doc.Models.RootItems.ToArray()}
                };
                var checks=new List<object>();
                foreach(var test in cases)
                {
                    Hide(doc,all,false);Hide(doc,test.items,true);int hiddenGeometry=all.Count(i=>i.HasGeometry&&i.AncestorsAndSelf.Any(a=>a.IsHidden));
                    if(hiddenGeometry==0)throw new InvalidOperationException("Visibility fixture did not hide actual geometry: "+test.label);
                    var changed=HostNetwork.Read(doc,null);var changedRoute=Route(changed,args);
                    bool recognizedSame=recognized==Json.Serialize(changed.Recognized),topologySame=topology==Topology(changed),routeSame=route==Json.Serialize(changedRoute);
                    bool lengthSame=visibleRoute.Length==changedRoute.Length;
                    checks.Add(new{state=test.label,hiddenGeometry,recognized=changed.Recognized.Count,
                        recognizedIdentical=recognizedSame,topologyIdentical=topologySame,routeIdentical=routeSame,routeLengthIdentical=lengthSame,
                        topologyHash=Hash(Topology(changed)),routeLength=changedRoute.Length});
                    if(!recognizedSame||!topologySame||!routeSame||!lengthSame)throw new InvalidOperationException("Hidden state changed routing graph: "+test.label);
                }
                var volumes=all.Where(i=>i.DisplayName=="Maintenance Volume").ToArray();
                var volumeGeometry=new HashSet<string>(volumes.SelectMany(v=>v.DescendantsAndSelf).Where(i=>i.HasGeometry).Select(HostNetwork.Key));
                bool maintenanceExcluded=!visible.Geometry.SelectMany(g=>g).Any(g=>volumeGeometry.Contains(HostNetwork.Key(g)));
                if(!maintenanceExcluded)throw new InvalidOperationException("Maintenance Volume geometry entered routing graph");
                return new{passed=true,exact=true,baselineRecognized=visible.Recognized.Count,baselineTopologyHash=Hash(topology),baselineRouteLength=visibleRoute.Length,
                    maintenanceVolumes=volumes.Length,maintenanceGeometry=volumeGeometry.Count,maintenanceExcluded,states=checks};
            }
            finally{Hide(doc,all,false);Hide(doc,hidden,true);}
        }
    }
}
