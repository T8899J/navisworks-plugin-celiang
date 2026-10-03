using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace JiePinPai.TrayMeasurement
{
    public sealed class MeasurementForm : Form
    {
        private readonly Label itemName = new Label { Text = "请选择一段直桥架", AutoEllipsis = true, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        private readonly Label length = new Label { Text = "—", TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        private readonly Label millimetres = new Label { Text = "", TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        private readonly Label hint = new Label { Text = "选中构件后，点击下方按钮", TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        private readonly Button measure = new Button { Text = "测量选中构件", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        private readonly Font resultFont = new Font("Segoe UI", 38F, FontStyle.Bold);

        public MeasurementForm()
        {
            Text = "桥架长度";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(440, 300);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(246, 247, 249);

            var muted = Color.FromArgb(100, 116, 139);
            itemName.ForeColor = muted;
            millimetres.ForeColor = muted;
            hint.ForeColor = muted;
            length.Font = resultFont;
            length.ForeColor = Color.FromArgb(15, 23, 42);
            measure.BackColor = Color.FromArgb(37, 99, 235);
            measure.ForeColor = Color.White;
            measure.FlatAppearance.BorderSize = 0;
            measure.Cursor = Cursors.Hand;
            measure.Margin = new Padding(0, 10, 0, 0);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20), ColumnCount = 1, RowCount = 5 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.Controls.Add(itemName, 0, 0);
            layout.Controls.Add(length, 0, 1);
            layout.Controls.Add(millimetres, 0, 2);
            layout.Controls.Add(hint, 0, 3);
            layout.Controls.Add(measure, 0, 4);
            Controls.Add(layout);
            measure.Click += (s, e) => RunMeasurement();
        }

        private void RunMeasurement()
        {
            length.Text = "—";
            millimetres.Text = "";
            itemName.Text = "请选择一段直桥架";
            hint.Text = "正在测量…";
            measure.Enabled = false;
            UseWaitCursor = true;
            Refresh();
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null || doc.IsClear) { hint.Text = "请先打开模型"; return; }
                var selected = doc.CurrentSelection.SelectedItems;
                if (selected.Count != 1) { hint.Text = selected.Count == 0 ? "请先选中一段直桥架" : "一次只能测量一个构件"; return; }
                itemName.Text = string.IsNullOrWhiteSpace(selected[0].DisplayName) ? "已选构件" : selected[0].DisplayName;
                var result = GeometryReader.Read(doc, selected[0]).Result;
                if (!result.IsStraightCandidate)
                {
                    hint.Text = "暂时无法确定长度，请选择一段直桥架";
                    return;
                }
                length.Text = result.SpanMetres.ToString("0.###", CultureInfo.CurrentCulture) + " m";
                millimetres.Text = (result.SpanMetres * 1000).ToString("0.#", CultureInfo.CurrentCulture) + " mm";
                hint.Text = "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                hint.Text = "未能测量，请重新选择一段直桥架";
            }
            finally { UseWaitCursor = false; measure.Enabled = true; }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) resultFont.Dispose();
        }
    }
}