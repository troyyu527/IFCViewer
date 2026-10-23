using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace IfcViewer.Services
{
    /// <summary>
    /// File dialogs and message boxes, so ViewModels don't create UI windows themselves.
    /// </summary>
    public class DialogService
    {
        // Shows an Open dialog for IFC files, starting in initialFolder if it still exists.
        // Returns the chosen path, or null if cancelled.
        public string OpenIfcFile(string initialFolder)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open IFC file",
                Filter = "IFC files (*.ifc)|*.ifc",
            };
            if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder)) dialog.InitialDirectory = initialFolder;
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public void ShowError(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public void ShowInfo(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
