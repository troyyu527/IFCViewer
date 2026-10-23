using System;
using System.IO;
using System.Xml.Serialization;
using IfcViewer.Infrastructure;
using IfcViewer.Models;

namespace IfcViewer.Services
{
    /// <summary>
    /// Loads and saves AppSettings. A missing or damaged settings file gives the defaults.
    /// </summary>
    public class SettingsService
    {
        private static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IfcViewer", "settings.xml");

        public AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var stream = File.OpenRead(FilePath))
                    {
                        return (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Settings could not be read; using defaults", ex);
            }
            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var stream = File.Create(FilePath))
                {
                    new XmlSerializer(typeof(AppSettings)).Serialize(stream, settings);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Settings could not be saved", ex);
            }
        }
    }
}
