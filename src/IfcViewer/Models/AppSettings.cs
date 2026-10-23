namespace IfcViewer.Models
{
    /// <summary>
    /// What the app remembers between runs: window position and size, panel widths, last opened folder.
    /// Stored as XML in %LocalAppData%\IfcViewer\settings.xml.
    /// </summary>
    public class AppSettings
    {
        // Window position and size of the normal (not maximized) window; NaN = never saved, use the defaults.
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = 1280;
        public double WindowHeight { get; set; } = 800;
        public bool WindowMaximized { get; set; }

        public double TreeWidth { get; set; } = 260;
        public double PropertiesWidth { get; set; } = 350;

        // Folder of the last successfully opened file; the Open dialog starts there.
        public string LastFolder { get; set; }
    }
}
