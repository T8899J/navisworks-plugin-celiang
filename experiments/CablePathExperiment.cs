using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Plugins;
using JiePinPai.TrayMeasurement;
using JiePinPai.TrayMeasurement.Core;
using NavApp=Autodesk.Navisworks.Api.Application;
using UiColor=System.Drawing.Color;
using View=Autodesk.Navisworks.Api.View;

namespace TrayRouteExperiment
{
    [Plugin("CablePathExperimentV10","JPPM",DisplayName="电缆路径实验",ToolTip="从分支到主路，按点击位置计算路径")]
    [AddInPlugin(AddInLocation.AddIn)]
    public sealed class CableEntry:AddInPlugin
    {
        static CableForm form;
        public override int Execute(params string[] args)
        {
            if(form==null||form.IsDisposed){form=new CableForm();form.FormClosed+=(s,e)=>form=null;form.Show(NavApp.Gui.MainWindow);}
            form.Activate();if(args.Length>0)form.ConfigureReport(args[0]);
            if(args.Length>=5)form.DemoPoints(args[0],args[1],args[2],args[3],args[4]);
            else if(args.Length>=3)form.Demo(args[0],args[1],args[2]);return 0;
        }
    }
    [Plugin("CablePointPickerV10","JPPM")]
    public sealed class CablePicker:ToolPlugin
    {
        internal static CableForm Target;
        public override bool MouseDown(View view,KeyModifiers modifiers,ushort button,int x,int y,double timeOffset)
        {
            if(Target==null||(Control.MouseButtons&MouseButtons.Left)==0)return false;
            var hit=view.PickItemFromPoint(x,y);if(hit==null)return true;
            var form=Target;var item=hit.ModelItem;var p=hit.Point;var point=new Vec(p.X,p.Y,p.Z);
            // Dispatch outside the native input callback before changing the active Tool.
            form.BeginInvoke(new Action(()=>form.Picked(item,point)));return true;
        }
        public override bool KeyDown(View view,KeyModifiers modifiers,ushort key,double timeOffset)
        {if(key!=27||Target==null)return false;var f=Target;f.BeginInvoke(new Action(f.CancelPick));return true;}
    }
    [Plugin("CablePathOverlayV10","JPPM")]
    public sealed class CableOverlay:RenderPlugin
    {
        internal static Document Document;
        internal static ModelItem Root;
        internal static readonly List<Tuple<Vec,Vec,bool>> Lines=new List<Tuple<Vec,Vec,bool>>();
        internal static Vec? Start,End;
        internal static double Scale;
        public override void OverlayRender(View view,Autodesk.Navisworks.Api.Graphics graphics)
        {
            if(Document==null||Document!=NavApp.ActiveDocument||Document.IsClear||!Document.Models.RootItems.Any(x=>x.Equals(Root)))return;
            graphics.BeginModelContext();
            try
            {
                graphics.DepthTest(false);graphics.Lighting(false);graphics.LineWidth(5);
                foreach(var line in Lines)
                {graphics.Color(line.Item3?new Autodesk.Navisworks.Api.Color(1,.55,0):new Autodesk.Navisworks.Api.Color(0,.9,.2),1);graphics.Line(Point(line.Item1),Point(line.Item2));}
                if(Start.HasValue)Mark(graphics,Start.Value,new Autodesk.Navisworks.Api.Color(0,1,0));
                if(End.HasValue)Mark(graphics,End.Value,new Autodesk.Navisworks.Api.Color(1,.1,.1));
            }
            finally{graphics.EndModelContext();}
        }
        static Point3D Point(Vec v){return new Point3D(v.X/Scale,v.Y/Scale,v.Z/Scale);}
        static void Mark(Autodesk.Navisworks.Api.Graphics g,Vec p,Autodesk.Navisworks.Api.Color color)
        {g.Color(color,1);g.LineWidth(8);foreach(var d in new[]{new Vec(.05,0,0),new Vec(0,.05,0),new Vec(0,0,.05)})g.Line(Point(p-d),Point(p+d));}
    }
    internal sealed partial class CableForm:Form
    {
        readonly Label caption=new Label{Text="在桥架上点击起点和终点",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter};
        readonly Label number=new Label{Text="—",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter};
        readonly Label hint=new Label{Text="按点击位置计算，不累计整根主路",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter};
        readonly Button startButton=new Button{Text="选起点",Dock=DockStyle.Fill};
        readonly Button finishButton=new Button{Text="选终点并测量",Dock=DockStyle.Fill};
        readonly Button restoreButton=new Button{Text="恢复原视图",Dock=DockStyle.Fill};
        readonly Font resultFont=new Font("Segoe UI",38,FontStyle.Bold);
        HostNetwork network;Document doc;ModelItem originalRoot;Viewpoint originalView;ModelItemCollection originalSelection;
        readonly List<ModelItem> hidden=new List<ModelItem>();
        CableLocation start,finish;bool pickingStart;Tool priorTool;string priorCustom;
        string reportPath;int pickedPoints;
        public CableForm()
        {
            Text="电缆路径（端口图实验）";ClientSize=new Size(460,338);Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            BackColor=UiColor.FromArgb(246,247,249);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.Manual;
            var screen=Screen.PrimaryScreen.WorkingArea;Location=new System.Drawing.Point(screen.Left+80,screen.Top+200);
            caption.ForeColor=hint.ForeColor=UiColor.FromArgb(100,116,139);number.Font=resultFont;number.ForeColor=UiColor.FromArgb(15,23,42);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=5};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            var buttons=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));
            foreach(var b in new[]{startButton,finishButton,restoreButton}){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.Cursor=Cursors.Hand;}
            startButton.BackColor=UiColor.FromArgb(226,232,240);finishButton.BackColor=UiColor.FromArgb(37,99,235);finishButton.ForeColor=UiColor.White;restoreButton.ForeColor=UiColor.FromArgb(100,116,139);
            buttons.Controls.Add(startButton,0,0);buttons.Controls.Add(finishButton,1,0);layout.Controls.Add(caption,0,0);layout.Controls.Add(number,0,1);layout.Controls.Add(hint,0,2);layout.Controls.Add(buttons,0,3);layout.Controls.Add(restoreButton,0,4);Controls.Add(layout);
            AddDetailsUi(layout);
            startButton.Click+=(s,e)=>Safe(()=>BeginPick(true));finishButton.Click+=(s,e)=>Safe(()=>BeginPick(false));restoreButton.Click+=(s,e)=>Safe(Restore);
        }
        void Safe(Action action){try{UseWaitCursor=true;action();}catch(Exception e){number.Text="—";caption.Text="暂时无法测量";hint.Text=e.Message;CableOverlay.Lines.Clear();UpdateDetails(null);try{SaveReport(null,e.Message);}catch(Exception reportError){Debug.WriteLine(reportError);}Debug.WriteLine(e);}finally{UseWaitCursor=false;}}
        void Remember()
        {
            var d=NavApp.ActiveDocument;if(d==null||d.IsClear)throw new InvalidOperationException("请先打开模型");
            if(doc!=null){Check();return;}doc=d;originalRoot=d.Models.RootItems.First();originalView=d.CurrentViewpoint.CreateCopy();originalSelection=new ModelItemCollection();originalSelection.AddRange(d.CurrentSelection.SelectedItems);
        }
        void Check(){if(doc==null||doc!=NavApp.ActiveDocument||doc.IsClear||!doc.Models.RootItems.Any(x=>x.Equals(originalRoot)))throw new InvalidOperationException("模型已变化，请重新打开实验窗口");}
        void EnsureNetwork(ModelItem item)
        {
            Remember();if(network!=null){if(!network.Valid())throw new InvalidOperationException("模型已变化");return;}
            Reveal();caption.Text="正在读取桥架连接…";number.Text="—";Refresh();network=HostNetwork.Read(doc,item);
        }
        void BeginPick(bool first)
        {
            Remember();if(!first&&start==null)throw new InvalidOperationException("请先选择起点");
            CancelPick();pickingStart=first;priorTool=doc.Tool.Value;priorCustom=doc.Tool.CustomToolPluginId;
            CablePicker.Target=this;var record=(ToolPluginRecord)NavApp.Plugins.FindPlugin("CablePointPickerV10.JPPM");doc.Tool.SetCustomToolPlugin(record.LoadPlugin());
            caption.Text=first?"请在桥架上点击起点":"请在桥架上点击终点";hint.Text="Esc 取消选择";number.Text="—";
        }
        public void CancelPick()
        {
            if(CablePicker.Target!=this)return;CablePicker.Target=null;
            if(doc!=NavApp.ActiveDocument||doc.IsClear)return;
            if(priorTool==Tool.CustomToolPlugin&&!string.IsNullOrEmpty(priorCustom))
            {var r=NavApp.Plugins.FindPlugin(priorCustom) as ToolPluginRecord;if(r!=null)doc.Tool.SetCustomToolPlugin(r.LoadPlugin());else doc.Tool.Set(Tool.Select);}
            else doc.Tool.Set(priorTool);
            caption.Text="已取消选择";hint.Text="点击按钮重新选择起点或终点";number.Text="—";
        }
        public void Picked(ModelItem item,Vec documentPoint)
        {
            if(CablePicker.Target!=this)return;bool first=pickingStart;CancelPick();
            Safe(()=>{
                if(item.AncestorsAndSelf.Any(x=>x.DisplayName=="Maintenance Volume"))throw new InvalidOperationException("请点击桥架实体，避开检修空间");
                EnsureNetwork(item);int index=network.Index(item);if(index<0)throw new InvalidOperationException("这个构件未通过几何识别；展开明细查看具体原因");
                double scale=UnitConversion.ScaleFactor(doc.Units,Units.Meters);var location=network.Graph.Project(index,documentPoint*scale);pickedPoints++;
                if(first){start=location;finish=null;CableOverlay.Lines.Clear();SetOverlay();UpdateDetails(null);caption.Text="起点已记录";hint.Text="点击“选终点并测量”，再点终点";number.Text="—";}
                else{finish=location;Display(network.Graph.Find(start,finish),false);}
            });
        }
        void SetOverlay()
        {
            CableOverlay.Document=doc;CableOverlay.Root=originalRoot;CableOverlay.Scale=UnitConversion.ScaleFactor(doc.Units,Units.Meters);CableOverlay.Start=start==null?(Vec?)null:start.Point;CableOverlay.End=finish==null?(Vec?)null:finish.Point;
            ((RenderPluginRecord)NavApp.Plugins.FindPlugin("CablePathOverlayV10.JPPM")).LoadPlugin();doc.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
        }
        public void Demo(string report,string branchName,string mainName)
        {
            Safe(()=>{
                Remember();CancelPick();Reveal();reportPath=report;pickedPoints=0;
                var hits=doc.Models.RootItems.SelectMany(r=>r.DescendantsAndSelf).Where(x=>x.DisplayName==branchName).ToArray();if(hits.Length!=1)throw new InvalidOperationException("实验起点名称不唯一或不存在");
                EnsureNetwork(hits[0]);var graph=network.Graph;
                var join=graph.Joins.Single(j=>j.Kind=="branch-to-middle"&&graph.Pieces[j.A.Piece].Shape.Name==branchName&&graph.Pieces[j.B.Piece].Shape.Name==mainName);
                start=graph.At(join.A.Piece,join.A.Station==0?graph.Pieces[join.A.Piece].Shape.Length:0);finish=graph.At(join.B.Piece,graph.Pieces[join.B.Piece].Shape.Length);
                Display(graph.Find(start,finish),true);
            });
        }
        public void DemoPoints(string report,string firstName,string lastName,string firstPoint,string lastPoint)
        {
            Safe(()=>{
                Remember();CancelPick();Reveal();reportPath=report;pickedPoints=0;
                var hits=doc.Models.RootItems.SelectMany(r=>r.DescendantsAndSelf).Where(x=>x.DisplayName==firstName).ToArray();
                if(hits.Length!=1)throw new InvalidOperationException("实验起点名称不唯一或不存在");
                EnsureNetwork(hits[0]);var graph=network.Graph;
                var a=graph.Pieces.Select((p,i)=>new{p,i}).Single(x=>x.p.Shape.Name==firstName).i;
                var b=graph.Pieces.Select((p,i)=>new{p,i}).Single(x=>x.p.Shape.Name==lastName).i;
                Func<string,Vec> point=s=>{var q=s.Split(',').Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray();if(q.Length!=3)throw new ArgumentException("需要三维坐标");return new Vec(q[0],q[1],q[2]);};
                start=graph.Project(a,point(firstPoint));finish=graph.Project(b,point(lastPoint));
                Display(graph.Find(start,finish),true);
            });
        }
        void Display(CableRoute result,bool overview)
        {
            Check();Reveal();var selectedItems=result.Pieces.SelectMany(i=>network.Geometry[i]).ToArray();var keep=new HashSet<ModelItem>(selectedItems.SelectMany(x=>x.AncestorsAndSelf));
            Action<ModelItem> visit=null;visit=node=>{if(node.IsHidden)return;if(!keep.Contains(node)){hidden.Add(node);return;}foreach(var child in node.Children)visit(child);};foreach(var root in doc.Models.RootItems)visit(root);doc.Models.SetHidden(hidden,true);
            using(var selection=new ModelItemCollection()){selection.AddRange(selectedItems);doc.CurrentSelection.CopyFrom(selection);
                if(overview){using(var vp=doc.CurrentViewpoint.CreateCopy()){vp.AlignDirection(new Vector3D(.15,.35,-1));vp.AlignUp(new Vector3D(0,1,0));vp.ZoomBox(selection.BoundingBox());doc.CurrentViewpoint.CopyFrom(vp);}}}
            CableOverlay.Lines.Clear();foreach(var step in result.Steps)
            {
                var line=step.Centerline;
                if(line==null||line.Length<2)continue;
                for(int i=1;i<line.Length;i++)CableOverlay.Lines.Add(Tuple.Create(line[i-1],line[i],step.RequiresReview));
            }
            SetOverlay();caption.Text=(result.RequiresReview||network.Incomplete?"候选电缆路径":"总电缆路径长度")+" · "+result.Pieces.Length+" 个构件";number.Text=result.Length.ToString("0.000")+" m";
            double fittingLength=result.FittingContributions.Sum(f=>f.Length);
            string fittingSummary=result.FittingContributions.Count==0?"本段未经过配件":
                "已含配件 "+fittingLength.ToString("0.000")+" m · "+result.FittingContributions.Count+" 件";
            hint.Text=fittingSummary+(network.Incomplete?" · 模型未完整提取":result.RequiresReview?" · 待复核":"");
            UpdateDetails(result);SaveReport(result);
        }
        void Reveal(){if(hidden.Count==0)return;Check();doc.Models.SetHidden(hidden,false);hidden.Clear();}
        void Restore(){CancelPick();if(doc==null)return;Check();Reveal();doc.CurrentSelection.CopyFrom(originalSelection);doc.CurrentViewpoint.CopyFrom(originalView);CableOverlay.Lines.Clear();CableOverlay.Start=CableOverlay.End=null;doc.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);}
        protected override void OnFormClosed(FormClosedEventArgs e){try{Restore();}catch(Exception ex){Debug.WriteLine(ex);}CableOverlay.Document=null;if(originalView!=null)originalView.Dispose();if(originalSelection!=null)originalSelection.Dispose();base.OnFormClosed(e);}
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)resultFont.Dispose();}
    }
}
