using System;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    internal sealed partial class CableForm
    {
        readonly DataGridView connectivityGrid=Grid();
        readonly Button connectivityPrevious=new Button{Text="上一页",Width=76,Height=28,FlatStyle=FlatStyle.Flat};
        readonly Button connectivityNext=new Button{Text="下一页",Width=76,Height=28,FlatStyle=FlatStyle.Flat};
        readonly Label connectivityPageLabel=new Label{AutoSize=true,Margin=new Padding(8,7,8,0)};
        CableConnectivityDiagnostics connectivityDiagnostics;
        VirtualConnectorCandidate highlightedBreakpoint;
        string connectivityError;
        int connectivityPage;
        const int ConnectivityPageSize=100;

        void AddConnectivityTab()
        {
            var tab=new TabPage("断点诊断");tab.Controls.Add(connectivityGrid);
            var pager=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=38,WrapContents=false};
            pager.Controls.Add(connectivityPrevious);pager.Controls.Add(connectivityNext);pager.Controls.Add(connectivityPageLabel);tab.Controls.Add(pager);
            connectivityPrevious.Click+=(s,e)=>{connectivityPage--;ShowConnectivityPage();};
            connectivityNext.Click+=(s,e)=>{connectivityPage++;ShowConnectivityPage();};
            connectivityGrid.CellClick+=(s,e)=>{
                if(e.RowIndex<0||connectivityDiagnostics==null)return;int index=connectivityPage*ConnectivityPageSize+e.RowIndex;
                if(index<connectivityDiagnostics.Candidates.Count)Safe(()=>{
                    HighlightBreakpoint(connectivityDiagnostics.Candidates[index]);SaveReport(null,connectivityError);
                });
            };
            detailsTabs.TabPages.Add(tab);
        }

        static string Xyz(Vec p) { return string.Format(CultureInfo.InvariantCulture,"{0:F6}, {1:F6}, {2:F6}",p.X,p.Y,p.Z); }
        static string Angle(double? angle) { return angle.HasValue?angle.Value.ToString("F3",CultureInfo.InvariantCulture):"未定义"; }
        void ShowConnectivityPage()
        {
            var candidates=connectivityDiagnostics==null?new VirtualConnectorCandidate[0]:connectivityDiagnostics.Candidates.ToArray();
            int pages=Math.Max(1,(candidates.Length+ConnectivityPageSize-1)/ConnectivityPageSize);connectivityPage=Math.Max(0,Math.Min(connectivityPage,pages-1));
            Rows(connectivityGrid,new[]{"源构件","源 Port","目标构件","目标 Port / Edge + Station","源分量","目标分量","源 XYZ (m)","目标 XYZ (m)",
                "DeltaX (m)","DeltaY (m)","DeltaZ (m)","3D 距离 (m)","源方向角 (°)","目标方向 / 切向角 (°)","源宽 / 高 (m)","目标宽 / 高 (m)","Candidate rank","候选数量","虚拟连接范围内"},
                candidates.Skip(connectivityPage*ConnectivityPageSize).Take(ConnectivityPageSize).Select(d=>new object[]{
                    d.SourceName,d.SourcePortId,d.TargetName,d.TargetPortId??d.TargetEdgeId+" @ "+d.TargetStation.ToString("F6",CultureInfo.InvariantCulture),
                    d.SourcePhysicalComponent,d.TargetPhysicalComponent,Xyz(d.SourcePoint),Xyz(d.TargetPoint),d.DeltaX.ToString("F6"),d.DeltaY.ToString("F6"),d.DeltaZ.ToString("F6"),
                    d.Distance3D.ToString("F6"),Angle(d.SourceDirectionAngleDegrees),Angle(d.TargetDirectionAngleDegrees??d.TargetTangentAngleDegrees),
                    d.SourceWidth.ToString("F6")+" / "+d.SourceHeight.ToString("F6"),d.TargetWidth.ToString("F6")+" / "+d.TargetHeight.ToString("F6"),
                    d.CandidateRank,d.CandidateCount,d.WithinVirtualConnectorMaxDistance?"是":"否，仅诊断"}));
            connectivityPrevious.Enabled=connectivityPage>0;connectivityNext.Enabled=connectivityPage+1<pages;
            connectivityPageLabel.Text="第 "+(connectivityPage+1)+" / "+pages+" 页 · "+candidates.Length+" 个 · 点击候选可定位红线";
        }

        void ClearConnectivityDiagnostics()
        {
            connectivityDiagnostics=null;highlightedBreakpoint=null;connectivityError=null;connectivityPage=0;CableOverlay.Breakpoints.Clear();ShowConnectivityPage();
        }

        void ShowConnectivityFailure(CablePathNotFoundException error)
        {
            connectivityDiagnostics=error.Diagnostics;connectivityError=error.Message;connectivityPage=0;UpdateDetails(null);ShowConnectivityPage();
            caption.Text="未找到路径 · 断点诊断";number.Text="—";
            hint.Text="起点分量 "+connectivityDiagnostics.StartPhysicalComponent+" → 终点分量 "+connectivityDiagnostics.FinishPhysicalComponent+" · 查看断点诊断";
            detailsTabs.SelectedIndex=4;
            var nearest=connectivityDiagnostics.Candidates.FirstOrDefault();if(nearest!=null)HighlightBreakpoint(nearest);else SetOverlay();
        }

        void HighlightBreakpoint(VirtualConnectorCandidate candidate)
        {
            Check();highlightedBreakpoint=candidate;CableOverlay.Lines.Clear();CableOverlay.Breakpoints.Clear();
            CableOverlay.Breakpoints.Add(Tuple.Create(candidate.SourcePoint,candidate.TargetPoint));
            using(var items=new ModelItemCollection())
            {
                items.AddRange(network.Geometry[candidate.SourcePiece]);items.AddRange(network.Geometry[candidate.TargetPiece]);
                var box=items.BoundingBox();if(!box.IsEmpty)using(var viewpoint=doc.CurrentViewpoint.CreateCopy()){viewpoint.ZoomBox(box);doc.CurrentViewpoint.CopyFrom(viewpoint);}
            }
            SetOverlay();hint.Text="红线："+candidate.Distance3D.ToString("F3")+" m · 每端口排名 "+candidate.CandidateRank+" · 仅用于断点定位";
        }
    }
}
