using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;

namespace JiePinPai.TrayMeasurement
{
    // Adapted from the original plugin's single modeless window pattern.
    [Plugin("JiePinPai_TrayMeasurement", "JPPM", DisplayName = "桥架测量实验", ToolTip = "测量选中直桥架的真实几何长度")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class PluginEntry : AddInPlugin
    {
        private static MeasurementForm openForm;
        public override int Execute(params string[] parameters)
        {
            try
            {
                if (openForm != null && !openForm.IsDisposed)
                { openForm.WindowState = FormWindowState.Normal; openForm.Activate(); return 0; }
                openForm = new MeasurementForm();
                openForm.FormClosed += (s, e) => openForm = null;
                var gui = Autodesk.Navisworks.Api.Application.Gui;
                if (gui == null) openForm.Show(); else openForm.Show(gui.MainWindow);
                return 0;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "桥架测量实验"); return -1; }
        }
    }
}
