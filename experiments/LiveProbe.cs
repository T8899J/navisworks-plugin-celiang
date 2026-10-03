using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Plugins;
using JiePinPai.TrayMeasurement;

[Plugin("TrayLiveProbe2", "JPPM")]
public sealed class LiveProbe : AddInPlugin
{
    internal static string Path(ModelItem item)
    {
        var a = (Array)ComApiBridge.ToInwOaPath(item).ArrayData;
        return string.Join("/", a.Cast<object>().Select(Convert.ToInt32));
    }
    static object Describe(ModelItem item, bool properties)
    {
        var b = item.BoundingBox();
        return new { path=Path(item), name=item.DisplayName, geometry=item.HasGeometry, hidden=item.IsHidden,
            children=item.Children.Count(), min=b.IsEmpty?null:new[]{b.Min.X,b.Min.Y,b.Min.Z}, max=b.IsEmpty?null:new[]{b.Max.X,b.Max.Y,b.Max.Z},
            properties=properties?item.PropertyCategories.SelectMany(c=>c.Properties.Select(p=>new { category=c.DisplayName, name=p.DisplayName, value=p.Value.ToString() })).ToArray():null };
    }
    public override int Execute(params string[] args)
    {
        try
        {
            var d = Autodesk.Navisworks.Api.Application.ActiveDocument;
            var selected = d.CurrentSelection.SelectedItems.ToArray();
            var all = d.Models.RootItems.SelectMany(x=>x.DescendantsAndSelf);
            var scope = selected.Take(1).SelectMany(x=>x.AncestorsAndSelf)
                .FirstOrDefault(x=>x.DisplayName.EndsWith("-TRAY", StringComparison.OrdinalIgnoreCase));
            object result;
            if (args.Length>1 && args[1]=="geometry")
            {
                var wanted = new HashSet<string>(args.Skip(2));
                var output = new List<object>();
                foreach(var item in (scope==null?all:scope.DescendantsAndSelf).Where(x=>wanted.Contains(Path(x))))
                {
                    try { var s=GeometryReader.Read(d,item); output.Add(new { path=s.Path, name=s.ItemName, result=s.Result, triangles=s.Triangles, fragments=s.AcceptedFragments, otherInstances=s.OtherInstanceFragments, properties=Describe(item,true), parent=Describe(item.Parent,true) }); }
                    catch(Exception e) { output.Add(new { path=Path(item), name=item.DisplayName, error=e.Message }); }
                }
                result = new { file=d.FileName, units=d.Units.ToString(), items=output };
            }
            else
            {
                result = new { file=d.FileName, units=d.Units.ToString(), selection=selected.Select(x=>Describe(x,true)).ToArray(),
                    ancestors=selected.Take(1).SelectMany(x=>x.Ancestors).Select(x=>Describe(x,true)).ToArray(),
                    roots=d.Models.RootItems.Select(x=>Describe(x,false)).ToArray(),
                    scope=scope==null?null:Describe(scope,true),
                    nodes=scope==null?null:scope.DescendantsAndSelf.Take(6000).Select(x=>Describe(x,false)).ToArray() };
            }
            File.WriteAllText(args[0],new JavaScriptSerializer { MaxJsonLength=100000000 }.Serialize(result));
            return 0;
        }
        catch(Exception e) { File.WriteAllText(args[0],e.ToString()); return 1; }
    }
}
