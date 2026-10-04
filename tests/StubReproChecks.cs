using System;using System.Collections.Generic;using System.Linq;using JiePinPai.TrayMeasurement.Core;using TrayRouteExperiment;

static partial class PortGraphChecks
{

    static List<Triangle> RealStub0()
    {
        return new List<Triangle>
        {
            new Triangle(new Vec(255.16500311969224,26.563434035851177,54.495726182151365),new Vec(255.16500311969224,26.563434035851177,54.72410569994502),new Vec(255.16500311969224,26.413434029890713,54.495726182151365)),
            new Triangle(new Vec(255.16500311969224,26.413434029890713,54.495726182151365),new Vec(255.16500311969224,26.563434035851177,54.72410569994502),new Vec(255.16500311969224,26.413434029890713,54.72410569994502)),
            new Triangle(new Vec(255.16500311969224,26.563434035851177,54.72410569994502),new Vec(255.16500311969224,26.563434035851177,54.495726182151365),new Vec(254.97500312207643,26.563434035851177,54.72410569994502)),
            new Triangle(new Vec(254.97500312207643,26.563434035851177,54.72410569994502),new Vec(255.16500311969224,26.563434035851177,54.495726182151365),new Vec(254.97500312207643,26.563434035851177,54.495726182151365)),
            new Triangle(new Vec(254.97500312207643,26.563434035851177,54.72410569994502),new Vec(254.97500312207643,26.563434035851177,54.495726182151365),new Vec(254.97500312207643,26.413434029890713,54.72410569994502)),
            new Triangle(new Vec(254.97500312207643,26.413434029890713,54.72410569994502),new Vec(254.97500312207643,26.563434035851177,54.495726182151365),new Vec(254.97500312207643,26.413434029890713,54.495726182151365))
        };
    }

    static void RealStubCase0()
    {
        var mesh = RealStub0(); var name = "ET-M09F-L-E-TRAY-30002-TRAY-0232";
        var pca = StraightMeasurement.Measure(mesh, true);
        Console.WriteLine("     pca: candidate=" + pca.IsStraightCandidate + " span=" + pca.SpanMetres.ToString("F4") + " w=" + pca.WidthMetres.ToString("F4") + " h=" + pca.HeightMetres.ToString("F4") + " ratio=" + pca.EigenRatio.ToString("F3") + " reason=" + pca.Reason);
        try { var part = FittingGeometry.Build(mesh, "Straight", .15, .15); Near(part.InternalEdges.Single().Length, (part.Ports[1].Point-part.Ports[0].Point).Norm, name + " rebuilt with nominal section"); Console.WriteLine("     built length=" + part.InternalEdges.Single().Length.ToString("F4") + " axis=" + (part.Ports[1].Point - part.Ports[0].Point)); }
        catch (Exception e) { Console.WriteLine("     FAILED " + name + ": " + e.Message); }
    }

    static List<Triangle> RealStub1()
    {
        return new List<Triangle>
        {
            new Triangle(new Vec(255.51333348562355,29.879251258296243,54.88799999701977),new Vec(255.2606251101696,29.879251258296243,54.88799999701977),new Vec(255.51333348562355,29.879251258296243,55.03800000298023)),
            new Triangle(new Vec(255.51333348562355,29.879251258296243,55.03800000298023),new Vec(255.2606251101696,29.879251258296243,54.88799999701977),new Vec(255.2606251101696,29.879251258296243,55.03800000298023)),
            new Triangle(new Vec(255.2606251101696,29.879251258296243,54.88799999701977),new Vec(255.51333348562355,29.879251258296243,54.88799999701977),new Vec(255.2606251101696,29.68925126068043,54.88799999701977)),
            new Triangle(new Vec(255.2606251101696,29.68925126068043,54.88799999701977),new Vec(255.51333348562355,29.879251258296243,54.88799999701977),new Vec(255.51333348562355,29.68925126068043,54.88799999701977)),
            new Triangle(new Vec(255.2606251101696,29.68925126068043,54.88799999701977),new Vec(255.51333348562355,29.68925126068043,54.88799999701977),new Vec(255.2606251101696,29.68925126068043,55.03800000298023)),
            new Triangle(new Vec(255.2606251101696,29.68925126068043,55.03800000298023),new Vec(255.51333348562355,29.68925126068043,54.88799999701977),new Vec(255.51333348562355,29.68925126068043,55.03800000298023))
        };
    }

    static void RealStubCase1()
    {
        var mesh = RealStub1(); var name = "ET-M09F-L-E-TRAY-30004-TRAY-0018";
        var pca = StraightMeasurement.Measure(mesh, true);
        Console.WriteLine("     pca: candidate=" + pca.IsStraightCandidate + " span=" + pca.SpanMetres.ToString("F4") + " w=" + pca.WidthMetres.ToString("F4") + " h=" + pca.HeightMetres.ToString("F4") + " ratio=" + pca.EigenRatio.ToString("F3") + " reason=" + pca.Reason);
        try { var part = FittingGeometry.Build(mesh, "Straight", .15, .15); Near(part.InternalEdges.Single().Length, (part.Ports[1].Point-part.Ports[0].Point).Norm, name + " rebuilt with nominal section"); Console.WriteLine("     built length=" + part.InternalEdges.Single().Length.ToString("F4") + " axis=" + (part.Ports[1].Point - part.Ports[0].Point)); }
        catch (Exception e) { Console.WriteLine("     FAILED " + name + ": " + e.Message); }
    }
}
