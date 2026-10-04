using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TrayRouteExperiment
{
    internal sealed partial class CableForm
    {
        readonly Button detailsButton = new Button { Text = "查看明细与断点诊断", Dock = DockStyle.Top, Height = 38, FlatStyle = FlatStyle.Flat };
        readonly TabControl detailsTabs = new TabControl { Dock = DockStyle.Fill, Visible = false };
        readonly DataGridView edgeGrid = Grid(), fittingGrid = Grid(), reviewGrid = Grid(), rejectedGrid = Grid();
        bool detailsExpanded;
        const int RejectedPageSize = 100;
        static readonly string[] RejectedHeaders = { "ModelItem ID", "DisplayName", "RunName", "Description", "Size", "类型分类", "几何失败原因" };
        readonly Button rejectedPrevious = new Button { Text = "上一页", Width = 76, Height = 28, FlatStyle = FlatStyle.Flat };
        readonly Button rejectedNext = new Button { Text = "下一页", Width = 76, Height = 28, FlatStyle = FlatStyle.Flat };
        readonly Label rejectedPageLabel = new Label { AutoSize = true, Margin = new Padding(8, 7, 8, 0) };
        object[][] rejectedRows = new object[0][];
        int rejectedPage;

        static DataGridView Grid()
        {
            return new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 32,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.False },
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.False },
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        }

        void AddDetailsUi(Control summary)
        {
            summary.Dock = DockStyle.Top; summary.Height = 300;
            detailsButton.FlatAppearance.BorderSize = 0;
            detailsButton.ForeColor = Color.FromArgb(37, 99, 235);
            detailsButton.Click += (s, e) => {
                bool expand = !detailsExpanded;
                SuspendLayout(); detailsTabs.SuspendLayout();
                try
                {
                    if (!expand) { detailsTabs.Visible = false; detailsTabs.SelectedIndex = 0; }
                    ClientSize = expand ? new Size(1000, 650) : new Size(460, 338);
                    if (expand) detailsTabs.Visible = true;
                    detailsExpanded = expand;
                    detailsButton.Text = expand ? "收起明细" : "查看明细与断点诊断";
                }
                finally { detailsTabs.ResumeLayout(false); ResumeLayout(true); }
                var area = Screen.FromControl(this).WorkingArea;
                Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
            };
            AddTab("内部路径", edgeGrid); AddTab("配件已计入长度", fittingGrid); AddTab("待复核", reviewGrid); AddTab("跳过 / 未识别", rejectedGrid);
            AddConnectivityTab();Controls.Clear(); Controls.Add(detailsTabs); Controls.Add(detailsButton); Controls.Add(summary);
        }

        void AddTab(string title, DataGridView grid)
        {
            var tab = new TabPage(title); tab.Controls.Add(grid);
            if (grid == rejectedGrid)
            {
                var pager = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, WrapContents = false };
                pager.Controls.Add(rejectedPrevious); pager.Controls.Add(rejectedNext); pager.Controls.Add(rejectedPageLabel);
                rejectedPrevious.Click += (s, e) => { rejectedPage--; ShowRejectedPage(); };
                rejectedNext.Click += (s, e) => { rejectedPage++; ShowRejectedPage(); };
                tab.Controls.Add(pager);
            }
            detailsTabs.TabPages.Add(tab);
        }

        void ShowRejectedPage()
        {
            int pages = Math.Max(1, (rejectedRows.Length + RejectedPageSize - 1) / RejectedPageSize);
            rejectedPage = Math.Max(0, Math.Min(rejectedPage, pages - 1));
            // Limit actual accessible rows as well as layout work. VirtualMode would still expose
            // every RowCount entry to UI Automation / MSAA when a diagnostic table is inspected.
            Rows(rejectedGrid, RejectedHeaders, rejectedRows.Skip(rejectedPage * RejectedPageSize).Take(RejectedPageSize));
            rejectedPrevious.Enabled = rejectedPage > 0; rejectedNext.Enabled = rejectedPage + 1 < pages;
            rejectedPageLabel.Text = "第 " + (rejectedPage + 1) + " / " + pages + " 页 · 共 " + rejectedRows.Length + " 个 · 每页 " + RejectedPageSize + " 个";
        }

        static void Rows(DataGridView grid, string[] headers, IEnumerable<object[]> rows)
        {
            grid.SuspendLayout();
            try
            {
                grid.Rows.Clear(); grid.Columns.Clear();
                foreach (var header in headers)
                {
                    int width = header == "Description" || header.Contains("原因") ? 380 :
                        header == "构件" || header == "配件" || header == "位置" || header == "DisplayName" || header == "RunName" ? 240 :
                        header == "ModelItem ID" || header == "经过的内部边" ? 190 : 160;
                    grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "c" + grid.Columns.Count, HeaderText = header,
                        AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = width, MinimumWidth = 60 });
                }
                var batch = rows.Select(values => { var row = new DataGridViewRow(); row.CreateCells(grid, values); return row; }).ToArray();
                if (batch.Length > 0) grid.Rows.AddRange(batch);
            }
            finally { grid.ResumeLayout(false); grid.Invalidate(); }
        }

        string PieceName(int piece) { return piece < 0 ? "外部连接" : network.Graph.Pieces[piece].Shape.Name; }

        void UpdateDetails(CableRoute route)
        {
            if (network == null) return;
            Rows(edgeGrid, new[] { "构件", "类型", "InternalEdge", "本次经过长度 (m)", "入口 station", "出口 station", "复核原因" },
                route == null ? Enumerable.Empty<object[]>() : route.InternalEdges.Select(s => new object[] {
                    PieceName(s.Piece), network.Graph.Pieces[s.Piece].Shape.Kind, s.EdgeId, s.Length.ToString("0.000000"), s.From.ToString("0.000000"), s.To.ToString("0.000000"), s.ReviewReason }));
            Rows(fittingGrid, new[] { "配件", "类型", "已计入总长 (m)", "经过的内部边" },
                route == null ? Enumerable.Empty<object[]>() : route.FittingContributions.Select(f => new object[] { f.Name, f.Kind, f.Length.ToString("0.000000"), string.Join(", ", f.EdgeIds) }));
            Rows(reviewGrid, new[] { "位置", "边类型", "长度 (m)", "RequiresReview 原因" },
                route == null ? Enumerable.Empty<object[]>() : route.Steps.Where(s => s.RequiresReview).Select(s => new object[] {
                    s.Join == null ? PieceName(s.Piece) : PieceName(s.Join.A.Piece) + " → " + PieceName(s.Join.B.Piece),
                    s.Kind.ToString(), s.Length.ToString("0.000000"), s.ReviewReason }));
            rejectedRows = network.Rejected.Select(r => new object[] { r.ModelItemId, r.DisplayName, r.RunName, r.Description, r.Size, r.TypeClassification, r.GeometryFailureReason }).ToArray();
            rejectedPage = 0; ShowRejectedPage();
            detailsTabs.TabPages[3].Text = "跳过 / 未识别 (" + network.Rejected.Count + ")";
        }

        public void ConfigureReport(string path) { reportPath = path; }

        void SaveReport(CableRoute route, string error = null)
        {
            if (network == null || string.IsNullOrEmpty(reportPath)) return;
            var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath)); Directory.CreateDirectory(directory);
            File.WriteAllText(reportPath, new JavaScriptSerializer { MaxJsonLength = 50000000 }.Serialize(new {
                model = doc.FileName, definition = "Port / Junction / Edge graph; physical components first; 3D review-only VirtualConnector",
                start, finish, result = route, picked3DPoints = pickedPoints, error,
                recognized = network.Recognized, rejected = network.Rejected, incomplete = network.Incomplete,
                scan = new { network.ScanNodeCount, network.ScanCandidateCount, network.ScanElapsedSeconds },
                settings = new { network.Graph.PhysicalTolerance, network.Graph.GapBridgeMaxDistance,
                    network.Graph.GapBridgeWidthAxisTolerance, network.Graph.GapBridgeHeightAxisTolerance, network.Graph.GapBridgeSizeTolerance, network.Graph.VirtualConnectorMaxDistance, network.Graph.VirtualConnectorExperimentalTopN, network.Graph.VirtualConnectorTopN },
                graphNodes = network.Graph.GraphNodes, graphEdges = network.Graph.GraphEdges,
                physicalComponents = network.Graph.PhysicalPieceComponents, physicalComponentCount = network.Graph.PhysicalComponentCount,
                virtualConnectorCandidates = network.Graph.VirtualConnectorCandidates, physicalBoundaryPorts = network.Graph.PhysicalBoundaryPorts,
                connectivityDiagnostics, highlightedBreakpoint,
                rejectionSummary = network.Rejected.GroupBy(r=>r.GeometryFailureReason).Select(g=>new{reason=g.Key,count=g.Count()}),
                ambiguities = network.Graph.Ambiguities, manualPassabilityVerified = false, manualLengthVerified = false
            }));
        }
    }
}
