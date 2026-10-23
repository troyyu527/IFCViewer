using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IfcViewer.Infrastructure;
using Xbim.Common.Configuration;

namespace IfcViewer
{
    /// <summary>
    /// Application entry point: configures xBIM once at startup, gives it a temp folder of its own, and catches
    /// unexpected errors so they are logged and shown instead of closing the app silently.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            UseOwnTempFolder();
            Log.DeleteOldFiles();
            // RuntimeInformation gives the real Windows version (Environment.OSVersion reports 6.2 without a manifest)
            Log.Info($"Started {typeof(App).Assembly.GetName().Version}, {(Environment.Is64BitProcess ? "64" : "32")}-bit, {RuntimeInformation.OSDescription}");

            DispatcherUnhandledException += OnUiThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnFatalException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // xBIM v6 configuration, once per app. Heuristic: small files are kept in memory (fast), large files in a
            // temporary database on disk (the in-memory model needs ~18x the file size). The size limit is passed
            // per file in IfcModelService.
            XbimServices.Current.ConfigureServices(s => s.AddXbimToolkit(opt => opt.AddHeuristicModel()));

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Info("Exited");
            base.OnExit(e);
        }

        // An error on the UI thread that nothing caught: log it, tell the user, keep the app running.
        private void OnUiThreadException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Unexpected error", e.Exception);
            MessageBox.Show(
                $"Something went wrong:\n\n{e.Exception.Message}\n\nThe app keeps running. Details are in the log file:\n{Log.Folder}",
                "IFC Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        // An error on another thread: the process will end, so at least the log gets it.
        private static void OnFatalException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error("Fatal error", e.ExceptionObject as Exception);
        }

        // A failed background task nobody waited for: log it and keep running.
        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Error("Unobserved background error", e.Exception);
            e.SetObserved();
        }

        // Large models are kept in temporary database files (Esent). They are deleted when the model is closed, but
        // a crash, a killed process or a timing race at exit can leave them behind. So the process uses its own temp
        // folder, and whatever is left there from earlier runs is removed at startup. Files still in use by another
        // running copy are locked and skipped. Only this app writes to the folder, so nothing else is touched.
        private static void UseOwnTempFolder()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IfcViewer", "Temp");
            try
            {
                Directory.CreateDirectory(folder);
                foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); }
                    catch (IOException) { }                 // in use by another running copy
                    catch (UnauthorizedAccessException) { }
                }
                // xBIM also creates Xbim.<guid> subfolders; deepest first, and only once empty
                foreach (var dir in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    try { Directory.Delete(dir); }
                    catch (IOException) { }                 // not empty: files still in use
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (Exception)
            {
                return; // no own folder: keep using the normal temp folder
            }

            // Path.GetTempPath() (used by xBIM) reads TMP, then TEMP, from the process environment
            Environment.SetEnvironmentVariable("TMP", folder);
            Environment.SetEnvironmentVariable("TEMP", folder);
        }
    }
}
