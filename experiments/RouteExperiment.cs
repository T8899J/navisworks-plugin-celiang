using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Plugins;
using JiePinPai.TrayMeasurement;
using NavApp=Autodesk.Navisworks.Api.Application;
using Color=System.Drawing.Color;

namespace TrayRouteExperiment
{
    [Plugin("TrayRouteExperiment","JPPM",DisplayName="桥架路径实验",ToolTip="在当前桥架组内验证端口连接与路径长度")]
    [AddInPlugin(AddInLocation.AddIn)]
    public sealed class Entry : AddInPlugin
    {
        static RouteForm form;
        public override int Execute(params string[] args)
        {
            if(form==null||form.IsDisposed)
            {
                form=new RouteForm();form.FormClosed+=(s,e)=>form=null;
                form.Show(NavApp.Gui.MainWindow);
            }
            form.Activate();
            if(args.Length>0)form.Demo(args[0]);
            return 0;
        }
    }
    internal sealed class Run
    {
        public readonly List<Part> Parts=new List<Part>();
        public readonly List<ModelItem> Items=new List<ModelItem>();
        public readonly List<string> Rejected=new List<string>();
        public List<Connection> Connections;
        public string Name;
        public int Anchor;
        public static string Key(ModelItem item){return string.Join("/",((Array)ComApiBridge.ToInwOaPath(item).ArrayData).Cast<object>().Select(Convert.ToInt32));}
        public static string Property(ModelItem item,string name)
        {
            foreach(var c in item.PropertyCategories)foreach(var p in c.Properties)
                if(p.DisplayName==name&&p.Value.IsDisplayString)return p.Value.ToDisplayString();
            return "";
        }
        public static ModelItem Component(ModelItem item)
        {
            while(item!=null){if(!string.IsNullOrEmpty(Property(item,"RunName")))return item;item=item.Parent;}
            throw new InvalidOperationException("请选中带有桥架组信息的构件");
        }
        public static Run Read(Document doc,ModelItem selected)
        {
            var component=Component(selected);var scope=component.Parent;
            string runName=Property(component,"RunName");
            if(scope==null||scope.DisplayName!=runName)throw new InvalidOperationException("暂不支持这个模型的桥架分组");
            var candidates=scope.Children.ToArray();
            if(candidates.Length>100)throw new InvalidOperationException("该组超过 100 个构件，请先用更小的桥架组实验");
            var run=new Run{Name=runName,Anchor=-1};string anchor=Key(component);
            foreach(var c in candidates)
            {
                try
                {
                    if(Property(c,"RunName")!=runName)throw new InvalidOperationException("桥架组信息不一致");
                    var nodes=c.DescendantsAndSelf.Where(x=>x.HasGeometry&&!x.IsHidden&&x.DisplayName=="Geometry").ToArray();
                    if(nodes.Length!=1)throw new InvalidOperationException("无法唯一确定实体几何");
                    var size=Regex.Match(Property(c,"Size"),@"^\s*([\d.]+)\s*mm\s*x\s*([\d.]+)\s*mm\s*$",RegexOptions.IgnoreCase);
                    if(!size.Success)throw new InvalidOperationException("规格单位不明确");
                    double width=double.Parse(size.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)/1000;
                    double height=double.Parse(size.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture)/1000;
                    string description=Property(c,"Description");
                    var mesh=GeometryReader.Read(doc,nodes[0]).Triangles;
                    Part part;
                    if(description.IndexOf("Straight",StringComparison.OrdinalIgnoreCase)>=0)part=RouteGeometry.Straight(mesh,width,height);
                    else if(Regex.IsMatch(description,@"\b90Deg\b",RegexOptions.IgnoreCase)&&description.IndexOf("Riser",StringComparison.OrdinalIgnoreCase)>=0)
                        part=RouteGeometry.Riser90(mesh,width,height);
                    else throw new InvalidOperationException("当前实验只支持直段和 90° 竖向弯");
                    part.Id=Key(c);part.Name=c.DisplayName;part.System=runName;
                    if(part.Id==anchor)run.Anchor=run.Parts.Count;
                    run.Parts.Add(part);run.Items.Add(nodes[0]);
                }
                catch(Exception e){run.Rejected.Add(c.DisplayName+": "+e.Message);}
            }
            if(run.Anchor<0)throw new InvalidOperationException("当前选中构件暂不能识别，请选择直段或 90° 竖向弯");
            run.Connections=RouteGeometry.Connect(run.Parts,.002);
            return run;
        }
        public Route DemoRoute()
        {
            var ends=Enumerable.Range(0,Parts.Count).Where(i=>Connections.Count(e=>e.A==i||e.B==i)==1).ToArray();
            Route best=null;
            for(int a=0;a<ends.Length;a++)for(int b=a+1;b<ends.Length;b++)
            {
                try{var r=RouteGeometry.Find(Parts,Connections,ends[a],ends[b]);if(r.Items.Contains(Anchor)&&(best==null||r.Length>best.Length))best=r;}
                catch(InvalidOperationException){}
            }
            if(best==null)throw new InvalidOperationException("当前构件附近未找到连续路径");
            return best;
        }
    }
    internal sealed class RouteForm : Form
    {
        readonly Label title=new Label{Text="选起点，再选终点",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter};
        readonly Label value=new Label{Text="—",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter};
        readonly Label hint=new Label{Text="当前桥架组内 · 整段测量",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter};
        readonly Button start=new Button{Text="设为起点",Dock=DockStyle.Fill};
        readonly Button end=new Button{Text="设为终点并测量",Dock=DockStyle.Fill};
        readonly Button restore=new Button{Text="恢复原视图",Dock=DockStyle.Fill};
        Document document; ModelItem originalRoot,startItem;
        ModelItemCollection selection;Viewpoint viewpoint;
        readonly List<ModelItem> hidden=new List<ModelItem>();
        readonly Font resultFont=new Font("Segoe UI",38,FontStyle.Bold);
        public RouteForm()
        {
            Text="桥架路径长度（实验）";ClientSize=new Size(450,320);Font=new Font("Microsoft YaHei UI",10);
            AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            BackColor=Color.FromArgb(246,247,249);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
            StartPosition=FormStartPosition.Manual;var area=Screen.PrimaryScreen.WorkingArea;Location=new System.Drawing.Point(area.Right-Width-30,area.Top+190);
            value.Font=resultFont;value.ForeColor=Color.FromArgb(15,23,42);title.ForeColor=hint.ForeColor=Color.FromArgb(100,116,139);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=5};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            var buttons=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));
            foreach(var b in new[]{start,end,restore}){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.Cursor=Cursors.Hand;}
            start.BackColor=Color.FromArgb(226,232,240);end.BackColor=Color.FromArgb(37,99,235);end.ForeColor=Color.White;restore.ForeColor=Color.FromArgb(100,116,139);
            buttons.Controls.Add(start,0,0);buttons.Controls.Add(end,1,0);
            layout.Controls.Add(title,0,0);layout.Controls.Add(value,0,1);layout.Controls.Add(hint,0,2);layout.Controls.Add(buttons,0,3);layout.Controls.Add(restore,0,4);Controls.Add(layout);
            start.Click+=(s,e)=>Safe(()=>{var d=NavApp.ActiveDocument;var item=Selected(d);Remember(d);startItem=Run.Component(item);value.Text="—";title.Text="起点已记录";hint.Text="选中终点构件，再点击测量";});
            end.Click+=(s,e)=>Safe(()=>{if(startItem==null)throw new InvalidOperationException("请先选中起点构件并设为起点");var d=NavApp.ActiveDocument;CheckDocument();var item=Run.Component(Selected(d));var run=Run.Read(d,startItem);int finish=run.Parts.FindIndex(p=>p.Id==Run.Key(item));if(finish<0)throw new InvalidOperationException("终点须为同一桥架组内可识别的构件");ShowRoute(d,run,RouteGeometry.Find(run.Parts,run.Connections,run.Anchor,finish));});
            restore.Click+=(s,e)=>Safe(Restore);
        }
        static ModelItem Selected(Document d){if(d==null||d.IsClear||d.CurrentSelection.SelectedItems.Count!=1)throw new InvalidOperationException("请先在模型中选中一个桥架构件");return d.CurrentSelection.SelectedItems[0];}
        void Safe(Action action)
        {
            try{UseWaitCursor=true;action();}
            catch(Exception e){value.Text="—";title.Text="暂时无法测量";hint.Text=e.Message;System.Diagnostics.Debug.WriteLine(e);}
            finally{UseWaitCursor=false;}
        }
        void Remember(Document d)
        {
            if(document!=null){CheckDocument();return;}
            document=d;originalRoot=d.Models.RootItems.First();selection=new ModelItemCollection();selection.AddRange(d.CurrentSelection.SelectedItems);viewpoint=d.CurrentViewpoint.CreateCopy();
        }
        void CheckDocument(){if(document==null||document.IsClear||document!=NavApp.ActiveDocument||!document.Models.RootItems.Any(x=>x.Equals(originalRoot)))throw new InvalidOperationException("模型已变化，请关闭实验窗口后重新打开");}
        public void Demo(string report)
        {
            Safe(()=>{
                var d=NavApp.ActiveDocument;var item=Selected(d);Remember(d);var run=Run.Read(d,item);var route=run.DemoRoute();
                ShowRoute(d,run,route);
                File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{model=d.FileName,units="m",scope=run.Name,definition="full components; polygonal geometric centerline; 2mm port tolerance",parts=run.Parts,connections=run.Connections,route=route,rejected=run.Rejected,manualLengthVerified=false}));
            });
        }
        void ShowRoute(Document d,Run run,Route route)
        {
            Remember(d);if(hidden.Count>0){d.Models.SetHidden(hidden,false);hidden.Clear();}
            var routeItems=route.Items.Select(i=>run.Items[i]).ToArray();
            var keep=new HashSet<ModelItem>(routeItems.SelectMany(x=>x.AncestorsAndSelf));
            Action<ModelItem> visit=null;visit=node=>{if(node.IsHidden)return;if(!keep.Contains(node)){hidden.Add(node);return;}foreach(var child in node.Children)visit(child);};
            foreach(var root in d.Models.RootItems)visit(root);
            d.Models.SetHidden(hidden,true);
            var selected=new ModelItemCollection();selected.AddRange(routeItems);d.CurrentSelection.CopyFrom(selected);
            var vp=d.CurrentViewpoint.CreateCopy();vp.ZoomBox(selected.BoundingBox());d.CurrentViewpoint.CopyFrom(vp);vp.Dispose();selected.Dispose();
            title.Text="已连通 · "+route.Items.Length+" 个构件";value.Text=route.Length.ToString("0.000")+" m";
            hint.Text="模型折线中心线 · 起止构件整段累计";
        }
        void Restore()
        {
            if(document==null)return;CheckDocument();
            if(hidden.Count>0){document.Models.SetHidden(hidden,false);hidden.Clear();}
            document.CurrentSelection.CopyFrom(selection);document.CurrentViewpoint.CopyFrom(viewpoint);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try{Restore();}catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex);}
            if(selection!=null)selection.Dispose();if(viewpoint!=null)viewpoint.Dispose();base.OnFormClosed(e);
        }
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)resultFont.Dispose();}
    }
}
