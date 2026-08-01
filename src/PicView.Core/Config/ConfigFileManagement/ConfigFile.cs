namespace PicView.Core.Config.ConfigFileManagement;

public class ConfigFile(string configFileName)
{
    public const string ConfigFolder = "PicView/Config";
    
    public string ConfigFileName { get; } = configFileName;
    private string ConfigPath => Path.Combine(ConfigFolder, ConfigFileName);
    
    public string RoamingConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ConfigPath);
    
    public string LocalConfigPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", ConfigFileName);

    public string TryGetCurrentUserConfigPath =>
        File.Exists(RoamingConfigPath) ? RoamingConfigPath :
        File.Exists(LocalConfigPath) ? LocalConfigPath : string.Empty;

    /// <summary>
    /// Represents the appropriate resolved path to an active configuration file.
    /// This variable is used to track and store the determined file path for a specific configuration file.
    /// The value of <see cref="CorrectPath"/> is dynamically set during the runtime
    /// when attempting to locate or resolve the configuration file path.
    /// Typically, it is populated based on the file's actual existence on the system
    /// in roaming, local directory, or other fallback locations as needed.
    /// If the path cannot be resolved, the value may remain null until explicitly defined.
    /// </summary>
    public string? CorrectPath = null;
}