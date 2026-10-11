using System.Collections.Generic;
using System.Xml.Serialization;

namespace WinLaunch.Legacy;

// Byte-compatible mirror of the original WinLaunch persistence schema.
// Field and element names intentionally match the 0.7.x format so files written
// by the original app and by Enhanced are interchangeable in both directions.
public class ICItem
{
    public List<ICItem> Items = new List<ICItem>();

    public bool IsFolder = false;

    public int GridIndex = 0;
    public int Page = 0;

    public string Name = "";
    public string Keywords = "";
    public string Notes = "";
    public string Application = "";
    public string Arguments = "";
    public bool RunAsAdmin = false;
    public string IconPath = null;
    public bool ShowMiniatures = true;
}

// Mirror of the original Settings XML. Every property the 0.7.x app reads is
// preserved; unknown future elements are ignored on load, never dropped on save.
[XmlRoot("Settings")]
public class LegacySettings
{
    public string Versionstring = "0.0.0.0";

    public bool CheckForUpdatesFrequently { get; set; } = true;
    public bool UpdateSilently { get; set; } = false;

    public bool SortItemsAlphabetically { get; set; } = false;
    public bool SortFoldersFirst { get; set; } = false;
    public bool SortFolderContentsOnly { get; set; } = false;

    public bool WatchForDesktopLinks { get; set; } = true;
    public bool DeleteDesktopLinksAfterAdding { get; set; } = false;

    public int Columns { get; set; } = 8;
    public int Rows { get; set; } = 5;
    public int FolderColumns { get; set; } = 8;
    public int FolderRows { get; set; } = 5;

    public double IconSize { get; set; } = 1.4;
    public bool ShowItemIconText { get; set; } = true;
    public bool ShowFolderIconText { get; set; } = true;

    public bool ExtensionIconVisible { get; set; } = true;
    public bool SearchBarVisible { get; set; } = true;
    public bool PageIndicatorsVisible { get; set; } = true;

    public bool TabletMode { get; set; } = false;
    public bool FillScreen { get; set; } = false;
    public bool FreeItemPlacement { get; set; } = false;
    public bool DeskMode { get; set; } = false;
    public bool LegacyDeskMode { get; set; } = false;
    public int ScreenIndex { get; set; } = 0;

    public bool CheckForUpdates { get; set; } = true;
    public string SelectedLanguage { get; set; } = "en-US";
    public bool BlockOnFullscreen { get; set; } = true;
    public bool OpenOnActiveDesktop { get; set; } = true;

    public bool HotKeyEnabled { get; set; } = true;
    public bool HotAlt { get; set; } = true;
    public bool HotControl { get; set; } = true;
    public bool HotShift { get; set; } = true;
    public bool HotWin { get; set; } = false;
    public string HotKeyExtend { get; set; } = "Tab";

    public bool HotCornersEnabled { get; set; } = false;
    public bool HotTopLeft { get; set; } = false;
    public bool HotTopRight { get; set; } = false;
    public bool HotBottomRight { get; set; } = false;
    public bool HotBottomLeft { get; set; } = false;
    public double HotCornerDelay { get; set; } = 0.0;

    public bool WindowsKeyActivationEnabled { get; set; } = false;
    public bool DoubleTapCtrlActivationEnabled { get; set; } = false;
    public bool DoubleTapAltActivationEnabled { get; set; } = false;

    public string MiddleMouseActivation { get; set; } = "DoubleClicked";

    public bool GamepadActivation { get; set; } = false;
    public bool VoiceActivation { get; set; } = false;
}
