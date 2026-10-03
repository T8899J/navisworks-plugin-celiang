using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using JiePinPai.TrayMeasurement;

[Plugin("TrayHostProbe", "JPPM")]
public class HostProbe : AddInPlugin
{
    public override int Execute(params string[] parameters)
    {
        var text = new StringBuilder();
        int failures = 0;
        try
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            text.AppendLine("Document units: " + doc.Units);
            var items = doc.Models.RootItems.SelectMany(root => root.DescendantsAndSelf).ToList();
            for (int i = 0; i < 3; i++)
            {
                string name = "MVP_Straight_" + i;
                var item = items.FirstOrDefault(x => x.DisplayName == name);
                if (item == null) { failures++; text.AppendLine("FAIL missing: " + name + " names=" + string.Join(",", items.Select(x => x.DisplayName))); continue; }
                var result = GeometryReader.Read(doc, item);
                double expected = i == 2 ? 2.474 : 1.237;
                bool lengthPass = result.Result.IsStraightCandidate && Math.Abs(result.Result.SpanMetres - expected) < .0005;
                bool attributePass = result.Lengths.Any(x => x.Metres.HasValue && Math.Abs(x.Metres.Value - 1) < .000001);
                if (!lengthPass || !attributePass) failures++;
                text.AppendLine((lengthPass && attributePass ? "PASS " : "FAIL ") + name + " length_m=" + result.Result.SpanMetres.ToString("R") + " expected=" + expected + " triangles=" + result.Triangles.Count + " fragments=" + result.AcceptedFragments + " other_instances=" + result.OtherInstanceFragments + " duplicate=" + result.DuplicateFragments + " status=" + result.Result.Reason);
                foreach (var p in result.Lengths) text.AppendLine("  property: " + p.Source + " metres=" + p.Metres);
                var geometryNodes = item.DescendantsAndSelf.Where(x => x.HasGeometry).ToList();
                if (geometryNodes.Count == 1 && !geometryNodes[0].Equals(item))
                {
                    var leaf = GeometryReader.Read(doc, geometryNodes[0]);
                    bool pass = leaf.Result.IsStraightCandidate && Math.Abs(leaf.Result.SpanMetres - expected) < .0005 && leaf.Lengths.Any(x => x.Metres.HasValue && Math.Abs(x.Metres.Value - 1) < .000001);
                    if (!pass) failures++;
                    text.AppendLine((pass ? "PASS " : "FAIL ") + name + " geometry child: length_m=" + leaf.Result.SpanMetres.ToString("R") + " other_instances=" + leaf.OtherInstanceFragments);
                }
            }
        }
        catch (Exception ex) { failures++; text.AppendLine(ex.ToString()); }
        text.AppendLine("RESULT: " + (failures == 0 ? "PASS" : "FAIL") + " failures=" + failures);
        File.WriteAllText(parameters[0], text.ToString(), new UTF8Encoding(true));
        if (failures == 0 && parameters.Length > 1 && parameters[1] == "ui")
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            var selected = doc.Models.RootItems.SelectMany(x => x.DescendantsAndSelf).First(x => x.DisplayName == "MVP_Straight_1");
            using (var selection = new ModelItemCollection()) { selection.Add(selected); doc.CurrentSelection.CopyFrom(selection); }
            Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("JiePinPai_TrayMeasurement.JPPM");
        }
        return failures;
    }
}
