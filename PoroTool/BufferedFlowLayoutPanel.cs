using System.Windows.Forms;

namespace PoroTool
{
    /// <summary>
    /// FlowLayoutPanel with double buffering, so image grids scroll and
    /// repopulate without flicker.
    /// </summary>
    class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel()
        {
            DoubleBuffered = true;
        }
    }
}
