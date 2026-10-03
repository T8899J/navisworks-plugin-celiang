using System;
using System.Collections.Generic;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;

class CoreChecks
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); checks++; }
    static void Near(double actual, double expected, string name) { Check(Math.Abs(actual - expected) < 1e-8, name + " = " + actual.ToString("G12")); }
    static List<Triangle> Box(double l, double w, double h, Vec offset = default(Vec))
    {
        var p = new[] { new Vec(0,0,0),new Vec(l,0,0),new Vec(l,w,0),new Vec(0,w,0),new Vec(0,0,h),new Vec(l,0,h),new Vec(l,w,h),new Vec(0,w,h) }.Select(v=>v+offset).ToArray();
        int[] faces = {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7};
        var result = new List<Triangle>(); for(int i=0;i<faces.Length;i+=3) result.Add(new Triangle(p[faces[i]],p[faces[i+1]],p[faces[i+2]])); return result;
    }
    static List<Triangle> Transform(List<Triangle> t, Func<Vec,Vec> f) { return t.Select(x=>new Triangle(f(x.A),f(x.B),f(x.C))).ToList(); }
    static Vec Rotate(Vec p) { double a=.713,b=.381,c=.297; var q=new Vec(p.X*Math.Cos(a)-p.Y*Math.Sin(a),p.X*Math.Sin(a)+p.Y*Math.Cos(a),p.Z); var r=new Vec(q.X*Math.Cos(b)+q.Z*Math.Sin(b),q.Y,-q.X*Math.Sin(b)+q.Z*Math.Cos(b)); return new Vec(r.X,r.Y*Math.Cos(c)-r.Z*Math.Sin(c),r.Y*Math.Sin(c)+r.Z*Math.Cos(c)); }
    static void Main()
    {
        var box=Box(1.237,.6,.15); var r=StraightMeasurement.Measure(box);
        Near(r.SpanMetres,1.237,"known 1237 mm length"); Check(r.IsStraightCandidate,"wide straight is candidate");
        r=StraightMeasurement.Measure(Transform(box,p=>Rotate(p)+new Vec(12345,-98654,3514))); Near(r.SpanMetres,1.237,"3D rotation and large world offset"); Check(r.IsStraightCandidate,"rotated straight candidate");
        r=StraightMeasurement.Measure(Transform(box,p=>new Vec(p.Z,p.Y,p.X))); Near(r.SpanMetres,1.237,"vertical straight");
        var subdivided=new List<Triangle>(box.Skip(1)); var t=box[0]; Vec mid=(t.A+t.B+t.C)*(1.0/3);
        subdivided.Add(new Triangle(t.A,t.B,mid));subdivided.Add(new Triangle(t.B,t.C,mid));subdivided.Add(new Triangle(t.C,t.A,mid));
        r=StraightMeasurement.Measure(subdivided);Near(r.SpanMetres,1.237,"unequal triangulation has no bias");
        var channel=Box(1.237,.6,.005);channel.AddRange(Box(1.237,.005,.15));channel.AddRange(Box(1.237,.005,.15,new Vec(0,.595,0)));
        r=StraightMeasurement.Measure(Transform(channel,Rotate));Near(r.SpanMetres,1.237,"U channel rotated");Check(r.IsStraightCandidate,"U channel candidate");
        var tee=Box(3,.2,.1);tee.AddRange(Box(.2,1,.1,new Vec(1.4,0,0)));r=StraightMeasurement.Measure(tee);Check(!r.IsStraightCandidate,"tee rejected by cross-section variation");
        var elbow=Box(2,.2,.1);elbow.AddRange(Box(.2,1.5,.1,new Vec(1.8,0,0)));r=StraightMeasurement.Measure(elbow);Check(!r.IsStraightCandidate,"elbow rejected");
        var gap=Box(1,.2,.1);gap.AddRange(Box(1,.2,.1,new Vec(2,0,0)));r=StraightMeasurement.Measure(gap);Check(!r.IsStraightCandidate,"separated pieces rejected");
        r=StraightMeasurement.Measure(Transform(Box(3,.3,.1),p=>new Vec(p.X,p.Y*(1+p.X*.2),p.Z)));Check(!r.IsStraightCandidate,"reducer rejected");
        Check(!StraightMeasurement.Measure(Box(.4,.6,.15)).IsStraightCandidate,"short wide piece rejected");
        Check(!StraightMeasurement.Measure(Box(1,1,1)).IsStraightCandidate,"cube ambiguous");
        bool thrown=false;try { StraightMeasurement.Measure(new List<Triangle>()); } catch(InvalidOperationException){thrown=true;} Check(thrown,"empty mesh fails");
        thrown=false;try { StraightMeasurement.Measure(new[]{new Triangle(new Vec(),new Vec(),new Vec())}); } catch(InvalidOperationException){thrown=true;} Check(thrown,"degenerate mesh fails");
        var matrix=new double[]{0,2,0,0,-3,0,0,0,0,0,4,0,100,200,300,1}; var point=GeometryTransform.Apply(new Vec(1,2,3),matrix);
        Near(point.X,94,"column-major X");Near(point.Y,202,"column-major Y");Near(point.Z,312,"column-major Z");
        var transformed=Transform(box,p=>GeometryTransform.Apply(p,matrix));r=StraightMeasurement.Measure(transformed);Near(r.SpanMetres,2*1.237,"nonuniform scale then rotation");
        Array oneBased=Array.CreateInstance(typeof(double),new[]{16},new[]{1});for(int i=0;i<16;i++)oneBased.SetValue(matrix[i],i+1);
        Check(GeometryTransform.ReadArray(oneBased,16).SequenceEqual(matrix),"one-based SAFEARRAY");
        Check(GeometryTransform.ReadArray(matrix,16).SequenceEqual(matrix),"zero-based SAFEARRAY");
        Check(GeometryTransform.IsWithinPath(new[]{1,2},new[]{1,2,3}),"descendant fragment accepted");
        Check(!GeometryTransform.IsWithinPath(new[]{1,2},new[]{1,20,3}),"same geometry other instance excluded");
        Check(!GeometryTransform.IsWithinPath(new int[0],new[]{1,2}),"empty path rejected");
        Near(LengthText.ParseMetres("1000 mm").Value,1,"attribute explicit mm");Near(LengthText.ParseMetres("1.237 m").Value,1.237,"attribute explicit m");Near(LengthText.ParseMetres("1 ft").Value,.3048,"attribute explicit ft");
        Check(!LengthText.ParseMetres("1000").HasValue,"unitless attribute not guessed");Check(!LengthText.ParseMetres("1,000 mm").HasValue,"ambiguous attribute separators rejected");Check(!LengthText.ParseMetres("-4 mm").HasValue,"negative attribute rejected");
        Console.WriteLine("RESULT: "+checks+" checks passed.");
    }
}
