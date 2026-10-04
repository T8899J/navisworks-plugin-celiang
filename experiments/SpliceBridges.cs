using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    // A modelled joint without its own reconstructable centreline (e.g. an adjustable splice connector).
    // Min/Max is its world-metre bounding box; it only tells the graph which two tray ends it joins.
    public sealed class CableJointHint
    {
        public string Id,Name,Kind;
        public Vec Min,Max;
        // Nominal tray section from the connector's Size, in metres; zero when unknown.
        public double NominalWidth,NominalHeight;
    }

    public sealed partial class CableNetwork
    {
        public const double SpliceHintTolerance=.03,SpliceSizeTolerance=.01;

        // Ports of the joined trays match the nominal height; measured width includes flanges, so it is at least nominal.
        // Smaller trays merely passing through the box are ignored and stay free for virtual candidates.
        static bool MatchesNominalSection(Port port,CableJointHint hint)
        {
            if(hint.NominalWidth<=0||hint.NominalHeight<=0)return true;
            return Math.Abs(port.Height-hint.NominalHeight)<=SpliceSizeTolerance&&port.Width>=hint.NominalWidth-SpliceSizeTolerance;
        }
        public int SpliceBridgeCount;

        static bool InsideBox(Vec p,CableJointHint hint,double tolerance)
        {
            return p.X>=hint.Min.X-tolerance&&p.X<=hint.Max.X+tolerance&&p.Y>=hint.Min.Y-tolerance&&p.Y<=hint.Max.Y+tolerance&&p.Z>=hint.Min.Z-tolerance&&p.Z<=hint.Max.Z+tolerance;
        }

        // A splice connector bolts to the two tray ends facing each other inside its box. Dense boxes also
        // contain ports of smaller trays merely passing through; those do not face each other, so a single
        // mutually facing pair still identifies the joint. Anything less clear stays ambiguous.
        const double FacingRatio = .1;
        void BuildSpliceBridges(HashSet<string> occupied)
        {
            if(JointHints.Count==0)return;
            var free=new List<CableLocation>();
            for(int piece=0;piece<Pieces.Count;piece++)for(int port=0;port<Pieces[piece].Shape.Ports.Length;port++)
            {var location=WorldPort(piece,port);if(!occupied.Contains(Socket(location)))free.Add(location);}
            var claimed=new HashSet<string>(StringComparer.Ordinal);
            foreach(var hint in JointHints)
            {
                var inside=free.Where(p=>InsideBox(p.Point,hint,SpliceHintTolerance)&&MatchesNominalSection(Pieces[p.Piece].Shape.Ports[p.Port],hint)).ToList();
                if(inside.Count<2)continue;
                string label="SpliceConnector "+(hint.Name??hint.Id)+" ("+hint.Id+")";
                Func<CableLocation,CableLocation,bool> facing=(p,q)=>{
                    if(p.Piece==q.Piece)return false;
                    var delta=q.Point-p.Point;if(delta.Norm<=1e-9)return false;
                    return Pieces[p.Piece].Shape.Ports[p.Port].Outward.Dot(delta)/delta.Norm>FacingRatio&&
                        Pieces[q.Piece].Shape.Ports[q.Port].Outward.Dot(delta*(-1))/delta.Norm>FacingRatio;};
                CableLocation a=null,b=null;int pairs=0;
                for(int i=0;i<inside.Count;i++)for(int j=i+1;j<inside.Count;j++)
                    if(facing(inside[i],inside[j])){pairs++;a=inside[i];b=inside[j];}
                // Legacy rule: only a box with exactly two free ports is considered at all.
                if(!SpliceBridgeUniqueFacingPair)
                {
                    if(inside.Count>2){Ambiguities.Add(label+": 包围盒内有 "+inside.Count+" 个自由端口，不自动桥接");continue;}
                    pairs=1;a=inside[0];b=inside[1];
                }
                if(pairs!=1){Ambiguities.Add(label+": 包围盒内有 "+inside.Count+" 个自由端口、"+pairs+" 对相向端口，不自动桥接");continue;}
                if(claimed.Contains(Socket(a))||claimed.Contains(Socket(b))){Ambiguities.Add(label+": 端口已被另一个连接件桥接");continue;}
                claimed.Add(Socket(a));claimed.Add(Socket(b));
                var deltaAB=b.Point-a.Point;
                Joins.Add(new CableJoin{A=a,B=b,Kind="splice-bridge",SpliceConnectorId=hint.Id,SpliceConnectorName=hint.Name,ConnectionPoint=b.Point,SurfaceGap=deltaAB.Norm,RequiresReview=false,
                    ReviewReason=string.Format(CultureInfo.InvariantCulture,"经 {0} 连接: distance3D={1:F6} m",label,deltaAB.Norm)});
                SpliceBridgeCount++;
            }
            foreach(string socket in claimed)occupied.Add(socket);
        }
    }
}
