namespace GenShin_Launcher_Plus.Models;

/// <summary>
/// Game state for the launcher UI, determines what the action button shows.
/// </summary>
public enum GameState
{
    /// <summary>Game is not installed</summary>
    NotInstalled = 0,
    /// <summary>Game is installed and up to date, ready to launch</summary>
    Ready = 1,
    /// <summary>Game needs an update</summary>
    NeedUpdate = 2,
    /// <summary>Pre-download is available</summary>
    PreDownloadAvailable = 3,
    /// <summary>Game install/update is in progress</summary>
    Installing = 4,
    /// <summary>Game process is running</summary>
    Running = 5,
    /// <summary>Install/update completed</summary>
    InstallComplete = 6,
}

/// <summary>
/// Install operation type
/// </summary>
public enum GameInstallOperation
{
    Install = 0,
    Update = 1,
    PreDownload = 2,
}

/// <summary>
/// Install task state
/// </summary>
public enum GameInstallState
{
    Idle = 0,
    Preparing = 1,
    Downloading = 2,
    Extracting = 3,
    Verifying = 4,
    Finished = 5,
    Error = 6,
    Paused = 7,
}
