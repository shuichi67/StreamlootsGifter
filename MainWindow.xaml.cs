using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace StreamlootsManager;

public partial class MainWindow : Window
{
    private static string _themeBackgroundStart = "#0B1020";
    private static string _themeBackgroundEnd = "#111A30";
    private static string _themePanel = "#151D30";
    private static string _themeInput = "#0E1525";
    private static string _themeButton = "#25324A";
    private static string _themeAccent = "#5D6EFF";
    private static string _themeText = "#F4F6FF";
    private static string _themeMuted = "#9AA7C0";
    private static string _themeLine = "#26324A";
    private static string _themeSuccess = "#35C979";
    private static string _themeDanger = "#FF8585";
    private static string _themeWarning = "#E5B94C";
    private static string _themeSuccessSoft = "#174331";
    private static string _themeDangerSoft = "#492B36";
    private static readonly Dictionary<string, SolidColorBrush> _themeBrushes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ThemePalette> ThemePresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Midnight"] = new("#0B1020", "#0B1020", "#151D30", "#0E1525", "#25324A", "#5D6EFF", "#F4F6FF", "#9AA7C0", "#26324A", "#35C979", "#FF8585", "#E5B94C", "#174331", "#492B36"),
        ["Light"] = new("#F2F3F5", "#F2F3F5", "#FFFFFF", "#E8E9EC", "#E1E3E6", "#5865F2", "#232428", "#5C6068", "#D1D3D7", "#167A43", "#B42332", "#8A6200", "#E1F3E7", "#FBE4E7"),
        ["Ash"] = new("#383A40", "#383A40", "#313338", "#232428", "#3F424A", "#5865F2", "#F2F3F5", "#C3C5CA", "#4A4D55", "#35C979", "#FF8585", "#E5B94C", "#174331", "#492B36"),
        ["Dark"] = new("#232428", "#232428", "#2B2D31", "#1E1F22", "#383A40", "#5865F2", "#DBDEE1", "#B5BAC1", "#3A3C42", "#35C979", "#FF8585", "#E5B94C", "#174331", "#492B36"),
        ["Onyx"] = new("#08090A", "#08090A", "#16171A", "#0B0C0E", "#222328", "#5865F2", "#F2F3F5", "#A1A4AA", "#303137", "#35C979", "#FF8585", "#E5B94C", "#174331", "#492B36")
    };
    private readonly ConfigStore _configStore = new(AppPaths.Config);
    private readonly AppConfig _config;
    private readonly LocalGiftQueue _giftQueue;
    private readonly TwitchService _twitch;
    private readonly Dictionary<string, Border> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _cardBodies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Grid> _cardHeadings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _addButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _removeButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _collapseButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Border> _widgetDropHints = new(StringComparer.Ordinal);
    private readonly List<GridSplitter> _dashboardSplitters = [];
    // Kept for the legacy drag-preview helpers while the tree-based dock is introduced.
    private StackPanel? _dashboardLeftColumn;
    private StackPanel? _dashboardRightColumn;
    private GridSplitter? _dashboardColumnSplitter;
    private DashboardDragAdorner? _dashboardDragAdorner;
    private AdornerLayer? _dashboardAdornerLayer;
    private Border? _dashboardDraggedCard;
    private List<string>? _dashboardDragOriginalOrder;
    private List<string>? _dashboardDragPreviewOrder;
    private Dictionary<string, Point>? _dashboardDragBasePositions;
    private Point[]? _dashboardDragColumnStarts;
    private bool _dashboardDragDropped;
    private readonly Dictionary<string, Thumb> _resizeGrips = new(StringComparer.Ordinal);
    private StackPanel? _availableWidgetsPanel;
    private StackPanel? _addedWidgetsPanel;
    private readonly Dictionary<string, TextBox> _mappingInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _packInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComboBox> _cardCountInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GiftActivity> _activityById = new(StringComparer.Ordinal);
    private DashboardLayoutNode? _layoutBeforeEdit;
    private string? _activeWidgetDragKey;
    private DashboardLayoutNode? _layoutBeforeWidgetDrag;
    private string? _pendingWidgetDropTarget;
    private DashboardDropSide? _pendingWidgetDropSide;
    private bool _pendingWidgetDragRemoval;
    private bool _widgetDragLayoutDetached;
    private bool _widgetDragRemovalQueued;
    private Window? _historyCleanupWindow;
    private readonly ObservableCollection<GiftActivity> _activity = [];
    private readonly HashSet<string> _claimedViewers = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _freeChestTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _bridgeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _clientId;
    private readonly string _clientSecret;
    private string _activePage = "dashboard";
    private string? _activeWindowId;
    private DateTimeOffset? _freeChestUntil;
    private IReadOnlyList<FreeChestRule> _activeRules = [];
    private TextBlock? _freeChestStatus;
    private TextBlock? _freeChestChatActivity;
    private TextBlock? _freeChestCountdown;
    private TextBlock? _freeChestCompactCountdown;
    private UIElement? _freeChestExpandedBody;
    private UIElement? _freeChestCompactBody;
    private Button? _startFreeChestButton;
    private Button? _cancelFreeChestButton;
    private Button? _compactStartFreeChestButton;
    private Button? _compactStopFreeChestButton;
    private TextBox? _keywordInput;
    private TextBox? _collectionInput;
    private TextBox? _packsInput;
    private TextBox? _freeChestDurationInput;
    private ComboBox? _cardsInput;
    private StackPanel? _freeChestRulesContainer;
    private FrameworkElement? _freeChestTimerButtons;
    private FrameworkElement? _freeChestDurationRow;
    private FrameworkElement? _freeChestKeywordField;
    private FrameworkElement? _freeChestCollectionField;
    private FrameworkElement? _freeChestChestField;
    private FrameworkElement? _freeChestCardsField;
    private FrameworkElement? _freeChestAddField;
    private FrameworkElement? _freeChestRuleSwitch;
    private FrameworkElement? _freeChestRuleEditRemoveActions;
    private Button? _addFreeChestRuleButton;
    private Button? _cancelFreeChestRuleEditButton;
    private FreeChestRule? _editingFreeChestRule;
    private ComboBox? _historyFilter;
    private ComboBox? _historySourceFilter;
    private ComboBox? _activitySourceFilter;
    private FrameworkElement? _activityUserFilterField;
    private FrameworkElement? _activitySourceFilterField;
    private Button? _activityStatusFilterButton;
    private Popup? _activityStatusPopup;
    private CheckBox? _activityStatusSelectAll;
    private readonly Dictionary<string, CheckBox> _activityStatusChecks = new(StringComparer.Ordinal);
    private bool _updatingActivityStatusChecks;
    private TextBox? _historyUserFilter;
    private TextBox? _activityUserFilter;
    private IReadOnlyList<string> _historyNameSuggestions = [];
    private IReadOnlyList<string> _activityNameSuggestions = [];
    private ToggleButton? _showHiddenRewards;
    private StackPanel? _historyList;
    private StackPanel? _activityList;
    private StackPanel? _streamlootsCardRulesList;
    private FrameworkElement? _historyFiltersPanel;
    private FrameworkElement? _historyUsernameField;
    private FrameworkElement? _historySourceField;
    private FrameworkElement? _historyStatusField;
    private FrameworkElement? _historyFirstRow;
    private FrameworkElement? _activityFiltersPanel;
    private FrameworkElement? _streamlootsCardForm;
    private FrameworkElement? _streamlootsCardNameField;
    private FrameworkElement? _streamlootsCardActionField;
    private FrameworkElement? _streamlootsCardCollectionField;
    private FrameworkElement? _streamlootsCardChestField;
    private FrameworkElement? _streamlootsCardCardsField;
    private FrameworkElement? _streamlootsCardAddField;
    private FrameworkElement? _streamlootsCardCancelField;
    private FrameworkElement? _streamlootsCardRuleSwitch;
    private FrameworkElement? _streamlootsCardRuleEditRemoveActions;
    private FrameworkElement? _rewardToolbar;
    private Button? _rewardRefreshButton;
    private Button? _rewardSaveButton;
    private Button? _historyCleanupButton;
    private Button? _bridgeCopyButton;
    private Button? _bridgeGenerateButton;
    private readonly Dictionary<string, FrameworkElement> _rewardEnabledControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _rewardHideControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _rewardMappingFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _rewardChestFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _rewardCardFields = new(StringComparer.Ordinal);
    private StackPanel? _viewerAliasRows;
    private Border? _viewerAliasRowsContainer;
    private Button? _viewerAliasesToggle;
    private TextBox? _viewerAliasSearchBox;
    private TextBlock? _viewerAliasSearchPlaceholder;
    private TextBlock? _streamlootsCardStatus;
    private TextBlock? _streamlootsFeedInstruction;
    private TextBox? _streamlootsCardNameInput;
    private ComboBox? _streamlootsCardActionInput;
    private TextBox? _streamlootsCardCollectionInput;
    private TextBox? _streamlootsCardChestsInput;
    private ComboBox? _streamlootsCardCardsInput;
    private Button? _addStreamlootsCardRuleButton;
    private Button? _cancelStreamlootsCardRuleEditButton;
    private StreamlootsCardRule? _editingStreamlootsCardRule;
    private TextBlock? _bridgeStatus;
    private Ellipse? _bridgeStatusDot;
    private TextBox? _bridgeKeyBox;
    private Button? _bridgeRevealButton;
    private bool _bridgeServerReady;
    private string? _bridgeError;
    private bool _bridgeKeyVisible;
    private ImageSource? _profileImage;
    private bool _isClosing;
    private bool _updatingUserSuggestions;
    private bool _isDashboardEditing;
    private bool _loginHelpIsOpen;
    private double _loginHelpShiftX;
    private int _introductionStep = -1;
    private bool _isWidgetGuide;
    private bool _widgetGuideAddedFreeChestExample;
    private bool _widgetGuideAddedStreamlootsExample;
    private int _widgetGuideStep = -1;
    private string? _activeWidgetGuideKey;
    private readonly List<WidgetGuideStep> _widgetGuideSteps = [];
    private FrameworkElement? _settingsTourTarget;
    private readonly List<SettingsSectionEntry> _settingsSections = [];
    private TextBlock? _settingsNoResults;
    private string _settingsSearchQuery = "";

    public MainWindow()
    {
        InitializeComponent();
        var versionLabel = $"v{AppUpdater.CurrentVersion.ToString(3)}";
        LoginVersionLabel.Text = versionLabel;
        DashboardVersionLabel.Text = versionLabel;
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WindowProc);
        };
        _availableWidgetsPanel = AvailableWidgetsPanel;
        _addedWidgetsPanel = AddedWidgetsPanel;
        _config = _configStore.Load();
        var hadSavedLayout = _config.DashboardLayout is not null;
        _config.DashboardLayout = hadSavedLayout
            ? NormalizeDashboardLayout(_config.DashboardLayout, new HashSet<string>(StringComparer.Ordinal))
            : BuildInitialDashboardLayout(_config.SectionOrder.Where(_config.DashboardSections.Contains).ToArray());
        SyncDashboardSectionsFromLayout();
        ApplyThemePreset(_config.ThemePreset);
        var env = LoadEnvironment(AppPaths.EnvironmentFile);
        _clientId = env.GetValueOrDefault("TWITCH_CLIENT_ID", "");
        _clientSecret = env.GetValueOrDefault("TWITCH_CLIENT_SECRET", "");
        _giftQueue = new LocalGiftQueue(AppPaths.Queue, AppPaths.BridgeKey);
        _giftQueue.StreamlootsCardRuleNamesProvider = () => Dispatcher.Invoke(() => _config.StreamlootsCardRules.Where(rule => rule.Enabled).Select(rule => rule.CardName).ToArray());
        _giftQueue.StreamlootsLanguageProvider = () => Dispatcher.Invoke(() => _config.StreamlootsLanguage);
        _twitch = new TwitchService(_clientId, _clientSecret);

        BuildCards();
        BuildNavigation();
        _freeChestTimer.Tick += (_, _) => UpdateFreeChestTimer();
        _bridgeTimer.Tick += (_, _) => UpdateBridgeStatus();
        _giftQueue.GiftUpdated += job => Dispatcher.InvokeAsync(() => OnGiftUpdated(job));
        _giftQueue.StreamlootsCardReceived += cardEvent => Dispatcher.Invoke(() => HandleStreamlootsCardRedeemed(cardEvent));
        _twitch.StatusChanged += status => Dispatcher.InvokeAsync(() =>
        {
            ConnectionStatus.Text = status;
            ConnectionStatus.Foreground = status.Contains("reconnecting", StringComparison.OrdinalIgnoreCase)
                ? Brush("#E5B94C") : Brush("#35C979");
            UpdateFreeChestCompactControls();
        });
        _twitch.ChatReadyChanged += ready => Dispatcher.InvokeAsync(() =>
        {
            UpdateFreeChestCompactControls();
            if (_freeChestChatActivity is not null)
                _freeChestChatActivity.Text = ready ? "Chat listener connected · waiting for a message" : "Chat listener connecting…";
            if (!_freeChestUntil.HasValue && _freeChestStatus is not null)
                _freeChestStatus.Text = ready ? "Free Chest Hour is inactive." : _twitch.User is null
                    ? "Inactive · connect Twitch to listen for keywords"
                    : "Connecting to Twitch chat…";
        });
        _twitch.RedemptionReceived += redemption => Dispatcher.InvokeAsync(() => HandleRedemption(redemption));
        _twitch.ChatMessageReceived += message => Dispatcher.InvokeAsync(() => HandleChatMessage(message));
        Loaded += Window_Loaded;
        Closed += Window_Closed;
        ShowPage("dashboard");
    }

    private void WindowTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowSize();
            e.Handled = true;
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ToggleWindowSize_Click(object sender, RoutedEventArgs e) => ToggleWindowSize();

    private void ToggleWindowSize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmGetMinMaxInfo = 0x0024;
        if (message != wmGetMinMaxInfo) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, 2);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo)) return IntPtr.Zero;

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        info.MaxPosition.X = monitorInfo.Work.Left - monitorInfo.Monitor.Left;
        info.MaxPosition.Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top;
        info.MaxSize.X = monitorInfo.Work.Right - monitorInfo.Work.Left;
        info.MaxSize.Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top;
        Marshal.StructureToPtr(info, lParam, false);
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _giftQueue.Start();
            _bridgeServerReady = true;
            _bridgeTimer.Start();
            UpdateBridgeStatus();
        }
        catch (Exception ex)
        {
            _bridgeError = ex.Message;
            UpdateBridgeStatus();
            MessageBox.Show(this, ex.Message, "Local Gifting Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await LoadProfileImageAsync();
        if (_config.CheckUpdatesOnStartup)
            await CheckForStartupUpdateAsync();
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;
        _freeChestTimer.Stop();
        _bridgeTimer.Stop();
        await _twitch.DisposeAsync();
        await _giftQueue.DisposeAsync();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        LoginStatus.Text = "Opening Twitch authorization…";
        LoginError.Text = "";
        try
        {
            var user = await _twitch.LoginAsync(CancellationToken.None);
            if (ProfileButton.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault() is { } accountItem)
                accountItem.Header = user.DisplayName;
            LoginScreen.Visibility = Visibility.Collapsed;
            DashboardScreen.Visibility = Visibility.Visible;
            _twitch.StartEventSub();
            await RefreshRewardsAsync();
            await LoadProfileImageAsync();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            if (!_config.IntroductionDontAskAgain && !_config.IntroductionCompleted)
                ShowIntroductionPrompt();
        }
        catch (Exception ex)
        {
            LoginStatus.Text = "Login failed";
            LoginError.Text = ex.Message;
            LoginButton.IsEnabled = true;
        }
    }

    private void LoginHelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loginHelpIsOpen)
        {
            CloseLoginHelp();
            return;
        }

        var loginCardLeft = LoginCard.TranslatePoint(new Point(0, 0), LoginScreen).X;
        LoginHelpPanel.Visibility = Visibility.Visible;
        LoginHelpPanel.Opacity = 0;
        LoginContentGroup.UpdateLayout();
        var expandedCardLeft = LoginCard.TranslatePoint(new Point(0, 0), LoginScreen).X;
        _loginHelpShiftX = Math.Max(0, loginCardLeft - expandedCardLeft);
        LoginCard.RenderTransform = new TranslateTransform(_loginHelpShiftX, 0);
        LoginHelpPanel.RenderTransform = new TranslateTransform(54, 0);
        _loginHelpIsOpen = true;
        LoginHelpButton.ToolTip = "Close Twitch login setup guide";

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(260);
        ((TranslateTransform)LoginCard.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(_loginHelpShiftX, 0, duration) { EasingFunction = easing });
        ((TranslateTransform)LoginHelpPanel.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(54, 0, duration) { EasingFunction = easing });
        LoginHelpPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
    }

    private void CloseLoginHelp()
    {
        if (!_loginHelpIsOpen) return;
        _loginHelpIsOpen = false;
        LoginHelpButton.ToolTip = "Twitch login setup guide";
        var cardTransform = LoginCard.RenderTransform as TranslateTransform ?? new TranslateTransform();
        var panelTransform = LoginHelpPanel.RenderTransform as TranslateTransform ?? new TranslateTransform();
        LoginCard.RenderTransform = cardTransform;
        LoginHelpPanel.RenderTransform = panelTransform;
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(220);
        cardTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(cardTransform.X, _loginHelpShiftX, duration) { EasingFunction = easing });
        panelTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(panelTransform.X, 54, duration) { EasingFunction = easing });
        LoginHelpPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(LoginHelpPanel.Opacity, 0, duration) { EasingFunction = easing });

        var collapse = new DispatcherTimer { Interval = duration };
        collapse.Tick += (_, _) =>
        {
            collapse.Stop();
            LoginHelpPanel.Visibility = Visibility.Collapsed;
            LoginHelpPanel.BeginAnimation(OpacityProperty, null);
            LoginHelpPanel.Opacity = 1;
            LoginHelpPanel.RenderTransform = Transform.Identity;
            LoginCard.RenderTransform = Transform.Identity;
        };
        collapse.Start();
    }

    private void OpenTwitchConsole_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://dev.twitch.tv/console/apps") { UseShellExecute = true });
    }

    private void OpenLoginEnv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(AppPaths.EnvironmentFile))
                File.WriteAllText(AppPaths.EnvironmentFile, "TWITCH_CLIENT_ID=\r\nTWITCH_CLIENT_SECRET=\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var notepad = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
            notepad.ArgumentList.Add(AppPaths.EnvironmentFile);
            Process.Start(notepad);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the .env file:\n{ex.Message}\n\nFile path: {AppPaths.EnvironmentFile}", "Twitch login setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyLoginGuide_Click(object sender, RoutedEventArgs e)
    {
        const string guide = "STREAMLOOTS GIFTER — TWITCH LOGIN SETUP\r\n\r\n" +
            "1. Open https://dev.twitch.tv/console/apps and sign in. Enable two-factor authentication if Twitch asks.\r\n" +
            "2. Choose Applications > Register Your Application. Enter a name, such as Streamloots Gifter - YourChannel.\r\n" +
            "3. Add this exact OAuth Redirect URL: http://localhost:17563\r\n" +
            "4. Choose the closest application category, complete verification, and create the app. Open Manage, copy the Client ID, then create and copy a Client Secret.\r\n" +
            "5. Open the .env file beside StreamlootsGifter.exe and enter these lines without quotes or spaces around the equals signs:\r\n" +
            "TWITCH_CLIENT_ID=your_client_id\r\nTWITCH_CLIENT_SECRET=your_client_secret\r\n" +
            "6. Save .env, restart Streamloots Gifter, click Continue with Twitch, sign in as the streamer, and approve access.\r\n\r\n" +
            "Official guides:\r\nhttps://dev.twitch.tv/docs/authentication/register-app/\r\nhttps://dev.twitch.tv/docs/authentication/getting-tokens-oauth/\r\n\r\n" +
            "Keep the Client Secret private. Never share the filled .env file or include it in screenshots or public uploads.";
        try
        {
            Clipboard.SetText(guide);
            LoginStatus.Text = "Setup instructions copied to clipboard";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not copy the setup instructions:\n{ex.Message}", "Twitch login setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await _twitch.LogoutAsync();
        StopFreeChestHour("Free Chest Hour cancelled.");
        LoginStatus.Text = "Signed out";
        LoginError.Text = "";
        LoginButton.IsEnabled = true;
        DashboardScreen.Visibility = Visibility.Collapsed;
        LoginScreen.Visibility = Visibility.Visible;
    }

    private void ShowIntroductionPrompt()
    {
        var dialog = new Window
        {
            Title = "Streamloots Gifter introduction",
            Owner = this,
            Width = 500,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false
        };
        var root = new Border
        {
            Background = Brush(_themePanel),
            BorderBrush = Brush("#68C7FF"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20)
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "A quick tour?",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(_themeText),
            Margin = new Thickness(0, 0, 0, 7)
        };
        Grid.SetRow(title, 0);
        layout.Children.Add(title);
        var description = new TextBlock
        {
            Text = "Take a short introduction to the dashboard, widget layout, and settings. You can start it again any time from Settings.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 20,
            Foreground = Brush(_themeMuted),
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);

        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dontAskChecked = false;
        var checkMark = new TextBlock
        {
            Text = "✓",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var checkSurface = new Border
        {
            Width = 19,
            Height = 19,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brush(_themeLine),
            Background = Brush(_themeInput),
            Child = checkMark,
            VerticalAlignment = VerticalAlignment.Center
        };
        var checkContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        checkContent.Children.Add(checkSurface);
        checkContent.Children.Add(new TextBlock
        {
            Text = "Don't ask again",
            Foreground = Brush(_themeText),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
            FontSize = 13
        });
        var dontAsk = new Button
        {
            Content = checkContent,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Remember this choice"
        };
        dontAsk.Click += (_, _) =>
        {
            dontAskChecked = !dontAskChecked;
            checkMark.Visibility = dontAskChecked ? Visibility.Visible : Visibility.Collapsed;
            checkSurface.Background = Brush(dontAskChecked ? _themeAccent : _themeInput);
            checkSurface.BorderBrush = Brush(dontAskChecked ? _themeAccent : _themeLine);
        };
        footer.Children.Add(dontAsk);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var start = MakeButton("Yes, show me", () => dialog.Tag = true, _themeAccent);
        start.Width = 120;
        start.Height = 38;
        start.MinHeight = 0;
        start.Margin = new Thickness(8, 0, 0, 0);
        start.Click += (_, _) => dialog.Close();
        var skip = MakeButton("Skip", dialog.Close, _themeButton);
        skip.Width = 86;
        skip.Height = 38;
        skip.MinHeight = 0;
        skip.Click += (_, _) => dialog.Close();
        actions.Children.Add(skip);
        actions.Children.Add(start);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        root.Child = layout;
        dialog.Content = root;
        dialog.ShowDialog();

        if (dontAskChecked)
        {
            _config.IntroductionDontAskAgain = true;
            SaveConfig();
        }
        if (dialog.Tag is true)
            StartIntroduction();
    }

    private void StartIntroduction()
    {
        if (_isDashboardEditing) CancelDashboardEdit();
        ShowPage("dashboard");
        _introductionStep = 0;
        IntroductionOverlay.Visibility = Visibility.Visible;
        IntroductionOverlay.UpdateLayout();
        Dispatcher.BeginInvoke(UpdateIntroductionOverlay, DispatcherPriority.Loaded);
    }

    private void IntroductionNext_Click(object sender, RoutedEventArgs e)
    {
        if (_isWidgetGuide)
        {
            if (_widgetGuideStep >= _widgetGuideSteps.Count - 1)
            {
                FinishWidgetGuide();
                return;
            }
            _widgetGuideStep++;
            IntroductionOverlay.UpdateLayout();
            ScheduleWidgetGuideOverlay();
            return;
        }
        switch (_introductionStep)
        {
            case 0:
                _introductionStep = 1;
                break;
            case 1:
                BeginDashboardEdit();
                _introductionStep = 2;
                break;
            case 2:
                SyncDashboardSectionsFromLayout();
                SaveConfig();
                CloseDashboardEditor();
                ShowPage("settings");
                _introductionStep = 3;
                break;
            default:
                FinishIntroduction(completed: true);
                return;
        }
        IntroductionOverlay.UpdateLayout();
        UpdateIntroductionOverlay();
    }

    private void IntroductionBack_Click(object sender, RoutedEventArgs e)
    {
        if (_isWidgetGuide)
        {
            if (_widgetGuideStep > 0) _widgetGuideStep--;
            IntroductionOverlay.UpdateLayout();
            ScheduleWidgetGuideOverlay();
            return;
        }
        switch (_introductionStep)
        {
            case 1:
                _introductionStep = 0;
                break;
            case 2:
                CancelDashboardEdit();
                _introductionStep = 1;
                break;
            case 3:
                ShowPage("dashboard");
                BeginDashboardEdit();
                _introductionStep = 2;
                break;
            default:
                return;
        }
        IntroductionOverlay.UpdateLayout();
        UpdateIntroductionOverlay();
    }

    private void IntroductionSkip_Click(object sender, RoutedEventArgs e)
    {
        if (_isWidgetGuide)
        {
            FinishWidgetGuide();
            return;
        }
        if (_isDashboardEditing) CancelDashboardEdit();
        FinishIntroduction();
    }

    private void WidgetGuideButton_Click(string widgetKey)
    {
        if (_isDashboardEditing || !_cards.TryGetValue(widgetKey, out var card)) return;
        FrameworkElement? Target(FrameworkElement? preferred) => preferred ?? _cardBodies.GetValueOrDefault(widgetKey);
        _widgetGuideSteps.Clear();
        switch (widgetKey)
        {
            case "bridge":
                if (_config.CollapsedSections.Contains("bridge")) ToggleCollapse("bridge");
                _widgetGuideSteps.Add(new(Target(_bridgeStatus)!, "Extension connection", "This status shows whether the browser extension is connected to the app. Keep the Streamloots Activity Feed open while using card automation."));
                _widgetGuideSteps.Add(new(Target(_bridgeKeyBox)!, "Bridge key", "The key links this app to the browser extension. Copy it in the extension setup, or use Show to inspect it here."));
                _widgetGuideSteps.Add(new([Target(_bridgeCopyButton)!, Target(_bridgeRevealButton)!], "Copy and reveal", "Copy key copies the current key to your clipboard. Show reveals or hides the key in this panel."));
                _widgetGuideSteps.Add(new(Target(_bridgeGenerateButton)!, "Generate new key", "Generate a replacement only if needed. The extension will disconnect until it reconnects with the new key."));
                break;
            case "rewards":
                _widgetGuideSteps.Add(new([Target(_rewardRefreshButton)!, Target(_rewardSaveButton)!], "Refresh and save", "Refresh rewards reloads the redeems from Twitch. Save mappings stores your gift configuration."));
                _widgetGuideSteps.Add(new([Target(_showInactive)!, Target(_showHiddenRewards)!], "Display switches", "Inactive Rewards and Hidden Rewards control which redeems are listed. These switches do not enable gifting."));
                var rewardId = _rewardEnabledControls.Keys.FirstOrDefault();
                if (rewardId is not null && _rewardEnabledControls.TryGetValue(rewardId, out var rewardSwitchField))
                {
                    var rewardSwitch = rewardSwitchField is StackPanel switchStack && switchStack.Children.Count > 1
                        ? switchStack.Children[^1] as FrameworkElement ?? rewardSwitchField
                        : rewardSwitchField;
                    _widgetGuideSteps.Add(new(rewardSwitch, "Enable a redeem", "Each redeem has its own switch. Turn it on only when that reward should trigger a gift; rewards start disabled by default."));
                }
                else
                    _widgetGuideSteps.Add(new(Target(_rewardContainer)!, "Enable a redeem", "Each redeem has its own switch. Turn it on only when that reward should trigger a gift; rewards start disabled by default."));
                if (rewardId is not null && _rewardHideControls.TryGetValue(rewardId, out var hideButton))
                    _widgetGuideSteps.Add(new(hideButton, "Hide a redeem", "Hide removes this reward from the list without deleting its mapping. Turn on Hidden Rewards to show hidden rewards again."));
                var mappedFields = new List<FrameworkElement>();
                if (rewardId is not null && _rewardMappingFields.TryGetValue(rewardId, out var mappingField)) mappedFields.Add(mappingField);
                if (rewardId is not null && _rewardChestFields.TryGetValue(rewardId, out var chestField)) mappedFields.Add(chestField);
                if (rewardId is not null && _rewardCardFields.TryGetValue(rewardId, out var cardField)) mappedFields.Add(cardField);
                if (mappedFields.Count > 0)
                    _widgetGuideSteps.Add(new(mappedFields, "Collection and amounts", "For an enabled redeem, set its Streamloots collection and choose how many chests and cards to give. Save mappings applies your changes."));
                else
                    _widgetGuideSteps.Add(new(Target(_rewardContainer)!, "Collection and amounts", "For an enabled redeem, set its Streamloots collection and choose how many chests and cards to give. Save mappings applies your changes."));
                break;
            case "free_chest":
                if (_config.FreeChestRules.Count == 0)
                {
                    _widgetGuideAddedFreeChestExample = true;
                    RenderFreeChestRules(includeGuideExample: true);
                }
                _widgetGuideSteps.Add(new(Target(_freeChestStatus)!, "Timer status", "This line shows whether the timer is inactive, listening, or has ended."));
                _widgetGuideSteps.Add(new(Target(_freeChestTimerButtons)!, "Start and cancel", "Start timer opens the keyword window. Cancel timer ends it early; the countdown appears beside the buttons."));
                _widgetGuideSteps.Add(new(Target(_freeChestDurationRow)!, "Timer duration", "Set how many minutes the keyword window stays open before you start it."));
                _widgetGuideSteps.Add(new(Target(_keywordInput)!, "Keyword", "Enter the exact chat message viewers must send to claim from this rule."));
                _widgetGuideSteps.Add(new(Target(_freeChestCollectionField)!, "Collection", "Enter the exact Streamloots collection name that this keyword will gift from."));
                _widgetGuideSteps.Add(new([Target(_freeChestChestField)!, Target(_freeChestCardsField)!], "Chest and card amounts", "Choose how many chests and how many cards each matching viewer receives."));
                _widgetGuideSteps.Add(new(Target(_addFreeChestRuleButton)!, "Add keyword", "Select Add keyword to save this rule. You can edit it later without re-creating it."));
                _widgetGuideSteps.Add(new(Target(_freeChestRuleSwitch ?? _freeChestRulesContainer)!, "Enable a keyword", "Use this switch to temporarily enable or disable this keyword without deleting its settings."));
                _widgetGuideSteps.Add(new(Target(_freeChestRuleEditRemoveActions ?? _freeChestRulesContainer)!, "Edit or remove a keyword", "Edit loads this rule into the form so you can change it. Remove deletes it. Viewers can claim once per timer window."));
                break;
            case "free_chest_history":
                _widgetGuideSteps.Add(new(Target(_historyUsernameField)!, "Filter by username", "Enter a viewer name to show matching claims. Suggestions are offered for names already in the history."));
                _widgetGuideSteps.Add(new(Target(_historySourceField)!, "Filter by gifting type", "Choose which source to view, such as channel point redeem or Streamloots card."));
                _widgetGuideSteps.Add(new(Target(_historyStatusField)!, "Filter by status", "Choose Gifted, Needs attention, Queued, or another status. All shows every matching claim."));
                _widgetGuideSteps.Add(new(Target(_historyCleanupButton)!, "Clear old history", "The red trash button opens cleanup options. You can clear by username, type, status, age, or clear the entire history."));
                _widgetGuideSteps.Add(new(Target(_historyFirstRow ?? _historyList)!, "Claim results", "Each entry shows who triggered the gift, its recipient, collection and amount, status, and time. Scroll the list to review older claims."));
                break;
            case "activity":
                _widgetGuideSteps.Add(new(Target(_activityUserFilterField)!, "Username filter", "Type a viewer name to narrow the current session’s redeems. Suggestions appear when matching names are available."));
                _widgetGuideSteps.Add(new(Target(_activitySourceFilterField)!, "Gifting type filter", "Choose which source to show, such as channel point redeems or Streamloots card redeems."));
                _widgetGuideSteps.Add(new(Target(_activityStatusFilterButton)!, "Status filter", "Open Status to select which outcomes are visible. You can include several statuses or select all."));
                _widgetGuideSteps.Add(new(Target(_activityList)!, "Recent activity and retry", "New Twitch redeems appear here with their gifting status. Rows needing attention include a Retry button; successfully gifted rows can be repeated."));
                break;
            case "streamloots_cards":
                if (_config.StreamlootsCardRules.Count == 0)
                {
                    _widgetGuideAddedStreamlootsExample = true;
                    RenderStreamlootsCardRules(includeGuideExample: true);
                }
                _widgetGuideSteps.Add(new([Target(_streamlootsCardStatus)!, Target(_streamlootsFeedInstruction)!], "Activity feed connection", "Keep the Streamloots Activity Feed open in the browser. The extension watches it for card redeems and sends matching ones to this app."));
                _widgetGuideSteps.Add(new(Target(_streamlootsCardNameField)!, "Card name", "Enter the exact Streamloots card name. Matching is case-sensitive, so capitalization must be the same."));
                _widgetGuideSteps.Add(new(Target(_streamlootsCardActionField)!, "Gift recipient", "Choose whether the gift goes to the redeemer or to the username entered on the card."));
                _widgetGuideSteps.Add(new(Target(_streamlootsCardCollectionField)!, "Collection", "Enter the exact Streamloots collection name to gift from."));
                _widgetGuideSteps.Add(new([Target(_streamlootsCardChestField)!, Target(_streamlootsCardCardsField)!], "Chest and card amounts", "Choose the number of chests and cards to give for this card redeem."));
                _widgetGuideSteps.Add(new(Target(_addStreamlootsCardRuleButton)!, "Add or save a rule", "Select Add card to create the rule. When editing a rule, this button saves your changes; Cancel exits editing."));
                _widgetGuideSteps.Add(new(Target(_streamlootsCardRuleSwitch ?? _streamlootsCardRulesList)!, "Enable a card rule", "Use the switch to enable or disable this rule. Disabled rules are ignored by card automation."));
                _widgetGuideSteps.Add(new(Target(_streamlootsCardRuleEditRemoveActions ?? _streamlootsCardRulesList)!, "Edit or remove a rule", "Edit loads a rule into the form, where Add card changes to Save changes and Cancel. Remove permanently deletes it. Cards without a matching enabled rule are ignored."));
                break;
            default:
                return;
        }

        _isWidgetGuide = true;
        _widgetGuideStep = 0;
        _activeWidgetGuideKey = widgetKey;
        _introductionStep = -1;
        IntroductionCallout.Visibility = Visibility.Collapsed;
        WidgetGuidePreviewFrame.Visibility = Visibility.Collapsed;
        WidgetGuidePreviewDimmer.Visibility = Visibility.Collapsed;
        WidgetGuidePreviewSpotlight.Visibility = Visibility.Collapsed;
        // The overlay is collapsed on the dashboard, so its ActualWidth/Height are
        // zero until it participates in layout. Make it visible before sizing the
        // detached widget preview; otherwise the widget falls back to its MinWidth.
        IntroductionOverlay.Visibility = Visibility.Visible;
        IntroductionOverlay.UpdateLayout();
        // Give the guide a real, readable working surface even when its dashboard
        // tile was narrow or short. Reflow the live widget instead of scaling it down.
        var overlayWidth = Math.Max(1, IntroductionOverlay.ActualWidth);
        var overlayHeight = Math.Max(1, IntroductionOverlay.ActualHeight);
        var previewWidth = Math.Max(card.MinWidth, Math.Min(1000, overlayWidth * 0.78));
        var previewHeight = Math.Max(card.MinHeight, Math.Min(520, overlayHeight * 0.60));
        DetachDashboardElement(card);
        card.Width = previewWidth;
        card.Height = previewHeight;
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        card.VerticalAlignment = VerticalAlignment.Stretch;
        card.IsHitTestVisible = false;
        WidgetGuidePreviewHost.Content = card;
        WidgetGuidePreviewFrame.Width = previewWidth + 4;
        WidgetGuidePreviewFrame.Height = previewHeight + 4;
        WidgetGuidePreviewFrame.Visibility = Visibility.Visible;
        IntroductionCallout.Height = 220;
        IntroductionOverlay.Visibility = Visibility.Visible;
        IntroductionOverlay.UpdateLayout();
        ScheduleWidgetGuideOverlay();
    }

    private void FinishWidgetGuide()
    {
        if (_activeWidgetGuideKey is { } widgetKey && _cards.TryGetValue(widgetKey, out var card))
        {
            WidgetGuidePreviewHost.Content = null;
            card.IsHitTestVisible = true;
            card.Width = double.NaN;
            card.Height = double.NaN;
            BuildDashboardGrid("dashboard");
        }
        if (_widgetGuideAddedFreeChestExample)
        {
            RenderFreeChestRules();
            _widgetGuideAddedFreeChestExample = false;
        }
        if (_widgetGuideAddedStreamlootsExample)
        {
            RenderStreamlootsCardRules();
            _widgetGuideAddedStreamlootsExample = false;
        }
        _isWidgetGuide = false;
        _widgetGuideStep = -1;
        _activeWidgetGuideKey = null;
        _widgetGuideSteps.Clear();
        WidgetGuidePreviewFrame.Visibility = Visibility.Collapsed;
        WidgetGuidePreviewDimmer.Visibility = Visibility.Collapsed;
        WidgetGuidePreviewSpotlight.Visibility = Visibility.Collapsed;
        IntroductionSpotlight.Visibility = Visibility.Visible;
        IntroductionCallout.Height = double.NaN;
        IntroductionOverlay.Visibility = Visibility.Collapsed;
    }

    private void ScheduleWidgetGuideOverlay()
    {
        if (!_isWidgetGuide || _widgetGuideStep < 0 || _widgetGuideStep >= _widgetGuideSteps.Count || IntroductionOverlay.Visibility != Visibility.Visible) return;
        var step = _widgetGuideSteps[_widgetGuideStep];
        foreach (var target in step.Targets)
            if (target.IsVisible) target.BringIntoView();
        Dispatcher.BeginInvoke(RenderWidgetGuideOverlay, DispatcherPriority.ContextIdle);
    }

    private void RenderWidgetGuideOverlay()
    {
        if (!_isWidgetGuide || _widgetGuideStep < 0 || _widgetGuideStep >= _widgetGuideSteps.Count || IntroductionOverlay.Visibility != Visibility.Visible) return;
        var step = _widgetGuideSteps[_widgetGuideStep];
        if (_activeWidgetGuideKey is null || !_cards.TryGetValue(_activeWidgetGuideKey, out var card) || card.ActualWidth < 2 || card.ActualHeight < 2) return;
        card.UpdateLayout();
        var width = Math.Max(1, IntroductionOverlay.ActualWidth);
        var height = Math.Max(1, IntroductionOverlay.ActualHeight);
        var responsiveWidth = Math.Max(card.MinWidth, Math.Min(1000, width * 0.78));
        var responsiveHeight = Math.Max(card.MinHeight, Math.Min(520, height * 0.60));
        if (Math.Abs(card.Width - responsiveWidth) > 1 || Math.Abs(card.Height - responsiveHeight) > 1)
        {
            card.Width = responsiveWidth;
            card.Height = responsiveHeight;
            card.UpdateLayout();
        }
        var viewport = new Rect(0, 0, width, height);
        var targetBoundsList = new List<Rect>();
        foreach (var target in step.Targets)
        {
            if (target.RenderSize.Width < 1 || target.RenderSize.Height < 1) continue;
            var targetBounds = target.TransformToVisual(card).TransformBounds(new Rect(new Point(0, 0), target.RenderSize));
            targetBounds.Intersect(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
            if (!targetBounds.IsEmpty) targetBoundsList.Add(targetBounds);
        }
        if (targetBoundsList.Count == 0) targetBoundsList.Add(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
        var targetUnion = targetBoundsList.Aggregate(Rect.Union);
        var previewWidth = card.ActualWidth;
        var previewHeight = card.ActualHeight;
        const double previewBorder = 2;
        WidgetGuidePreviewFrame.Width = previewWidth + previewBorder * 2;
        WidgetGuidePreviewFrame.Height = previewHeight + previewBorder * 2;
        var frameLeft = (width - WidgetGuidePreviewFrame.Width) / 2;
        Canvas.SetLeft(WidgetGuidePreviewFrame, frameLeft);
        var previewTop = Math.Max(18, (height - WidgetGuidePreviewFrame.Height - 220 - 24) / 2);
        Canvas.SetTop(WidgetGuidePreviewFrame, previewTop);
        WidgetGuidePreviewFrame.Visibility = Visibility.Visible;
        IntroductionSpotlight.Visibility = Visibility.Collapsed;
        WidgetGuidePreviewFrame.UpdateLayout();
        var contentOrigin = new Point(frameLeft + previewBorder, previewTop + previewBorder);

        var scaled = new Rect(
            contentOrigin.X + targetUnion.X,
            contentOrigin.Y + targetUnion.Y,
            targetUnion.Width,
            targetUnion.Height);
        scaled.Inflate(5, 5);
        var previewRect = new Rect(frameLeft, previewTop, WidgetGuidePreviewFrame.Width, WidgetGuidePreviewFrame.Height);
        scaled.Intersect(previewRect);
        var localDim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        localDim.Children.Add(new RectangleGeometry(previewRect));
        foreach (var targetBounds in targetBoundsList)
        {
            var hole = new Rect(
                contentOrigin.X + targetBounds.X,
                contentOrigin.Y + targetBounds.Y,
                targetBounds.Width,
                targetBounds.Height);
            hole.Inflate(5, 5);
            hole.Intersect(previewRect);
            localDim.Children.Add(new RectangleGeometry(hole, 8, 8));
        }
        WidgetGuidePreviewDimmer.Data = localDim;
        WidgetGuidePreviewDimmer.Visibility = Visibility.Visible;
        Canvas.SetLeft(WidgetGuidePreviewDimmer, 0);
        Canvas.SetTop(WidgetGuidePreviewDimmer, 0);
        WidgetGuidePreviewDimmer.Width = width;
        WidgetGuidePreviewDimmer.Height = height;
        Canvas.SetLeft(WidgetGuidePreviewSpotlight, scaled.Left);
        Canvas.SetTop(WidgetGuidePreviewSpotlight, scaled.Top);
        WidgetGuidePreviewSpotlight.Width = Math.Max(1, scaled.Width);
        WidgetGuidePreviewSpotlight.Height = Math.Max(1, scaled.Height);
        WidgetGuidePreviewSpotlight.Visibility = Visibility.Visible;

        var dimGeometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        dimGeometry.Children.Add(new RectangleGeometry(viewport));
        IntroductionDimmer.Data = dimGeometry;
        Canvas.SetLeft(IntroductionDimmer, 0);
        Canvas.SetTop(IntroductionDimmer, 0);
        IntroductionDimmer.Width = width;
        IntroductionDimmer.Height = height;
        IntroductionSettingsButtonSpotlight.Visibility = Visibility.Collapsed;
        IntroductionStepTitle.Text = step.Title;
        IntroductionStepCounter.Text = $"STEP {_widgetGuideStep + 1} OF {_widgetGuideSteps.Count}";
        IntroductionStepBody.Text = step.Body;
        IntroductionBackButton.IsEnabled = _widgetGuideStep > 0;
        IntroductionNextButton.Content = _widgetGuideStep == _widgetGuideSteps.Count - 1 ? "Done" : "Next";
        IntroductionCallout.Visibility = Visibility.Visible;
        IntroductionCallout.Measure(new Size(width - 32, height - 32));
        var calloutWidth = IntroductionCallout.DesiredSize.Width;
        var calloutHeight = IntroductionCallout.DesiredSize.Height;
        var calloutLeft = (width - calloutWidth) / 2;
        var calloutTop = previewTop + WidgetGuidePreviewFrame.Height + 12;
        if (calloutTop + calloutHeight > height - 12)
            calloutTop = Math.Max(12, previewTop + WidgetGuidePreviewFrame.Height - calloutHeight - 12);
        Canvas.SetLeft(IntroductionCallout, Math.Clamp(calloutLeft, 16, Math.Max(16, width - calloutWidth - 16)));
        Canvas.SetTop(IntroductionCallout, Math.Clamp(calloutTop, 12, Math.Max(12, height - calloutHeight - 12)));
    }

    private void FinishIntroduction(bool completed = false)
    {
        IntroductionOverlay.Visibility = Visibility.Collapsed;
        _introductionStep = -1;
        if (completed)
        {
            _config.IntroductionCompleted = true;
            SaveConfig();
        }
    }

    private void IntroductionOverlay_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isWidgetGuide)
        {
            Dispatcher.BeginInvoke(ScheduleWidgetGuideOverlay, DispatcherPriority.Loaded);
            return;
        }
        if (_introductionStep >= 0)
            Dispatcher.BeginInvoke(UpdateIntroductionOverlay, DispatcherPriority.Loaded);
    }

    private void UpdateIntroductionOverlay()
    {
        if (_introductionStep < 0 || IntroductionOverlay.Visibility != Visibility.Visible) return;
        FrameworkElement target = _introductionStep switch
        {
            1 => AddWidgetButton,
            2 => WidgetEditorPanel,
            3 => _settingsTourTarget ?? WorkspaceGrid,
            _ => WorkspaceGrid
        };
        var width = Math.Max(1, IntroductionOverlay.ActualWidth);
        var height = Math.Max(1, IntroductionOverlay.ActualHeight);
        var viewport = new Rect(0, 0, width, height);
        var bounds = target.TransformToVisual(IntroductionOverlay).TransformBounds(new Rect(new Point(0, 0), target.RenderSize));
        bounds.Intersect(viewport);
        bounds.Inflate(7, 7);
        bounds.Intersect(viewport);
        var cutout = new RectangleGeometry(new Rect(0, 0, width, height));
        var spotlightHole = new RectangleGeometry(bounds, 10, 10);
        var dimGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dimGeometry.Children.Add(cutout);
        dimGeometry.Children.Add(spotlightHole);
        Rect settingsButtonBounds = Rect.Empty;
        if (_introductionStep == 3)
        {
            settingsButtonBounds = SettingsCogButton.TransformToVisual(IntroductionOverlay)
                .TransformBounds(new Rect(new Point(0, 0), SettingsCogButton.RenderSize));
            settingsButtonBounds.Inflate(6, 6);
            settingsButtonBounds.Intersect(viewport);
            dimGeometry.Children.Add(new RectangleGeometry(settingsButtonBounds, 9, 9));
            Canvas.SetLeft(IntroductionSettingsButtonSpotlight, settingsButtonBounds.Left);
            Canvas.SetTop(IntroductionSettingsButtonSpotlight, settingsButtonBounds.Top);
            IntroductionSettingsButtonSpotlight.Width = Math.Max(1, settingsButtonBounds.Width);
            IntroductionSettingsButtonSpotlight.Height = Math.Max(1, settingsButtonBounds.Height);
            IntroductionSettingsButtonSpotlight.Visibility = Visibility.Visible;
        }
        else
        {
            IntroductionSettingsButtonSpotlight.Visibility = Visibility.Collapsed;
        }
        IntroductionDimmer.Data = dimGeometry;
        Canvas.SetLeft(IntroductionDimmer, 0);
        Canvas.SetTop(IntroductionDimmer, 0);
        IntroductionDimmer.Width = width;
        IntroductionDimmer.Height = height;
        Canvas.SetLeft(IntroductionSpotlight, bounds.Left);
        Canvas.SetTop(IntroductionSpotlight, bounds.Top);
        IntroductionSpotlight.Width = Math.Max(1, bounds.Width);
        IntroductionSpotlight.Height = Math.Max(1, bounds.Height);

        var titles = new[] { "Your dashboard", "Edit layout", "Add and arrange widgets", "Settings" };
        var descriptions = new[]
        {
            "This is your dashboard. Here you can move and resize widgets to keep the information you need close at hand.",
            "Use Edit layout to choose which widgets appear on your dashboard and arrange their positions.",
            "Add widgets from the available list, remove ones you don't need, then save your layout. You can also drag widgets onto panel edges to split the space.",
            "Settings contains viewer aliases and other app options, including how redeems identify viewers and where to manage updates and the browser extension."
        };
        IntroductionStepTitle.Text = titles[_introductionStep];
        IntroductionStepCounter.Text = $"STEP {_introductionStep + 1} OF 4";
        IntroductionStepBody.Text = descriptions[_introductionStep];
        IntroductionBackButton.IsEnabled = _introductionStep > 0;
        IntroductionNextButton.Content = _introductionStep == 3 ? "Finish" : "Next";
        IntroductionCallout.Measure(new Size(width - 32, height - 32));
        var calloutWidth = IntroductionCallout.DesiredSize.Width;
        var calloutHeight = IntroductionCallout.DesiredSize.Height;
        double left;
        double top;
        if (_introductionStep == 2)
        {
            left = Math.Max(20, bounds.Left - calloutWidth - 18);
            top = Math.Max(20, Math.Min(height - calloutHeight - 24, bounds.Bottom - calloutHeight - 20));
        }
        else if (_introductionStep == 1)
        {
            left = Math.Max(20, bounds.Left - calloutWidth - 18);
            top = Math.Min(height - calloutHeight - 20, bounds.Bottom + 18);
        }
        else if (_introductionStep == 3)
        {
            left = (width - calloutWidth) / 2;
            top = height - calloutHeight - 24;
        }
        else
        {
            left = Math.Max(20, width - calloutWidth - 24);
            top = Math.Max(20, height - calloutHeight - 24);
        }
        Canvas.SetLeft(IntroductionCallout, Math.Clamp(left, 16, Math.Max(16, width - calloutWidth - 16)));
        Canvas.SetTop(IntroductionCallout, Math.Clamp(top, 16, Math.Max(16, height - calloutHeight - 16)));
    }

    private async void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ProfileButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
        await Task.CompletedTask;
    }

    private void ProfileButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ProfileButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ProfileButton;
            menu.Placement = PlacementMode.Bottom;
        }
    }

    private void Profile_MouseEnter(object sender, MouseEventArgs e) => AnimateProfile(1.1);
    private void Profile_MouseLeave(object sender, MouseEventArgs e) => AnimateProfile(1.0);

    private void AnimateProfile(double scale)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation(scale, TimeSpan.FromMilliseconds(110));
        ProfileScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        ProfileScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private async Task LoadProfileImageAsync()
    {
        if (string.IsNullOrWhiteSpace(_twitch.User?.ProfileImageUrl)) return;
        try
        {
            using var client = new HttpClient();
            var data = await client.GetByteArrayAsync(_twitch.User.ProfileImageUrl);
            var image = new BitmapImage();
            using var stream = new System.IO.MemoryStream(data);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.DecodePixelWidth = 88;
            image.EndInit();
            image.Freeze();
            _profileImage = image;
            ProfileEllipse.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException) { }
    }

    private void BuildNavigation()
    {
        WorkspaceGrid.AllowDrop = true;
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(_activePage == "settings" ? "dashboard" : "settings");
    private void SettingsBack_Click(object sender, RoutedEventArgs e) => ShowPage("dashboard");

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _settingsSearchQuery = SettingsSearchBox.Text;
        SettingsSearchPlaceholder.Visibility = string.IsNullOrEmpty(_settingsSearchQuery) ? Visibility.Visible : Visibility.Collapsed;
        ApplySettingsSectionFilter();
    }

    private FrameworkElement CreateSettingsSection(string key, string title, string searchTerms, FrameworkElement body)
    {
        var collapsed = _config.SettingsCollapsedSections.Contains(key, StringComparer.Ordinal);
        var headerGrid = new Grid();
        headerGrid.Background = Brush("#00000000");
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        var leadLine = new Border
        {
            Height = 1,
            Background = Brush("#26324A"),
            Width = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerGrid.Children.Add(leadLine);
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#F4F6FF"),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleText.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(titleText, 1);
        headerGrid.Children.Add(titleText);
        var rule = new Border
        {
            Height = 1,
            Background = Brush("#26324A"),
            Margin = new Thickness(18, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        Grid.SetColumn(rule, 2);
        headerGrid.Children.Add(rule);
        var chevron = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(collapsed ? "M 2,6 L 10,14 L 18,6" : "M 2,14 L 10,6 L 18,14"),
            Width = 20,
            Height = 20,
            Stretch = Stretch.None,
            Stroke = Brush("#AAB5C9"),
            StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        Grid.SetColumn(chevron, 3);
        headerGrid.Children.Add(chevron);
        var tailLine = new Border
        {
            Height = 1,
            Background = Brush("#26324A"),
            Width = 16,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tailLine, 4);
        headerGrid.Children.Add(tailLine);
        var header = new Border
        {
            Child = headerGrid,
            Background = Brush("#00000000"),
            Padding = new Thickness(8, 7, 0, 7),
            Margin = new Thickness(0, 2, 38, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = collapsed ? $"Expand {title}" : $"Collapse {title}"
        };
        var bodyContainer = new Border
        {
            Child = body,
            Margin = new Thickness(10, 4, 48, 14),
            Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible
        };
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        section.Children.Add(header);
        section.Children.Add(bodyContainer);
        var entry = new SettingsSectionEntry(key, title, searchTerms, section, bodyContainer, chevron);
        _settingsSections.Add(entry);
        header.MouseEnter += (_, _) =>
        {
            leadLine.Height = 2;
            leadLine.Background = Brush("#5D6EFF");
            rule.Height = 2;
            rule.Background = Brush("#5D6EFF");
            tailLine.Height = 2;
            tailLine.Background = Brush("#5D6EFF");
        };
        header.MouseLeave += (_, _) =>
        {
            leadLine.Height = 1;
            leadLine.Background = Brush("#26324A");
            rule.Height = 1;
            rule.Background = Brush("#26324A");
            tailLine.Height = 1;
            tailLine.Background = Brush("#26324A");
        };
        header.MouseLeftButtonUp += (_, args) =>
        {
            ToggleSettingsSection(entry);
            args.Handled = true;
        };
        header.KeyDown += (_, args) =>
        {
            if (args.Key is Key.Enter or Key.Space)
            {
                ToggleSettingsSection(entry);
                args.Handled = true;
            }
        };
        return section;
    }

    private void ToggleSettingsSection(SettingsSectionEntry section)
    {
        var collapse = section.Body.Visibility == Visibility.Visible;
        section.Body.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        section.Chevron.Data = Geometry.Parse(collapse ? "M 2,6 L 10,14 L 18,6" : "M 2,14 L 10,6 L 18,14");
        _config.SettingsCollapsedSections.Remove(section.Key);
        if (collapse) _config.SettingsCollapsedSections.Add(section.Key);
        SaveConfig();
    }

    private void ApplySettingsSectionFilter()
    {
        if (_settingsSections.Count == 0) return;
        var query = _settingsSearchQuery.Trim();
        var matches = 0;
        foreach (var section in _settingsSections)
        {
            var match = query.Length == 0 || $"{section.Title} {section.SearchTerms}".Contains(query, StringComparison.OrdinalIgnoreCase);
            section.Container.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            if (!match) continue;
            matches++;
            var collapsed = query.Length == 0 && _config.SettingsCollapsedSections.Contains(section.Key, StringComparer.Ordinal);
            section.Body.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            section.Chevron.Data = Geometry.Parse(collapsed ? "M 2,6 L 10,14 L 18,6" : "M 2,14 L 10,6 L 18,14");
        }
        if (_settingsNoResults is not null)
            _settingsNoResults.Visibility = query.Length > 0 && matches == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddWidgetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDashboardEditing) CancelDashboardEdit();
        else BeginDashboardEdit();
    }

    private void BeginDashboardEdit()
    {
        _layoutBeforeEdit = CloneDashboardLayout(_config.DashboardLayout);
        _isDashboardEditing = true;
        WidgetEditorColumn.Width = new GridLength(320);
        WidgetEditorPanel.Visibility = Visibility.Visible;
        AddWidgetButton.Content = "Editing layout";
        RefreshWidgetEditorLists();
        BuildDashboardGrid("dashboard");
    }

    private void SaveDashboardEdit_Click(object sender, RoutedEventArgs e)
    {
        SyncDashboardSectionsFromLayout();
        SaveConfig();
        CloseDashboardEditor();
    }

    private void CancelDashboardEdit_Click(object sender, RoutedEventArgs e) => CancelDashboardEdit();

    private void CancelDashboardEdit()
    {
        _config.DashboardLayout = CloneDashboardLayout(_layoutBeforeEdit);
        SyncDashboardSectionsFromLayout();
        CloseDashboardEditor();
    }

    private void CloseDashboardEditor()
    {
        _isDashboardEditing = false;
        _layoutBeforeEdit = null;
        WidgetEditorPanel.Visibility = Visibility.Collapsed;
        WidgetEditorColumn.Width = new GridLength(0);
        AddWidgetButton.Content = "＋  Edit layout";
        HideWidgetDropHints();
        BuildDashboardGrid("dashboard");
    }

    private void RefreshWidgetEditorLists()
    {
        if (_availableWidgetsPanel is null || _addedWidgetsPanel is null) return;
        _availableWidgetsPanel.Children.Clear();
        _addedWidgetsPanel.Children.Clear();
        var titles = WidgetTitles();
        var visibleKeys = FlattenDashboardLayout(_config.DashboardLayout);
        var available = ConfigStore.Sections.Where(key => !visibleKeys.Contains(key, StringComparer.Ordinal)).ToArray();
        if (available.Length == 0)
            _availableWidgetsPanel.Children.Add(Label("All widgets are on the dashboard.", false, "#8491AA"));
        foreach (var key in available)
            _availableWidgetsPanel.Children.Add(CreateWidgetPaletteItem(key, titles[key]));
        if (visibleKeys.Count == 0)
            _addedWidgetsPanel.Children.Add(Label("No widgets added yet.", false, "#8491AA"));
        foreach (var key in visibleKeys)
        {
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2) };
            var remove = MakeButton("×", () => RemoveFromDashboard(key), "#492B36");
            remove.Width = 30;
            remove.Height = 28;
            remove.MinHeight = 0;
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(Label(titles.GetValueOrDefault(key, key), true));
            _addedWidgetsPanel.Children.Add(new Border
            {
                Background = Brush("#10182A"), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 4, 6, 4),
                Child = row, Margin = new Thickness(0, 2, 0, 2)
            });
        }
    }

    private Border CreateWidgetPaletteItem(string key, string title)
    {
        var row = new Border
        {
            Background = Brush("#202B40"), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 8, 9, 8),
            Margin = new Thickness(0, 3, 0, 3), Cursor = Cursors.SizeAll, Tag = key,
            Child = new TextBlock { Text = $"⠿   {title}", Foreground = Brush("#F4F6FF"), FontWeight = FontWeights.SemiBold }
        };
        Point? dragStart = null;
        row.PreviewMouseLeftButtonDown += (_, args) => dragStart = args.GetPosition(row);
        row.PreviewMouseLeftButtonUp += (_, _) => dragStart = null;
        row.PreviewMouseMove += (_, args) =>
        {
            if (!_isDashboardEditing || args.LeftButton != MouseButtonState.Pressed || dragStart is not Point start) return;
            var current = args.GetPosition(row);
            if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragStart = null;
            DragDrop.DoDragDrop(row, new DataObject(DataFormats.StringFormat, key), DragDropEffects.Copy);
        };
        return row;
    }

    private static Dictionary<string, string> WidgetTitles() => new(StringComparer.Ordinal)
    {
        ["bridge"] = "Streamloots Bridge",
        ["rewards"] = "Channel Point Redeems",
        ["free_chest"] = "Free Chest Hour",
        ["free_chest_history"] = "Claim History",
        ["activity"] = "Redemption Activity",
        ["streamloots_cards"] = "Streamloots Cards"
    };

    private static DashboardLayoutNode? BuildInitialDashboardLayout(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return null;
        if (keys.Count == 1) return new DashboardLayoutNode { WidgetKey = keys[0] };
        var left = keys.Where((_, index) => index % 2 == 0).ToArray();
        var right = keys.Where((_, index) => index % 2 == 1).ToArray();
        return new DashboardLayoutNode
        {
            SplitAxis = "columns", Ratio = 0.5,
            First = BuildVerticalStack(left), Second = BuildVerticalStack(right)
        };
    }

    private static DashboardLayoutNode? BuildVerticalStack(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return null;
        if (keys.Count == 1) return new DashboardLayoutNode { WidgetKey = keys[0] };
        var splitAt = (keys.Count + 1) / 2;
        return new DashboardLayoutNode
        {
            SplitAxis = "rows", Ratio = (double)splitAt / keys.Count,
            First = BuildVerticalStack(keys.Take(splitAt).ToArray()),
            Second = BuildVerticalStack(keys.Skip(splitAt).ToArray())
        };
    }

    private static DashboardLayoutNode? NormalizeDashboardLayout(DashboardLayoutNode? node, HashSet<string> seen)
    {
        if (node is null) return null;
        if (!string.IsNullOrWhiteSpace(node.WidgetKey))
        {
            var key = node.WidgetKey.Trim();
            return ConfigStore.Sections.Contains(key, StringComparer.Ordinal) && seen.Add(key)
                ? new DashboardLayoutNode { WidgetKey = key }
                : null;
        }
        var first = NormalizeDashboardLayout(node.First, seen);
        var second = NormalizeDashboardLayout(node.Second, seen);
        if (first is null) return second;
        if (second is null) return first;
        return new DashboardLayoutNode
        {
            SplitAxis = node.SplitAxis is "rows" or "columns" ? node.SplitAxis : "columns",
            Ratio = double.IsFinite(node.Ratio) ? Math.Clamp(node.Ratio, 0.12, 0.88) : 0.5,
            First = first,
            Second = second
        };
    }

    private static DashboardLayoutNode? CloneDashboardLayout(DashboardLayoutNode? node) => node is null ? null : new DashboardLayoutNode
    {
        WidgetKey = node.WidgetKey,
        SplitAxis = node.SplitAxis,
        Ratio = node.Ratio,
        First = CloneDashboardLayout(node.First),
        Second = CloneDashboardLayout(node.Second)
    };

    private static List<string> FlattenDashboardLayout(DashboardLayoutNode? node)
    {
        if (node is null) return [];
        if (node.IsLeaf && node.WidgetKey is { } key) return [key];
        return FlattenDashboardLayout(node.First).Concat(FlattenDashboardLayout(node.Second)).ToList();
    }

    private void SyncDashboardSectionsFromLayout()
    {
        var keys = FlattenDashboardLayout(_config.DashboardLayout).Distinct(StringComparer.Ordinal).ToArray();
        _config.DashboardSections = keys.ToList();
        _config.SectionOrder = keys.ToList();
    }

    private static DashboardLayoutNode? RemoveWidgetFromLayout(DashboardLayoutNode? node, string key)
    {
        if (node is null) return null;
        if (node.IsLeaf) return node.WidgetKey == key ? null : node;
        var first = RemoveWidgetFromLayout(node.First, key);
        var second = RemoveWidgetFromLayout(node.Second, key);
        if (first is null) return second;
        if (second is null) return first;
        return new DashboardLayoutNode { SplitAxis = node.SplitAxis, Ratio = node.Ratio, First = first, Second = second };
    }

    private static DashboardLayoutNode? InsertWidgetBeside(DashboardLayoutNode? node, string targetKey, string incomingKey, string axis, bool before)
    {
        if (node is null) return null;
        if (node.IsLeaf)
        {
            if (node.WidgetKey != targetKey) return node;
            var incoming = new DashboardLayoutNode { WidgetKey = incomingKey };
            return new DashboardLayoutNode
            {
                SplitAxis = axis, Ratio = 0.5,
                First = before ? incoming : CloneDashboardLayout(node),
                Second = before ? CloneDashboardLayout(node) : incoming
            };
        }
        var first = InsertWidgetBeside(node.First, targetKey, incomingKey, axis, before);
        if (!ReferenceEquals(first, node.First))
            return new DashboardLayoutNode { SplitAxis = node.SplitAxis, Ratio = node.Ratio, First = first, Second = node.Second };
        var second = InsertWidgetBeside(node.Second, targetKey, incomingKey, axis, before);
        return ReferenceEquals(second, node.Second)
            ? node
            : new DashboardLayoutNode { SplitAxis = node.SplitAxis, Ratio = node.Ratio, First = node.First, Second = second };
    }

    private static DashboardDropSide GetDashboardDropSide(Border target, Point point)
    {
        var width = Math.Max(1, target.ActualWidth);
        var height = Math.Max(1, target.ActualHeight);
        var x = Math.Clamp(point.X / width, 0, 1);
        var y = Math.Clamp(point.Y / height, 0, 1);
        var distances = new[]
        {
            (Side: DashboardDropSide.Left, Distance: x), (Side: DashboardDropSide.Right, Distance: 1 - x),
            (Side: DashboardDropSide.Top, Distance: y), (Side: DashboardDropSide.Bottom, Distance: 1 - y)
        };
        return distances.MinBy(item => item.Distance).Side;
    }

    private UIElement BuildSettingsPage()
    {
        _settingsSections.Clear();
        // Keep the settings accordion width stable when wide sections collapse.
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 850, MaxWidth = 900 };
        _settingsTourTarget = content;
        _settingsNoResults = Label("No settings match your search.", false, "#8D9AB3");
        _settingsNoResults.Margin = new Thickness(8, 16, 0, 0);
        _settingsNoResults.Visibility = Visibility.Collapsed;

        var settingCard = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 6, 4, 8),
            MaxWidth = 850,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var settingContent = new StackPanel();
        var requireUsername = CreateSwitch("Require Streamloots Username", _config.RequireStreamlootsUsername,
            value => { _config.RequireStreamlootsUsername = value; SaveConfig(); });
        requireUsername.Margin = new Thickness(0, 0, 0, 6);
        settingContent.Children.Add(requireUsername);
        settingContent.Children.Add(Label("When enabled, viewers enter their Streamloots username. When disabled, gifts use their Twitch login name instead.", false, "#8D9AB3"));
        settingCard.Child = settingContent;
        content.Children.Add(CreateSettingsSection("twitch_redeems", "Twitch Redeems", "channel point rewards username recipient Streamloots login", settingCard));

        var introductionCard = new Border
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 6, 4, 8), MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Left
        };
        var introductionContent = new StackPanel();
        var startIntroduction = MakeButton("Repeat introduction", ShowIntroductionPrompt, "#5D6EFF");
        startIntroduction.Width = 190;
        startIntroduction.Height = 36;
        startIntroduction.MinHeight = 0;
        startIntroduction.FontSize = 14;
        startIntroduction.FontWeight = FontWeights.SemiBold;
        startIntroduction.HorizontalAlignment = HorizontalAlignment.Left;
        introductionContent.Children.Add(startIntroduction);
        var introDescription = Label("Walk through the dashboard and the main controls.", false, "#AAB5C9");
        introDescription.Margin = new Thickness(0, 8, 0, 0);
        introductionContent.Children.Add(introDescription);
        introductionCard.Child = introductionContent;
        content.Children.Add(CreateSettingsSection("introduction", "Introduction", "repeat tour dashboard walkthrough help", introductionCard));

        var aliasesCard = new Border
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 6, 4, 8), MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Left
        };
        var aliasesContent = new StackPanel();
        var aliasesTitle = new StackPanel();
        aliasesTitle.Children.Add(Label("Map nicknames like “libra” to the viewer’s exact Streamloots username. Separate multiple aliases with commas.", false, "#8D9AB3"));
        aliasesContent.Children.Add(aliasesTitle);

        var aliasToolbar = new Grid { Margin = new Thickness(0, 10, 0, 8) };
        aliasToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        aliasToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        aliasToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var addAlias = MakeButton("Add entry", AddViewerAliasEntry, "#5D6EFF");
        addAlias.Width = 100;
        addAlias.Height = 32;
        addAlias.MinHeight = 0;
        aliasToolbar.Children.Add(addAlias);
        var searchHost = new Grid { Height = 32, Margin = new Thickness(10, 0, 10, 0) };
        var aliasSearchBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Search by Streamloots username or alias"
        };
        _viewerAliasSearchBox = aliasSearchBox;
        _viewerAliasSearchPlaceholder = new TextBlock
        {
            Text = "Search username or alias",
            Foreground = Brush("#8491AA"),
            Margin = new Thickness(10, 0, 28, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        aliasSearchBox.TextChanged += (_, _) =>
        {
            if (_viewerAliasSearchPlaceholder is not null)
                _viewerAliasSearchPlaceholder.Visibility = string.IsNullOrEmpty(aliasSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            RenderViewerAliasEntries();
        };
        searchHost.Children.Add(_viewerAliasSearchBox);
        searchHost.Children.Add(_viewerAliasSearchPlaceholder);
        Grid.SetColumn(searchHost, 1);
        aliasToolbar.Children.Add(searchHost);
        _viewerAliasesToggle = MakeButton(_config.ViewerAliasesCollapsed ? "+" : "−", ToggleViewerAliases, "#25324A");
        _viewerAliasesToggle.Width = 34;
        _viewerAliasesToggle.Height = 32;
        _viewerAliasesToggle.MinHeight = 0;
        _viewerAliasesToggle.ToolTip = _config.ViewerAliasesCollapsed ? "Expand viewer aliases" : "Collapse viewer aliases";
        Grid.SetColumn(_viewerAliasesToggle, 2);
        aliasToolbar.Children.Add(_viewerAliasesToggle);
        aliasesContent.Children.Add(aliasToolbar);
        _viewerAliasRows = new StackPanel();
        _viewerAliasRowsContainer = new Border { Child = _viewerAliasRows };
        aliasesContent.Children.Add(_viewerAliasRowsContainer);
        aliasesCard.Child = aliasesContent;
        content.Children.Add(CreateSettingsSection("viewer_aliases", "Viewer Aliases", "nickname nicknames alias aliases alternate short names username search add entry", aliasesCard));
        RenderViewerAliasEntries();

        var streamlootsLanguageCard = new Border
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 6, 4, 8), MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Left
        };
        var streamlootsLanguageContent = new StackPanel();
        var streamlootsLanguageChoice = new ComboBox
        {
            Width = 190, Height = 34, Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = new[] { "English", "Spanish" },
            SelectedItem = _config.StreamlootsLanguage,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        streamlootsLanguageChoice.SelectionChanged += (_, _) =>
        {
            if (streamlootsLanguageChoice.SelectedItem is string selected)
            {
                _config.StreamlootsLanguage = selected;
                SaveConfig();
            }
        };
        streamlootsLanguageContent.Children.Add(streamlootsLanguageChoice);
        streamlootsLanguageContent.Children.Add(Label("Choose the language used by Streamloots. The browser extension will use it when recognizing feed details and gift controls.", false, "#8D9AB3"));
        streamlootsLanguageCard.Child = streamlootsLanguageContent;
        content.Children.Add(CreateSettingsSection("streamloots_language", "Streamloots Language", "English Spanish locale language feed gift controls", streamlootsLanguageCard));

        var browserCard = new Border
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 6, 4, 8), MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Left
        };
        var browserContent = new StackPanel();
        var browserRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        browserRow.Margin = new Thickness(0, 12, 0, 0);
        var browserChoice = new ComboBox
        {
            Width = 190, Height = 34, Margin = new Thickness(0, 0, 10, 0),
            ItemsSource = new[] { "Chrome", "Brave", "Microsoft Edge" },
            SelectedItem = _config.ExtensionBrowser
        };
        if (browserChoice.SelectedIndex < 0)
        {
            browserChoice.SelectedIndex = 0;
            _config.ExtensionBrowser = "Chrome";
            SaveConfig();
        }
        var browserHelp = Label(BrowserExtensionHelp(browserChoice.SelectedItem as string ?? "Chrome"), false, "#8D9AB3");
        browserHelp.Margin = new Thickness(0, 10, 0, 0);
        browserChoice.SelectionChanged += (_, _) =>
        {
            if (browserChoice.SelectedItem is string selected)
            {
                _config.ExtensionBrowser = selected;
                SaveConfig();
                browserHelp.Text = BrowserExtensionHelp(selected);
            }
        };
        var setupBrowser = MakeButton("View setup instructions", () => OpenBrowserExtensionSetup(browserChoice), "#5D6EFF");
        setupBrowser.Height = 34;
        setupBrowser.MinHeight = 0;
        browserRow.Children.Add(browserChoice);
        browserRow.Children.Add(setupBrowser);
        browserContent.Children.Add(browserRow);
        browserContent.Children.Add(browserHelp);
        browserCard.Child = browserContent;
        content.Children.Add(CreateSettingsSection("browser_extension", "Browser Extension", "Chrome Brave Microsoft Edge browser extensions install developer mode load unpacked", browserCard));

        var updateCard = new Border
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 6, 4, 8), MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Left
        };
        var updateContent = new StackPanel();
        var startupUpdateSwitch = CreateSwitch("Check for updates on startup", _config.CheckUpdatesOnStartup,
            value => { _config.CheckUpdatesOnStartup = value; SaveConfig(); });
        startupUpdateSwitch.Margin = new Thickness(0, 0, 0, 4);
        updateContent.Children.Add(startupUpdateSwitch);
        updateContent.Children.Add(Label("Only shows a prompt when a newer release is available.", false, "#8D9AB3"));
        var updateStatus = Label($"Current version: {AppUpdater.CurrentVersion.ToString(3)} · Enter a public GitHub owner/repository.", false, "#8D9AB3");
        updateStatus.Margin = new Thickness(0, 10, 0, 0);
        updateStatus.TextWrapping = TextWrapping.Wrap;
        Button updateButton = null!;
        Button checkUpdates = null!;
        AppUpdateRelease? pendingUpdate = null;
        var updateRepoRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var updateRepository = new TextBox
        {
            Width = 250, Height = 34, Margin = new Thickness(0, 0, 8, 0),
            Text = _config.UpdateRepository, VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Public GitHub repository in owner/repository format"
        };
        var saveUpdateSource = MakeButton("Save source", () =>
        {
            if (!AppUpdater.IsValidRepository(updateRepository.Text))
            {
                updateStatus.Text = "Enter a public GitHub repository as owner/repository.";
                return;
            }
            _config.UpdateRepository = updateRepository.Text.Trim();
            SaveConfig();
            updateStatus.Text = "Update source saved.";
        });
        saveUpdateSource.Height = 34;
        saveUpdateSource.MinHeight = 0;
        checkUpdates = MakeButton("Check for updates", async () =>
        {
            updateButton.IsEnabled = false;
            pendingUpdate = null;
            if (!AppUpdater.IsValidRepository(updateRepository.Text))
            {
                updateStatus.Text = "Enter a public GitHub repository as owner/repository.";
                return;
            }
            _config.UpdateRepository = updateRepository.Text.Trim();
            SaveConfig();
            checkUpdates.IsEnabled = false;
            updateStatus.Text = "Checking GitHub Releases…";
            try
            {
                var latest = await AppUpdater.GetLatestAsync(_config.UpdateRepository);
                if (latest.Version <= AppUpdater.CurrentVersion)
                {
                    updateStatus.Text = $"You’re up to date · version {AppUpdater.CurrentVersion.ToString(3)}.";
                    return;
                }
                pendingUpdate = latest;
                updateStatus.Text = $"Version {latest.Version} is available. Current version: {AppUpdater.CurrentVersion.ToString(3)}.";
                updateButton.IsEnabled = File.Exists(IOPath.Combine(AppPaths.Root, "StreamlootsGifter.exe"));
                if (!updateButton.IsEnabled)
                    updateStatus.Text += " Build and install the packaged app before using in-app updates.";
            }
            catch (Exception ex)
            {
                updateStatus.Text = $"Could not check for updates: {ex.Message}";
            }
            finally { checkUpdates.IsEnabled = true; }
        }, "#5D6EFF");
        checkUpdates.Height = 34;
        checkUpdates.MinHeight = 0;
        checkUpdates.Margin = new Thickness(8, 0, 0, 0);
        updateRepoRow.Children.Add(updateRepository);
        updateRepoRow.Children.Add(saveUpdateSource);
        updateRepoRow.Children.Add(checkUpdates);
        updateContent.Children.Add(updateRepoRow);
        updateContent.Children.Add(updateStatus);
        updateButton = MakeButton("Download and install update", async () =>
        {
            if (pendingUpdate is null) return;
            updateButton.IsEnabled = false;
            try
            {
                updateStatus.Text = $"Downloading {pendingUpdate.Tag}…";
                var progress = new Progress<double>(value => updateStatus.Text = $"Downloading {pendingUpdate.Tag} · {value:P0}");
                var archive = await AppUpdater.DownloadAsync(pendingUpdate, progress);
                AppUpdater.LaunchInstaller(archive, AppPaths.Root, Environment.ProcessId);
                updateStatus.Text = "Update downloaded. The app is closing to finish installing, then it will reopen.";
                await Task.Delay(500);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                updateStatus.Text = $"Update could not be installed: {ex.Message}";
                updateButton.IsEnabled = true;
            }
        }, "#5D6EFF");
        updateButton.Height = 36;
        updateButton.MinHeight = 0;
        updateButton.HorizontalAlignment = HorizontalAlignment.Left;
        updateButton.Margin = new Thickness(0, 10, 0, 0);
        updateButton.IsEnabled = false;
        updateContent.Children.Add(updateButton);
        updateCard.Child = updateContent;
        content.Children.Add(CreateSettingsSection("application_updates", "Application Updates", "GitHub release update download install version repository check", updateCard));

        var themeCard = new Border
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 6, 4, 8), MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Left
        };
        var themeContent = new StackPanel();
        var themeChoices = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, palette) in ThemePresets)
        {
            var selected = string.Equals(_config.ThemePreset, name, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 48,
                Height = 48,
                Background = FixedBrush(palette.BackgroundStart),
                BorderBrush = FixedBrush(selected ? palette.Accent : palette.Line),
                BorderThickness = new Thickness(selected ? 3 : 1),
                CornerRadius = new CornerRadius(7),
                Cursor = Cursors.Hand
            };
            if (selected)
            {
                var check = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                    Background = FixedBrush(palette.Accent), HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -4, 0),
                    Child = new TextBlock { Text = "✓", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center }
                };
                var overlay = new Grid();
                overlay.Children.Add(check);
                swatch.Child = overlay;
            }
            var choice = new Button { Content = swatch, Width = 54, Height = 54, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, ToolTip = name };
            choice.Click += (_, _) =>
            {
                ApplyThemePreset(name);
                SaveConfig();
                BuildDashboardGrid("settings");
            };
            themeChoices.Children.Add(choice);
        }
        themeContent.Children.Add(themeChoices);
        themeContent.Children.Add(Label($"Selected: {_config.ThemePreset}", false, "#8D9AB3"));
        themeCard.Child = themeContent;
        content.Children.Add(CreateSettingsSection("appearance", "Appearance", "theme themes color colour Midnight Light Ash Dark Onyx", themeCard));
        content.Children.Add(_settingsNoResults);
        ApplySettingsSectionFilter();
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Padding = new Thickness(0, 0, 10, 18)
        };
    }

    private async Task CheckForStartupUpdateAsync()
    {
        if (!AppUpdater.IsValidRepository(_config.UpdateRepository)) return;
        try
        {
            var release = await AppUpdater.GetLatestAsync(_config.UpdateRepository);
            if (_isClosing || release.Version <= AppUpdater.CurrentVersion) return;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            ShowStartupUpdatePrompt(release);
        }
        catch
        {
            // Startup checks stay quiet on network or GitHub errors. The manual
            // check in Settings remains available for diagnostics.
        }
    }

    private void ShowStartupUpdatePrompt(AppUpdateRelease release)
    {
        var dialog = new Window
        {
            Title = "Streamloots Gifter update",
            Width = 560,
            Height = 470,
            MinWidth = 460,
            MinHeight = 360,
            Owner = this,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0B1020")
        };
        ConfigureBorderlessWindow(dialog, captionHeight: 48, resizable: true);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var shell = new Border
        {
            Background = Brush("#151D30"), BorderBrush = Brush("#26324A"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Child = layout
        };
        var header = new Border { Background = Brush("#25324A"), CornerRadius = new CornerRadius(13, 13, 0, 0), Padding = new Thickness(16, 0, 12, 0) };
        var headerDock = new DockPanel { LastChildFill = true };
        var close = MakeButton("×", dialog.Close, "#202B40");
        close.Width = 30;
        close.Height = 28;
        close.MinHeight = 0;
        close.Padding = new Thickness(0);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        DockPanel.SetDock(close, Dock.Right);
        headerDock.Children.Add(close);
        headerDock.Children.Add(new TextBlock { Text = $"Update available · {release.Tag}", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#F4F6FF"), VerticalAlignment = VerticalAlignment.Center });
        header.Child = headerDock;
        layout.Children.Add(header);

        var body = new StackPanel { Margin = new Thickness(20, 18, 20, 12) };
        body.Children.Add(new TextBlock { Text = $"Streamloots Gifter {release.Version} is ready to install.", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F4F6FF"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Your Twitch credentials, settings, claim history, gift queue, and Bridge key are preserved.", Margin = new Thickness(0, 8, 0, 12), Foreground = Brush("#A6B2C8"), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(release.Notes))
        {
            body.Children.Add(new TextBlock { Text = "Release notes", FontWeight = FontWeights.Bold, Foreground = Brush("#F4F6FF"), Margin = new Thickness(0, 0, 0, 6) });
            body.Children.Add(new TextBlock { Text = release.Notes.Trim(), Foreground = Brush("#A6B2C8"), TextWrapping = TextWrapping.Wrap, LineHeight = 18 });
        }
        var progressText = new TextBlock { Foreground = Brush("#91A4FF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(progressText);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 16) };
        var later = MakeButton("Later", dialog.Close, "#202B40");
        later.Width = 82;
        later.Margin = new Thickness(0, 0, 8, 0);
        Button install = null!;
        install = MakeButton("Download and install", async () =>
        {
            install.IsEnabled = false;
            later.IsEnabled = false;
            try
            {
                progressText.Text = "Downloading update…";
                var progress = new Progress<double>(value => progressText.Text = $"Downloading update · {value:P0}");
                var archive = await AppUpdater.DownloadAsync(release, progress);
                progressText.Text = "Starting the installer…";
                AppUpdater.LaunchInstaller(archive, AppPaths.Root, Environment.ProcessId);
                dialog.Close();
                await Task.Delay(250);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                progressText.Text = $"The update could not be installed: {ex.Message}";
                install.IsEnabled = true;
                later.IsEnabled = true;
            }
        }, "#5D6EFF");
        install.MinWidth = 150;
        buttons.Children.Add(later);
        buttons.Children.Add(install);
        Grid.SetRow(buttons, 2);
        layout.Children.Add(buttons);
        dialog.Content = shell;
        dialog.Loaded += (_, _) => { dialog.Activate(); dialog.Focus(); };
        dialog.ShowDialog();
    }

    private static string BrowserExtensionHelp(string browser) =>
        $"Install the unpacked extension in {browser}. The setup guide will walk you through each step.";

    private void OpenBrowserExtensionSetup(ComboBox browserChoice)
    {
        var browser = browserChoice.SelectedItem as string ?? "Chrome";
        var window = new Window
        {
            Title = "Browser extension setup",
            Width = 610,
            Height = 650,
            MinWidth = 520,
            MinHeight = 500,
            Owner = this,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0B1020")
        };
        ConfigureBorderlessWindow(window, captionHeight: 48, resizable: true);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var shell = new Border
        {
            Background = Brush("#151D30"),
            BorderBrush = Brush("#26324A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = layout
        };
        var header = new Border
        {
            Background = Brush("#25324A"),
            CornerRadius = new CornerRadius(13, 13, 0, 0),
            Padding = new Thickness(16, 0, 12, 0)
        };
        var headerDock = new DockPanel { LastChildFill = true };
        var close = MakeButton("×", window.Close, "#202B40");
        close.Width = 30;
        close.Height = 28;
        close.MinHeight = 0;
        close.Padding = new Thickness(0);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        DockPanel.SetDock(close, Dock.Right);
        headerDock.Children.Add(close);
        headerDock.Children.Add(new TextBlock
        {
            Text = $"Set up {browser}", FontSize = 16, FontWeight = FontWeights.Bold,
            Foreground = Brush("#F4F6FF"), VerticalAlignment = VerticalAlignment.Center
        });
        header.Child = headerDock;
        layout.Children.Add(header);

        var steps = new StackPanel { Margin = new Thickness(20, 18, 20, 22) };
        AddSetupStep(steps, "1", "Open the extension manager", $"In {browser}, open the extensions page using this link.");
        AddSetupLink(steps, $"Open {browser} extensions", () => OpenBrowserExtensionPage(window, browser));
        AddSetupStep(steps, "2", "Turn on Developer mode", "On the extensions page, enable Developer mode. This reveals the Load unpacked button.");
        AddSetupStep(steps, "3", "Load the extension folder", "Choose Load unpacked, then select the Streamloots Gifter extension folder.");
        AddSetupLink(steps, "Open Streamloots Gifter extension folder", () => OpenBrowserExtensionFolder(window));
        AddSetupStep(steps, "4", "Approve and pin", "Approve the requested permissions. Pin Streamloots Gifter from the browser’s extensions menu for quick access.");
        AddSetupStep(steps, "5", "Connect it to the app", "In the app’s Streamloots Bridge widget, copy the bridge key. Open the extension, paste the key, and connect.");

        var scroll = new ScrollViewer
        {
            Content = steps,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        window.Content = shell;
        EventHandler closeGuideOnReturn = (_, _) =>
        {
            if (window.IsVisible) window.Close();
        };
        Activated += closeGuideOnReturn;
        window.Closed += (_, _) => Activated -= closeGuideOnReturn;
        window.Show();
    }

    private void OpenBrowserExtensionPage(Window owner, string browser)
    {
        try
        {
            var page = browser switch
            {
                "Brave" => "brave://extensions/",
                "Microsoft Edge" => "edge://extensions/",
                _ => "chrome://extensions/"
            };
            var executable = FindBrowserExecutable(browser);
            if (executable is null)
                throw new InvalidOperationException($"Could not find {browser} on this computer. Install it first, then try again.");
            Process.Start(new ProcessStartInfo(executable, $"\"{page}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Browser extension setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenBrowserExtensionFolder(Window owner)
    {
        try
        {
            var directory = FindExtensionSourceDirectory();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Browser extension folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddSetupStep(Panel steps, string number, string title, string description)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
            Background = Brush("#25324A"),
            Child = new TextBlock { Text = number, Foreground = Brush("#91A4FF"), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center }
        };
        Grid.SetColumn(badge, 0);
        row.Children.Add(badge);
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock { Text = title, Foreground = Brush("#F4F6FF"), FontSize = 13, FontWeight = FontWeights.Bold });
        copy.Children.Add(new TextBlock { Text = description, Foreground = Brush("#A6B2C8"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 17 });
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        steps.Children.Add(row);
    }

    private void AddSetupLink(Panel steps, string label, Action action)
    {
        var link = new Hyperlink(new Run(label)) { Foreground = Brush("#91A4FF") };
        link.Click += (_, args) => { args.Handled = true; action(); };
        var text = new TextBlock { Margin = new Thickness(34, -6, 0, 14), FontSize = 12 };
        text.Inlines.Add(link);
        steps.Children.Add(text);
    }

    private static string FindExtensionSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IOPath.Combine(directory.FullName, "streamloots_extension");
            if (File.Exists(IOPath.Combine(candidate, "manifest.json"))) return candidate;
        }
        throw new DirectoryNotFoundException("Could not find the Streamloots Gifter extension folder next to the app or its project folder.");
    }

    private static string? FindBrowserExecutable(string browser)
    {
        var executableName = browser switch
        {
            "Brave" => "brave.exe",
            "Microsoft Edge" => "msedge.exe",
            _ => "chrome.exe"
        };
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = browser switch
        {
            "Brave" => new[] { IOPath.Combine(local, "BraveSoftware", "Brave-Browser", "Application", executableName), IOPath.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", executableName), IOPath.Combine(programFilesX86, "BraveSoftware", "Brave-Browser", "Application", executableName) },
            "Microsoft Edge" => new[] { IOPath.Combine(programFilesX86, "Microsoft", "Edge", "Application", executableName), IOPath.Combine(programFiles, "Microsoft", "Edge", "Application", executableName), IOPath.Combine(local, "Microsoft", "Edge", "Application", executableName) },
            _ => new[] { IOPath.Combine(local, "Google", "Chrome", "Application", executableName), IOPath.Combine(programFiles, "Google", "Chrome", "Application", executableName), IOPath.Combine(programFilesX86, "Google", "Chrome", "Application", executableName) }
        };
        return candidates.FirstOrDefault(candidate => File.Exists(candidate));
    }

    private void ToggleViewerAliases()
    {
        _config.ViewerAliasesCollapsed = !_config.ViewerAliasesCollapsed;
        if (_viewerAliasRowsContainer is not null)
            _viewerAliasRowsContainer.Visibility = _config.ViewerAliasesCollapsed ? Visibility.Collapsed : Visibility.Visible;
        if (_viewerAliasesToggle is not null)
        {
            _viewerAliasesToggle.Content = _config.ViewerAliasesCollapsed ? "+" : "−";
            _viewerAliasesToggle.ToolTip = _config.ViewerAliasesCollapsed ? "Expand viewer aliases" : "Collapse viewer aliases";
        }
        SaveConfig();
    }

    private void AddViewerAliasEntry()
    {
        _config.ViewerAliases.Add(new ViewerAliasEntry());
        _config.ViewerAliasesCollapsed = false;
        SaveConfig();
        RenderViewerAliasEntries();
    }

    private void RemoveViewerAliasEntry(ViewerAliasEntry entry)
    {
        _config.ViewerAliases.Remove(entry);
        SaveConfig();
        RenderViewerAliasEntries();
    }

    private void RenderViewerAliasEntries()
    {
        if (_viewerAliasRows is null || _viewerAliasRowsContainer is null) return;
        _viewerAliasRows.Children.Clear();
        _viewerAliasRowsContainer.Visibility = _config.ViewerAliasesCollapsed ? Visibility.Collapsed : Visibility.Visible;
        if (_viewerAliasesToggle is not null)
        {
            _viewerAliasesToggle.Content = _config.ViewerAliasesCollapsed ? "+" : "−";
            _viewerAliasesToggle.ToolTip = _config.ViewerAliasesCollapsed ? "Expand viewer aliases" : "Collapse viewer aliases";
        }
        var searchText = _viewerAliasSearchBox?.Text.Trim() ?? "";
        var filteredAliases = _config.ViewerAliases
            .Where(entry => searchText.Length == 0 ||
                entry.StreamlootsUsername.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                entry.Aliases.Any(alias => alias.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (filteredAliases.Length == 0)
        {
            var emptyMessage = searchText.Length == 0
                ? "No viewer aliases yet. Add an entry to map nicknames to full usernames."
                : "No viewer aliases match that search.";
            _viewerAliasRows.Children.Add(Label(emptyMessage, false, "#8491AA"));
            return;
        }

        var columnHeadings = new Grid { Margin = new Thickness(5, 0, 42, 2) };
        columnHeadings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columnHeadings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var targetHeading = Label("Streamloots username", true, "#8491AA");
        targetHeading.FontSize = 10;
        var aliasesHeading = Label("Nicknames / aliases", true, "#8491AA");
        aliasesHeading.FontSize = 10;
        Grid.SetColumn(aliasesHeading, 1);
        columnHeadings.Children.Add(targetHeading);
        columnHeadings.Children.Add(aliasesHeading);
        _viewerAliasRows.Children.Add(columnHeadings);

        foreach (var entry in filteredAliases)
        {
            var row = new Grid { Margin = new Thickness(0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var target = new TextBox
            {
                Text = entry.StreamlootsUsername,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 5, 8, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Exact Streamloots username, e.g. Libra_ballz"
            };
            target.LostFocus += (_, _) =>
            {
                entry.StreamlootsUsername = target.Text.Trim();
                SaveConfig();
            };
            var aliases = new TextBox
            {
                Text = string.Join(", ", entry.Aliases),
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 5, 8, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Nicknames people might enter, separated by commas"
            };
            aliases.LostFocus += (_, _) =>
            {
                entry.Aliases = ParseViewerAliases(aliases.Text);
                SaveConfig();
            };
            var remove = MakeButton("×", () => RemoveViewerAliasEntry(entry), "#492B36");
            remove.Width = 28;
            remove.Height = 28;
            remove.MinHeight = 0;
            remove.Padding = new Thickness(0);
            Grid.SetColumn(target, 0);
            Grid.SetColumn(aliases, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(target);
            row.Children.Add(aliases);
            row.Children.Add(remove);
            row.Margin = new Thickness(0, 3, 0, 3);
            _viewerAliasRows.Children.Add(row);
            if (!ReferenceEquals(entry, filteredAliases[^1]))
                _viewerAliasRows.Children.Add(new Border { Height = 1, Background = Brush("#26324A"), Margin = new Thickness(2, 2, 34, 2) });
        }
    }

    private static List<string> ParseViewerAliases(string text) => text
        .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(alias => alias.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private string ResolveViewerAlias(string enteredName)
    {
        var name = enteredName.Trim();
        if (name.Length == 0) return name;
        var matches = _config.ViewerAliases
            .Where(entry => entry.Aliases.Any(alias => alias.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.StreamlootsUsername.Trim())
            .Where(username => username.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        // Only resolve unambiguous, exact aliases. Keep the input unchanged if
        // multiple viewers use the same nickname, so a chest cannot go astray.
        return matches.Length == 1 ? matches[0] : name;
    }

    private void BuildCards()
    {
        _cards["bridge"] = CreateCard("bridge", "Streamloots Bridge", BuildBridgeBody());
        _cards["rewards"] = CreateCard("rewards", "Channel Point Redeems", BuildRewardsBody());
        _cards["free_chest"] = CreateCard("free_chest", "Free Chest Hour", BuildFreeChestBody());
        _cards["free_chest_history"] = CreateCard("free_chest_history", "Claim History", BuildHistoryBody());
        _cards["activity"] = CreateCard("activity", "Redemption Activity", BuildActivityBody());
        _cards["streamloots_cards"] = CreateCard("streamloots_cards", "Streamloots Cards", BuildStreamlootsCardsBody());
        foreach (var key in ConfigStore.Sections)
            UpdateCardActions(key);
        UpdateFreeChestCompactControls();
    }

    private Border CreateCard(string key, string title, UIElement body)
    {
        var root = new Border
        {
            Background = Brush("#151D30"), BorderBrush = Brush("#26324A"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(0), Margin = new Thickness(0), Tag = key,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            MinWidth = key == "bridge" ? 300 : 260, MinHeight = key == "bridge" ? 250 : 210, ClipToBounds = true, AllowDrop = true
        };
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new Grid { Margin = new Thickness(0), Background = Brushes.Transparent, Cursor = Cursors.SizeAll, MinHeight = 44 };
        _cardHeadings[key] = heading;
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AttachDragSource(heading, key, true);
        var labels = new StackPanel();
        labels.VerticalAlignment = VerticalAlignment.Center;
        labels.Margin = new Thickness(14, 0, 0, 0);
        labels.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#F4F6FF") });
        Grid.SetColumn(labels, 0);
        heading.Children.Add(labels);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var add = MakeButton("Add", () => AddToDashboard(key), "#174331");
        add.Foreground = Brush("#52DB91");
        add.Margin = new Thickness(6, 0, 0, 0);
        _addButtons[key] = add;
        var remove = MakeButton("×", () => RemoveFromDashboard(key), "#492B36");
        remove.Foreground = Brush("#FF8585");
        remove.Content = CloseGlyph("#FF8585");
        remove.FontSize = 18;
        remove.Width = 36;
        remove.Height = 32;
        remove.MinHeight = 0;
        remove.Padding = new Thickness(4, 0, 4, 0);
        remove.Margin = new Thickness(6, 0, 0, 0);
        _removeButtons[key] = remove;
        var collapse = MakeButton("−", () => ToggleCollapse(key), "#202B40");
        collapse.Content = CollapseGlyph(collapsed: false);
        collapse.FontSize = 20;
        collapse.FontWeight = FontWeights.Bold;
        collapse.Width = 36;
        collapse.Height = 32;
        collapse.MinHeight = 0;
        collapse.Padding = new Thickness(4, 0, 4, 0);
        collapse.Margin = new Thickness(6, 0, 0, 0);
        _collapseButtons[key] = collapse;
        actions.Margin = new Thickness(0, 0, 12, 0);
        actions.Children.Add(add);
        actions.Children.Add(remove);
        if (key == "bridge") actions.Children.Add(collapse);
        var help = MakeButton("?", () => WidgetGuideButton_Click(key), "#202B40");
        help.Width = 30;
        help.Height = 30;
        help.MinHeight = 0;
        help.Padding = new Thickness(0);
        help.FontSize = 15;
        help.FontWeight = FontWeights.Bold;
        help.Margin = new Thickness(6, 0, 0, 0);
        help.ToolTip = $"How {title} works";
        actions.Children.Add(help);
        Grid.SetColumn(actions, 1);
        heading.Children.Add(actions);
        var headingSurface = new Border
        {
            Background = Brush("#25324A"),
            CornerRadius = new CornerRadius(15, 15, 0, 0),
            Child = heading
        };
        shell.Children.Add(headingSurface);
        FrameworkElement bodyContent = (FrameworkElement)body;
        if (key == "free_chest")
        {
            var bodyPanel = new StackPanel();
            bodyPanel.Children.Add(body);
            _freeChestExpandedBody = body;
            _freeChestCompactBody = BuildCompactFreeChestControls();
            _freeChestCompactBody.Visibility = Visibility.Collapsed;
            bodyPanel.Children.Add(_freeChestCompactBody);
            bodyContent = bodyPanel;
        }
        _cardBodies[key] = bodyContent;
        if (key is "rewards" or "free_chest_history" or "activity")
        {
            bodyContent.Margin = new Thickness(14, 12, 14, 14);
            Grid.SetRow(bodyContent, 1);
            shell.Children.Add(bodyContent);
        }
        else
        {
            var bodyScroll = new ScrollViewer
            {
                Content = bodyContent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(14, 12, 14, 14)
            };
            Grid.SetRow(bodyScroll, 1);
            shell.Children.Add(bodyScroll);
        }
        var cardFrame = new Grid();
        cardFrame.Children.Add(shell);
        var dropHint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(86, 93, 110, 255)),
            BorderBrush = Brush("#91A4FF"), BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10), Margin = new Thickness(4),
            Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "Drop to split this panel",
                Foreground = Brush("#F4F6FF"), FontWeight = FontWeights.Bold,
                FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        cardFrame.Children.Add(dropHint);
        _widgetDropHints[key] = dropHint;
        root.PreviewDragOver += (_, args) => HandleWidgetDragOver(key, root, args);
        root.PreviewDragLeave += (_, _) => HideWidgetDropHints();
        root.PreviewDrop += (_, args) => HandleWidgetDrop(key, root, args);
        root.Child = cardFrame;
        return root;
    }

    private UIElement BuildBridgeBody()
    {
        var panel = new StackPanel();

        var statusCard = new Border
        {
            Background = Brush("#10182A"),
            BorderBrush = Brush("#26324A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 10, 0, 12)
        };
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _bridgeStatusDot = new Ellipse { Width = 10, Height = 10, Fill = Brush("#E5B94C"), Margin = new Thickness(0, 0, 9, 0) };
        statusRow.Children.Add(_bridgeStatusDot);
        var statusLabels = new StackPanel();
        _bridgeStatus = Label("Waiting for the browser extension", true);
        _bridgeStatus.Margin = new Thickness(0);
        statusLabels.Children.Add(_bridgeStatus);
        statusRow.Children.Add(statusLabels);
        statusCard.Child = statusRow;
        panel.Children.Add(statusCard);

        panel.Children.Add(Label("Extension Bridge Key", true));
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var copy = MakeButton("Copy key", () =>
        {
            Clipboard.SetText(_giftQueue.Token);
            if (_bridgeStatus is not null && _bridgeServerReady) UpdateBridgeStatus("Bridge key copied. Paste it into extension settings.");
        }, "#174331");
        _bridgeCopyButton = copy;
        copy.Foreground = Brush("#52DB91");
        _bridgeRevealButton = MakeButton("Show", ToggleBridgeKeyVisibility, "#202B40");
        _bridgeRevealButton.Margin = new Thickness(6, 1, 0, 1);
        actions.Children.Add(copy);
        actions.Children.Add(_bridgeRevealButton);
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        _bridgeKeyBox = new TextBox
        {
            Text = new string('•', 32),
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Bridge key is hidden"
        };
        row.Children.Add(_bridgeKeyBox);
        panel.Children.Add(row);
        var rotate = MakeButton("Generate new key", RegenerateBridgeKey, "#392833");
        _bridgeGenerateButton = rotate;
        rotate.Foreground = Brush("#FF8585");
        rotate.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(rotate);
        return panel;
    }

    private UIElement BuildStreamlootsCardsBody()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var feedInfo = new Border
        {
            Background = Brush("#10182A"), BorderBrush = Brush("#26324A"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 12)
        };
        var feedInstructions = new StackPanel();
        _streamlootsCardStatus = Label("Waiting for the Streamloots Activity Feed", true, "#A5B0C6");
        _streamlootsCardStatus.HorizontalAlignment = HorizontalAlignment.Left;
        _streamlootsCardStatus.MaxWidth = 320;
        _streamlootsCardStatus.TextWrapping = TextWrapping.Wrap;
        feedInstructions.Children.Add(_streamlootsCardStatus);
        _streamlootsFeedInstruction = Label("Keep the Streamloots Activity Feed open while using card automation.", false, "#8491AA");
        _streamlootsFeedInstruction.HorizontalAlignment = HorizontalAlignment.Left;
        _streamlootsFeedInstruction.MaxWidth = 320;
        feedInstructions.Children.Add(_streamlootsFeedInstruction);
        feedInfo.Child = feedInstructions;
        panel.Children.Add(feedInfo);

        // Use wrapping field groups instead of a wide fixed-column form. Narrow dashboard
        // widgets now reflow these controls onto extra lines rather than clipping the right side.
        var form = new WrapPanel { Margin = new Thickness(0, 2, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
        _streamlootsCardForm = form;
        _streamlootsCardNameInput = new TextBox { Height = 32, ToolTip = "Exact Streamloots card name" };
        _streamlootsCardActionInput = new ComboBox { Height = 32, ItemsSource = new[] { "Gift a chest to redeemer", "Gift a chest to entered username" }, SelectedIndex = 0, VerticalContentAlignment = VerticalAlignment.Center };
        _streamlootsCardCollectionInput = new TextBox { Height = 32, ToolTip = "Streamloots collection name" };
        _streamlootsCardChestsInput = new TextBox { Height = 32, Text = "1", ToolTip = "Number of chests" };
        _streamlootsCardCardsInput = new ComboBox { Height = 32, ItemsSource = new[] { 1, 2, 3 }, SelectedIndex = 2, VerticalContentAlignment = VerticalAlignment.Center };
        _streamlootsCardNameField = CreateResponsiveField("Card name", _streamlootsCardNameInput, 137);
        _streamlootsCardActionField = CreateResponsiveField("When redeemed", _streamlootsCardActionInput, 182);
        _streamlootsCardCollectionField = CreateResponsiveField("Collection", _streamlootsCardCollectionInput, 142);
        _streamlootsCardChestField = CreateResponsiveField("Chests", _streamlootsCardChestsInput, 62);
        _streamlootsCardCardsField = CreateResponsiveField("Cards", _streamlootsCardCardsInput, 62);
        form.Children.Add(_streamlootsCardNameField);
        form.Children.Add(_streamlootsCardActionField);
        form.Children.Add(_streamlootsCardCollectionField);
        form.Children.Add(_streamlootsCardChestField);
        form.Children.Add(_streamlootsCardCardsField);
        _addStreamlootsCardRuleButton = MakeButton("Add card", AddStreamlootsCardRule, "#174331");
        _addStreamlootsCardRuleButton.Foreground = Brush("#52DB91");
        _addStreamlootsCardRuleButton.Height = 32;
        _addStreamlootsCardRuleButton.MinHeight = 0;
        _streamlootsCardAddField = CreateResponsiveField("", _addStreamlootsCardRuleButton, 88);
        form.Children.Add(_streamlootsCardAddField);
        _cancelStreamlootsCardRuleEditButton = MakeButton("Cancel", CancelStreamlootsCardRuleEdit, "#25324A");
        _cancelStreamlootsCardRuleEditButton.Width = 72;
        _cancelStreamlootsCardRuleEditButton.Height = 32;
        _cancelStreamlootsCardRuleEditButton.MinHeight = 0;
        _cancelStreamlootsCardRuleEditButton.Visibility = Visibility.Collapsed;
        _streamlootsCardCancelField = CreateResponsiveField("", _cancelStreamlootsCardRuleEditButton, 72);
        form.Children.Add(_streamlootsCardCancelField);
        panel.Children.Add(form);
        _streamlootsCardRulesList = new StackPanel();
        panel.Children.Add(_streamlootsCardRulesList);
        RenderStreamlootsCardRules();
        return panel;
    }

    private void AddStreamlootsCardRule()
    {
        var name = _streamlootsCardNameInput?.Text.Trim() ?? "";
        var collection = _streamlootsCardCollectionInput?.Text.Trim() ?? "";
        if (name.Length == 0 || collection.Length == 0 || !int.TryParse(_streamlootsCardChestsInput?.Text, out var chests) || chests is < 1 or > 50)
        {
            MessageBox.Show(this, "Enter the exact card name, collection, and a chest amount from 1 to 50.", "Card rule", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var mode = _streamlootsCardActionInput?.SelectedIndex == 1 ? "gift_user" : "free_gift";
        if (_config.StreamlootsCardRules.Any(rule => !ReferenceEquals(rule, _editingStreamlootsCardRule) && rule.CardName.Trim().Equals(name, StringComparison.Ordinal)))
        {
            MessageBox.Show(this, "A rule for this exact card name and capitalization already exists.", "Card rule", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_editingStreamlootsCardRule is null)
        {
            _config.StreamlootsCardRules.Add(new StreamlootsCardRule
            {
                CardName = name,
                Action = mode,
                Collection = collection,
                Chests = chests,
                Cards = _streamlootsCardCardsInput?.SelectedItem is int cards ? cards : 3
            });
        }
        else
        {
            _editingStreamlootsCardRule.CardName = name;
            _editingStreamlootsCardRule.Action = mode;
            _editingStreamlootsCardRule.Collection = collection;
            _editingStreamlootsCardRule.Chests = chests;
            _editingStreamlootsCardRule.Cards = _streamlootsCardCardsInput?.SelectedItem is int cards ? cards : 3;
        }
        _editingStreamlootsCardRule = null;
        SaveConfig();
        _streamlootsCardNameInput!.Clear();
        _streamlootsCardCollectionInput!.Clear();
        _streamlootsCardChestsInput!.Text = "1";
        _addStreamlootsCardRuleButton!.Content = "Add card";
        _cancelStreamlootsCardRuleEditButton!.Visibility = Visibility.Collapsed;
        RenderStreamlootsCardRules();
    }

    private void BeginStreamlootsCardRuleEdit(StreamlootsCardRule rule)
    {
        _editingStreamlootsCardRule = rule;
        _streamlootsCardNameInput!.Text = rule.CardName;
        _streamlootsCardActionInput!.SelectedIndex = rule.Action == "gift_user" ? 1 : 0;
        _streamlootsCardCollectionInput!.Text = rule.Collection;
        _streamlootsCardChestsInput!.Text = rule.Chests.ToString();
        _streamlootsCardCardsInput!.SelectedItem = rule.Cards;
        _addStreamlootsCardRuleButton!.Content = "Save changes";
        _cancelStreamlootsCardRuleEditButton!.Visibility = Visibility.Visible;
        _streamlootsCardNameInput.Focus();
    }

    private void CancelStreamlootsCardRuleEdit()
    {
        _editingStreamlootsCardRule = null;
        _streamlootsCardNameInput?.Clear();
        _streamlootsCardCollectionInput?.Clear();
        if (_streamlootsCardChestsInput is not null) _streamlootsCardChestsInput.Text = "1";
        if (_streamlootsCardActionInput is not null) _streamlootsCardActionInput.SelectedIndex = 0;
        if (_streamlootsCardCardsInput is not null) _streamlootsCardCardsInput.SelectedItem = 3;
        if (_addStreamlootsCardRuleButton is not null) _addStreamlootsCardRuleButton.Content = "Add card";
        if (_cancelStreamlootsCardRuleEditButton is not null) _cancelStreamlootsCardRuleEditButton.Visibility = Visibility.Collapsed;
    }

    private void RenderStreamlootsCardRules(bool includeGuideExample = false)
    {
        if (_streamlootsCardRulesList is null) return;
        _streamlootsCardRulesList.Children.Clear();
        _streamlootsCardRuleSwitch = null;
        _streamlootsCardRuleEditRemoveActions = null;
        var rules = _config.StreamlootsCardRules.ToList();
        StreamlootsCardRule? guideExample = null;
        if (includeGuideExample && rules.Count == 0)
        {
            guideExample = new StreamlootsCardRule
            {
                CardName = "gift chest",
                Action = "free_gift",
                Collection = "How to Fish",
                Chests = 1,
                Cards = 3,
                Enabled = true
            };
            rules.Add(guideExample);
        }
        foreach (var rule in rules)
        {
            var isGuideExample = ReferenceEquals(rule, guideExample);
            var row = new Border { Background = Brush("#10182A"), BorderBrush = Brush("#26324A"), BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 5, 7, 5), Margin = new Thickness(0, 3, 0, 3) };
            var content = new DockPanel();
            var remove = MakeButton("Remove", () =>
            {
                if (ReferenceEquals(rule, _editingStreamlootsCardRule)) CancelStreamlootsCardRuleEdit();
                _config.StreamlootsCardRules.Remove(rule);
                SaveConfig();
                RenderStreamlootsCardRules();
            }, "#392833");
            remove.Height = 27;
            remove.MinHeight = 0;
            remove.Padding = new Thickness(9, 3, 9, 3);
            remove.Margin = new Thickness(8, 0, 0, 0);
            remove.IsEnabled = !isGuideExample;
            var edit = MakeButton("Edit", () => BeginStreamlootsCardRuleEdit(rule), "#25324A");
            edit.Width = 48;
            edit.Height = 27;
            edit.MinHeight = 0;
            edit.Padding = new Thickness(5, 3, 5, 3);
            edit.Margin = new Thickness(8, 0, 0, 0);
            edit.IsEnabled = !isGuideExample;
            var ruleActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ruleActions.Children.Add(edit);
            ruleActions.Children.Add(remove);
            ruleActions.Children[1].SetCurrentValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            DockPanel.SetDock(ruleActions, Dock.Right);
            content.Children.Add(ruleActions);
            _streamlootsCardRuleEditRemoveActions ??= ruleActions;
            var enabledToggle = CreateSwitch("", rule.Enabled, enabled =>
            {
                rule.Enabled = enabled;
                SaveConfig();
            });
            enabledToggle.ToolTip = "Enable or disable automation for this card";
            enabledToggle.Margin = new Thickness(0, 0, 10, 0);
            enabledToggle.IsEnabled = !isGuideExample;
            DockPanel.SetDock(enabledToggle, Dock.Left);
            content.Children.Add(enabledToggle);
            _streamlootsCardRuleSwitch ??= enabledToggle;
            var action = rule.Action == "gift_user" ? "gift entered username" : "gift redeemer";
            content.Children.Add(Label($"{(isGuideExample ? "Example · " : "")}{rule.CardName}  →  {action} · {rule.Collection} · {rule.Chests} {ChestWord(rule.Chests)} × {rule.Cards} cards", false, "#DCE3F5"));
            row.Child = content;
            _streamlootsCardRulesList.Children.Add(row);
        }
        if (rules.Count == 0)
            _streamlootsCardRulesList.Children.Add(Label("No Streamloots card rules configured yet.", false, "#8491AA"));
    }

    private StreamlootsCardFeedResult HandleStreamlootsCardRedeemed(StreamlootsCardFeedEvent cardEvent)
    {
        var normalizedCardName = NormalizeStreamlootsCardName(cardEvent.CardName);
        var rule = _config.StreamlootsCardRules.FirstOrDefault(item => NormalizeStreamlootsCardName(item.CardName).Equals(normalizedCardName, StringComparison.Ordinal));
        if (rule is null || !rule.Enabled)
        {
            if (_streamlootsCardStatus is not null) _streamlootsCardStatus.Text = $"Ignored “{normalizedCardName}” · {(rule is null ? "no automation rule configured" : "automation is disabled")}";
            return new(true, rule is null ? "No matching card rule; event ignored." : "Card automation is disabled; event ignored.");
        }
        var recipient = ResolveViewerAlias(rule.Action == "gift_user" ? cardEvent.Input.Trim() : cardEvent.Redeemer.Trim());
        var viewer = string.IsNullOrWhiteSpace(cardEvent.Redeemer) ? "Streamloots viewer" : cardEvent.Redeemer;
        var request = new GiftRequest(
            $"streamloots-card:{cardEvent.EventId}",
            viewer,
            recipient,
            rule.Collection,
            $"Streamloots card · {normalizedCardName}",
            "streamloots_card",
            normalizedCardName,
            rule.Chests,
            rule.Cards,
            cardEvent.PlayedAt);
        var (status, detail) = QueueRequest(request, viewer, request.RewardName, recipient);
        if (_streamlootsCardStatus is not null)
            _streamlootsCardStatus.Text = string.IsNullOrWhiteSpace(recipient)
                ? rule.Action == "gift_user"
                    ? $"Detected “{normalizedCardName}” from {viewer} · entered-recipient field was empty"
                    : $"Detected “{normalizedCardName}” · Activity Feed event had no redeemer username"
                : status == "blocked"
                    ? $"Detected “{normalizedCardName}” from {viewer} · not queued: {detail}"
                    : $"Detected “{normalizedCardName}” from {viewer} · gift queued for {recipient}";
        return status == "blocked"
            ? new(false, detail)
            : new(true, detail);
    }

    private static string NormalizeStreamlootsCardName(string value) =>
        Regex.Replace(value ?? "", @"\s*[·•⋅∙\-–—]\s*(?:just now|(?:about\s+)?(?:a|an|one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+(?:seconds?|minutes?|hours?|days?|weeks?|months?|years?)\s+ago)\s*$", "", RegexOptions.IgnoreCase)
            .Trim();

    private UIElement BuildCompactFreeChestControls()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 1) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _compactStartFreeChestButton = MakeButton("Start timer", StartFreeChestHour, "#5D6EFF");
        _compactStartFreeChestButton.Width = 130;
        _compactStartFreeChestButton.Height = 32;
        _compactStartFreeChestButton.MinHeight = 0;
        _compactStopFreeChestButton = MakeButton("Stop timer", () => StopFreeChestHour("Free Chest Hour cancelled."), "#392833");
        _compactStopFreeChestButton.Width = 120;
        _compactStopFreeChestButton.Height = 32;
        _compactStopFreeChestButton.MinHeight = 0;
        _compactStopFreeChestButton.Margin = new Thickness(8, 1, 0, 1);
        _compactStopFreeChestButton.IsEnabled = false;
        actions.Children.Add(_compactStartFreeChestButton);
        actions.Children.Add(_compactStopFreeChestButton);
        row.Children.Add(actions);
        _freeChestCompactCountdown = Label("00:00");
        _freeChestCompactCountdown.FontSize = 20;
        _freeChestCompactCountdown.VerticalAlignment = VerticalAlignment.Center;
        _freeChestCompactCountdown.Margin = new Thickness(10, 0, 0, 0);
        _freeChestCompactCountdown.Visibility = Visibility.Collapsed;
        row.Children.Add(_freeChestCompactCountdown);
        return row;
    }

    private void RegenerateBridgeKey()
    {
        var confirm = MessageBox.Show(this,
            "Generate a new Bridge key? The extension will disconnect until you press “Get key & connect” in its popup.",
            "Generate new Bridge key", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        _giftQueue.RotateToken();
        _bridgeKeyVisible = false;
        if (_bridgeRevealButton is not null) _bridgeRevealButton.Content = "Show";
        UpdateBridgeStatus("New key generated · reconnect the extension from its popup.");
    }

    private void ToggleBridgeKeyVisibility()
    {
        _bridgeKeyVisible = !_bridgeKeyVisible;
        if (_bridgeKeyBox is not null)
        {
            _bridgeKeyBox.Text = _bridgeKeyVisible ? _giftQueue.Token : new string('•', 32);
            _bridgeKeyBox.ToolTip = _bridgeKeyVisible ? "Bridge key is visible" : "Bridge key is hidden";
        }
        if (_bridgeRevealButton is not null) _bridgeRevealButton.Content = _bridgeKeyVisible ? "Hide" : "Show";
    }

    private void UpdateBridgeStatus(string? message = null)
    {
        if (_bridgeKeyBox is not null)
        {
            _bridgeKeyBox.Text = _bridgeKeyVisible ? _giftQueue.Token : new string('•', 32);
            _bridgeKeyBox.ToolTip = _bridgeKeyVisible ? "Bridge key is visible" : "Bridge key is hidden";
        }
        if (_bridgeStatus is null || _bridgeStatusDot is null) return;
        if (!_bridgeServerReady)
        {
            _bridgeStatus.Text = _bridgeError is null ? "Starting local Bridge…" : $"Bridge unavailable · {_bridgeError}";
            _bridgeStatus.Foreground = Brush(_bridgeError is null ? "#E5B94C" : "#FF8585");
            _bridgeStatusDot.Fill = Brush(_bridgeError is null ? "#E5B94C" : "#FF8585");
            return;
        }
        var extensionConnected = _giftQueue.LastExtensionActivity is { } lastSeen && DateTimeOffset.UtcNow - lastSeen < TimeSpan.FromSeconds(8);
        _bridgeStatus.Text = message ?? (extensionConnected ? "Streamloots extension connected" : "Bridge ready · waiting for extension");
        _bridgeStatus.Foreground = Brush(extensionConnected ? "#52DB91" : "#E5B94C");
        _bridgeStatusDot.Fill = Brush(extensionConnected ? "#35C979" : "#E5B94C");
    }


    private UIElement BuildRewardsBody()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        _rewardToolbar = buttons;
        _rewardRefreshButton = MakeButton("Refresh rewards", (Func<Task>)RefreshRewardsAsync);
        buttons.Children.Add(_rewardRefreshButton);
        var save = MakeButton("Save mappings", SaveMappings, "#5D6EFF");
        _rewardSaveButton = save;
        save.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(save);
        var inactive = CreateSwitch("Inactive Rewards", false);
        inactive.Margin = new Thickness(14, 0, 8, 0);
        inactive.VerticalAlignment = VerticalAlignment.Center;
        _showHiddenRewards = CreateSwitch("Hidden Rewards", false);
        _showHiddenRewards.Margin = new Thickness(4, 0, 0, 0);
        _showHiddenRewards.VerticalAlignment = VerticalAlignment.Center;
        buttons.Children.Add(inactive);
        buttons.Children.Add(_showHiddenRewards);
        Grid.SetRow(buttons, 0);
        panel.Children.Add(buttons);
        var list = new StackPanel();
        var listScroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(listScroll, 1);
        panel.Children.Add(listScroll);
        _rewardContainer = list;
        _showInactive = inactive;
        inactive.Checked += (_, _) => RenderRewards(_lastRewards);
        inactive.Unchecked += (_, _) => RenderRewards(_lastRewards);
        _showHiddenRewards.Checked += (_, _) => RenderRewards(_lastRewards);
        _showHiddenRewards.Unchecked += (_, _) => RenderRewards(_lastRewards);
        return panel;
    }

    private UIElement BuildFreeChestBody()
    {
        var panel = new StackPanel();
        var live = new DockPanel();
        _freeChestCountdown = Label("00:00");
        _freeChestCountdown.FontSize = 23;
        _freeChestCountdown.Visibility = Visibility.Collapsed;
        _freeChestStatus = Label("Inactive · connect Twitch to listen for keywords");
        _freeChestStatus.FontWeight = FontWeights.Bold;
        _freeChestStatus.HorizontalAlignment = HorizontalAlignment.Left;
        live.Children.Add(_freeChestStatus);
        panel.Children.Add(live);
        _freeChestChatActivity = Label("Chat listener connecting…", false, "#8491AA");
        _freeChestChatActivity.Margin = new Thickness(0, 2, 0, 3);
        panel.Children.Add(_freeChestChatActivity);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        buttons.HorizontalAlignment = HorizontalAlignment.Left;
        _freeChestTimerButtons = buttons;
        _startFreeChestButton = MakeButton("Start timer", StartFreeChestHour, "#5D6EFF");
        _startFreeChestButton.Width = 150;
        _cancelFreeChestButton = MakeButton("Cancel timer", () => StopFreeChestHour("Free Chest Hour cancelled."), "#392833");
        _cancelFreeChestButton.Width = 135;
        _cancelFreeChestButton.Margin = new Thickness(8, 0, 0, 0);
        _cancelFreeChestButton.IsEnabled = false;
        buttons.Children.Add(_startFreeChestButton);
        buttons.Children.Add(_cancelFreeChestButton);
        _freeChestCountdown.Margin = new Thickness(10, 0, 0, 0);
        _freeChestCountdown.VerticalAlignment = VerticalAlignment.Center;
        buttons.Children.Add(_freeChestCountdown);
        panel.Children.Add(buttons);
        var durationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 5) };
        durationRow.HorizontalAlignment = HorizontalAlignment.Left;
        var durationLabel = Label("Duration (minutes)", true);
        durationLabel.VerticalAlignment = VerticalAlignment.Center;
        durationRow.Children.Add(durationLabel);
        _freeChestDurationInput = new TextBox { Text = _config.FreeChestDurationMinutes.ToString(), Width = 58, Margin = new Thickness(9, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Timer duration, 1–60 minutes" };
        _freeChestDurationInput.LostFocus += (_, _) =>
        {
            if (int.TryParse(_freeChestDurationInput.Text, out var minutes) && minutes is >= 1 and <= 60)
            {
                _config.FreeChestDurationMinutes = minutes;
                SaveConfig();
            }
        };
        durationRow.Children.Add(_freeChestDurationInput);
        _freeChestDurationRow = durationRow;
        panel.Children.Add(durationRow);
        var form = new WrapPanel { Margin = new Thickness(0, 5, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
        _keywordInput = new TextBox { Height = 32, ToolTip = "Keyword (s1)" };
        _collectionInput = new TextBox { Height = 32, ToolTip = "Streamloots collection" };
        _packsInput = new TextBox { Height = 32, Text = "1", ToolTip = "Chests" };
        _cardsInput = new ComboBox { Height = 32, ItemsSource = new[] { 1, 2, 3 }, SelectedIndex = 2 };
        _freeChestKeywordField = CreateResponsiveField("Keyword", _keywordInput, 80);
        _freeChestCollectionField = CreateResponsiveField("Collection", _collectionInput, 105);
        _freeChestChestField = CreateResponsiveField("Chests", _packsInput, 50);
        _freeChestCardsField = CreateResponsiveField("Cards", _cardsInput, 58);
        form.Children.Add(_freeChestKeywordField);
        form.Children.Add(_freeChestCollectionField);
        form.Children.Add(_freeChestChestField);
        form.Children.Add(_freeChestCardsField);
        _addFreeChestRuleButton = MakeButton("Add keyword", AddFreeChestRule);
        _addFreeChestRuleButton.Width = 106;
        _addFreeChestRuleButton.Height = 32;
        _addFreeChestRuleButton.MinHeight = 0;
        _addFreeChestRuleButton.Padding = new Thickness(8, 6, 8, 6);
        _freeChestAddField = CreateResponsiveField("", _addFreeChestRuleButton, 106);
        form.Children.Add(_freeChestAddField);
        _cancelFreeChestRuleEditButton = MakeButton("Cancel", CancelFreeChestRuleEdit, "#25324A");
        _cancelFreeChestRuleEditButton.Width = 72;
        _cancelFreeChestRuleEditButton.Height = 32;
        _cancelFreeChestRuleEditButton.MinHeight = 0;
        _cancelFreeChestRuleEditButton.Visibility = Visibility.Collapsed;
        form.Children.Add(CreateResponsiveField("", _cancelFreeChestRuleEditButton, 72));
        panel.Children.Add(form);
        _freeChestRulesContainer = new StackPanel();
        panel.Children.Add(_freeChestRulesContainer);
        RenderFreeChestRules();
        UpdateFreeChestCompactControls();
        return panel;
    }

    private UIElement BuildHistoryBody()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
        _historyFiltersPanel = filters;
        _historyUserFilter = CreateUsernameTextFilter(_config.ClaimHistoryUserFilter, 120, () => _historyNameSuggestions, RenderHistory, value => { _config.ClaimHistoryUserFilter = value; SaveConfig(); });
        _historyUsernameField = CreateResponsiveField("Username", _historyUserFilter, 120);
        _historySourceFilter = new ComboBox { ItemsSource = ConfigStore.GiftSourceFilters, Width = 131, SelectedItem = _config.ClaimHistorySourceFilter, VerticalContentAlignment = VerticalAlignment.Center };
        _historySourceFilter.SelectionChanged += (_, _) =>
        {
            if (_historySourceFilter.SelectedItem is string source)
            {
                _config.ClaimHistorySourceFilter = source;
                SaveConfig();
                RenderHistory();
            }
        };
        _historySourceField = CreateResponsiveField("Gifting type", _historySourceFilter, 131);
        filters.Children.Add(_historyUsernameField);
        filters.Children.Add(_historySourceField);
        var filter = new ComboBox { ItemsSource = ConfigStore.HistoryFilters, Width = 135, SelectedItem = _config.HistoryFilter, VerticalContentAlignment = VerticalAlignment.Center };
        filter.SelectionChanged += (_, _) =>
        {
            if (filter.SelectedItem is string value)
            {
                _config.HistoryFilter = value;
                SaveConfig();
                RenderHistory();
            }
        };
        _historyStatusField = CreateResponsiveField("Status", filter, 135);
        filters.Children.Add(_historyStatusField);
        var retentionButton = MakeButton("", OpenHistoryCleanupWindow, "#492B36");
        _historyCleanupButton = retentionButton;
        retentionButton.Width = 34;
        retentionButton.Height = 30;
        retentionButton.MinHeight = 0;
        retentionButton.Padding = new Thickness(0);
        retentionButton.Content = TrashGlyph();
        retentionButton.ToolTip = "Clear or filter claim history";
        filters.Children.Add(CreateResponsiveField("", retentionButton, 34));
        retentionButton.VerticalAlignment = VerticalAlignment.Bottom;
        panel.Children.Add(filters);
        _historyFilter = filter;
        RefreshHistoryNameSuggestions();
        _historyList = new StackPanel();
        var listScroll = new ScrollViewer { Content = _historyList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(listScroll, 1);
        panel.Children.Add(listScroll);
        RenderHistory();
        return panel;
    }

    private UIElement BuildActivityBody()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Stretch };
        _activityFiltersPanel = filters;
        _activityUserFilter = CreateUsernameTextFilter(_config.ActivityUserFilter, 160, () => _activityNameSuggestions, RenderActivity, value => { _config.ActivityUserFilter = value; SaveConfig(); });
        _activitySourceFilter = new ComboBox { ItemsSource = ConfigStore.GiftSourceFilters, Width = 175, SelectedItem = _config.ActivitySourceFilter, VerticalContentAlignment = VerticalAlignment.Center };
        _activitySourceFilter.SelectionChanged += (_, _) =>
        {
            if (_activitySourceFilter.SelectedItem is string source)
            {
                _config.ActivitySourceFilter = source;
                SaveConfig();
                RenderActivity();
            }
        };
        _activityUserFilterField = CreateResponsiveField("Username", _activityUserFilter, 160);
        _activitySourceFilterField = CreateResponsiveField("Gifting type", _activitySourceFilter, 175);
        filters.Children.Add(_activityUserFilterField);
        filters.Children.Add(_activitySourceFilterField);
        _activityStatusFilterButton = MakeButton("Status: All  ▾", () =>
        {
            if (_activityStatusPopup is not null) _activityStatusPopup.IsOpen = !_activityStatusPopup.IsOpen;
        }, "#202B40");
        _activityStatusFilterButton.Height = 32;
        _activityStatusFilterButton.MinHeight = 0;
        _activityStatusFilterButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        filters.Children.Add(CreateResponsiveField("", _activityStatusFilterButton, 150));

        var statusOptions = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
        _activityStatusChecks.Clear();
        _activityStatusSelectAll = new CheckBox { Content = "(Select all)", IsThreeState = true, Foreground = Brush("#F4F6FF"), Margin = new Thickness(0, 0, 0, 6), FontWeight = FontWeights.SemiBold };
        _activityStatusSelectAll.Checked += (_, _) => SetActivityStatusesSelected(true);
        _activityStatusSelectAll.Unchecked += (_, _) => SetActivityStatusesSelected(false);
        statusOptions.Children.Add(_activityStatusSelectAll);
        statusOptions.Children.Add(new Border { Height = 1, Background = Brush("#26324A"), Margin = new Thickness(0, 0, 0, 6) });
        foreach (var status in ActivityStatusChoices)
        {
            var check = new CheckBox
            {
                Content = status,
                IsChecked = _config.ActivityStatusFilters.Contains(status, StringComparer.Ordinal),
                Foreground = Brush("#F4F6FF"),
                Margin = new Thickness(0, 3, 0, 3)
            };
            check.Checked += (_, _) => OnActivityStatusSelectionChanged();
            check.Unchecked += (_, _) => OnActivityStatusSelectionChanged();
            _activityStatusChecks[status] = check;
            statusOptions.Children.Add(check);
        }
        var initialSelectedCount = _activityStatusChecks.Values.Count(check => check.IsChecked == true);
        _activityStatusSelectAll.IsChecked = initialSelectedCount == _activityStatusChecks.Count
            ? true
            : initialSelectedCount == 0 ? false : null;
        _activityStatusPopup = new Popup
        {
            PlacementTarget = _activityStatusFilterButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = Brush("#151D30"), BorderBrush = Brush("#34415A"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), MinWidth = 205, Child = statusOptions
            }
        };
        panel.Children.Add(_activityStatusPopup);
        UpdateActivityStatusFilterButton();
        panel.Children.Add(filters);
        RefreshActivityNameSuggestions();
        _activityList = new StackPanel();
        var listScroll = new ScrollViewer { Content = _activityList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(listScroll, 1);
        panel.Children.Add(listScroll);
        return panel;
    }

    private static readonly string[] ActivityStatusChoices = ["Gifted", "Username not found", "Needs attention", "Queued", "Sending"];

    private void SetActivityStatusesSelected(bool selected)
    {
        if (_updatingActivityStatusChecks) return;
        _updatingActivityStatusChecks = true;
        foreach (var check in _activityStatusChecks.Values) check.IsChecked = selected;
        _updatingActivityStatusChecks = false;
        SaveSelectedActivityStatuses();
    }

    private void OnActivityStatusSelectionChanged()
    {
        if (_updatingActivityStatusChecks) return;
        _updatingActivityStatusChecks = true;
        var selectedCount = _activityStatusChecks.Values.Count(check => check.IsChecked == true);
        if (_activityStatusSelectAll is not null)
            _activityStatusSelectAll.IsChecked = selectedCount == _activityStatusChecks.Count
                ? true
                : selectedCount == 0 ? false : null;
        _updatingActivityStatusChecks = false;
        SaveSelectedActivityStatuses();
    }

    private void SaveSelectedActivityStatuses()
    {
        _config.ActivityStatusFilters = _activityStatusChecks
            .Where(pair => pair.Value.IsChecked == true)
            .Select(pair => pair.Key)
            .ToList();
        SaveConfig();
        UpdateActivityStatusFilterButton();
        RenderActivity();
    }

    private void UpdateActivityStatusFilterButton()
    {
        if (_activityStatusFilterButton is null) return;
        var selectedCount = _config.ActivityStatusFilters.Count;
        var caption = selectedCount == ActivityStatusChoices.Length ? "Status: All  ▾" : $"Status: {selectedCount} selected  ▾";
        _activityStatusFilterButton.Content = caption;
    }

    private void BuildDashboardGrid(string page) => BuildTreeDashboardGrid(page);

    private void BuildDashboardGrid_Legacy(string page)
    {
        _dashboardLeftColumn?.Children.Clear();
        _dashboardRightColumn?.Children.Clear();
        _dashboardLeftColumn = null;
        _dashboardRightColumn = null;
        _dashboardColumnSplitter = null;
        WorkspaceGrid.Children.Clear();
        WorkspaceGrid.RowDefinitions.Clear();
        WorkspaceGrid.ColumnDefinitions.Clear();
        var leftRatio = Math.Clamp(_config.DashboardColumnRatio, 0.25, 0.75);
        WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftRatio * 100, GridUnitType.Star) });
        WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((1 - leftRatio) * 100, GridUnitType.Star) });
        if (page == "dashboard")
        {
            var visible = _config.SectionOrder.Where(_config.DashboardSections.Contains).ToList();
            if (visible.Count == 0)
            {
                WorkspaceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var empty = Label("Your dashboard is empty. Choose Add widget/panel to place your first panel.");
                Grid.SetColumnSpan(empty, 3);
                WorkspaceGrid.Children.Add(empty);
                return;
            }
            _dashboardLeftColumn = new StackPanel();
            _dashboardRightColumn = new StackPanel();
            Grid.SetColumn(_dashboardRightColumn, 2);
            WorkspaceGrid.Children.Add(_dashboardLeftColumn);
            WorkspaceGrid.Children.Add(_dashboardRightColumn);
            _dashboardColumnSplitter = new GridSplitter
            {
                Width = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = Brush("#26324A"), ShowsPreview = false, Cursor = Cursors.SizeWE,
                IsEnabled = _isDashboardEditing,
                ToolTip = "Drag to resize the widget columns"
            };
            Grid.SetColumn(_dashboardColumnSplitter, 1);
            _dashboardColumnSplitter.DragCompleted += (_, _) =>
            {
                var total = WorkspaceGrid.ColumnDefinitions[0].ActualWidth + WorkspaceGrid.ColumnDefinitions[2].ActualWidth;
                if (total <= 0) return;
                _config.DashboardColumnRatio = Math.Clamp(WorkspaceGrid.ColumnDefinitions[0].ActualWidth / total, 0.25, 0.75);
                SaveConfig();
            };
            WorkspaceGrid.Children.Add(_dashboardColumnSplitter);
            for (var i = 0; i < visible.Count; i++)
            {
                var key = visible[i];
                var card = _cards[key];
                var collapsed = key == "bridge" && _config.CollapsedSections.Contains(key);
                SetCardCollapsedState(key, collapsed);
                _cardHeadings[key].Margin = new Thickness(0, 0, 0, collapsed ? 0 : 10);
                (i % 2 == 0 ? _dashboardLeftColumn : _dashboardRightColumn).Children.Add(card);
            }
        }
        else if (_cards.TryGetValue(page, out var selected))
        {
            WorkspaceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(selected, 0);
            Grid.SetColumn(selected, 0);
            Grid.SetColumnSpan(selected, 3);
            Grid.SetRowSpan(selected, 1);
            _cardBodies[page].Visibility = Visibility.Visible;
            _cardHeadings[page].Margin = new Thickness(0, 0, 0, 10);
            WorkspaceGrid.Children.Add(selected);
            UpdateCardActions(page);
        }
        else if (page == "settings")
        {
            WorkspaceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var settings = BuildSettingsPage();
            Grid.SetRow(settings, 0);
            Grid.SetColumn(settings, 0);
            Grid.SetColumnSpan(settings, 3);
            WorkspaceGrid.Children.Add(settings);
        }
    }

    private void BuildTreeDashboardGrid(string page)
    {
        foreach (var card in _cards.Values.ToArray()) DetachDashboardElement(card);
        WorkspaceGrid.Children.Clear();
        WorkspaceGrid.RowDefinitions.Clear();
        WorkspaceGrid.ColumnDefinitions.Clear();
        WorkspaceGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _dashboardSplitters.Clear();
        HideWidgetDropHints();

        if (page == "settings")
        {
            var settings = BuildSettingsPage();
            WorkspaceGrid.Children.Add(settings);
            return;
        }

        var root = _config.DashboardLayout;
        if (root is null)
        {
            var empty = new Border
            {
                Background = Brush("#151D30"), BorderBrush = Brush("#26324A"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16), Margin = new Thickness(7),
                Child = new TextBlock
                {
                    Text = _isDashboardEditing ? "Drop a widget here to fill the dashboard" : "Your dashboard is empty. Choose Edit layout to add a widget.",
                    Foreground = Brush("#9AA7C0"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetRow(empty, 0);
            Grid.SetColumn(empty, 0);
            WorkspaceGrid.Children.Add(empty);
            return;
        }

        var layout = BuildDashboardLayoutElement(root);
        Grid.SetRow(layout, 0);
        Grid.SetColumn(layout, 0);
        WorkspaceGrid.Children.Add(layout);
    }

    private UIElement BuildDashboardLayoutElement(DashboardLayoutNode node)
    {
        if (node.IsLeaf && node.WidgetKey is { } key && _cards.TryGetValue(key, out var card))
        {
            var collapsed = key == "bridge" && _config.CollapsedSections.Contains(key);
            SetCardCollapsedState(key, collapsed);
            card.Width = double.NaN;
            card.Height = double.NaN;
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Stretch;
            UpdateCardActions(key);
            return card;
        }

        if (node.First is null) return node.Second is null ? new Grid() : BuildDashboardLayoutElement(node.Second);
        if (node.Second is null) return BuildDashboardLayoutElement(node.First);

        var grid = new Grid { ClipToBounds = true };
        GridSplitter splitter;
        if (node.SplitAxis == "rows")
        {
            var firstIsCollapsedBridge = IsCollapsedBridgeLeaf(node.First);
            var secondIsCollapsedBridge = IsCollapsedBridgeLeaf(node.Second);
            grid.RowDefinitions.Add(new RowDefinition { Height = firstIsCollapsedBridge ? new GridLength(64) : new GridLength(node.Ratio, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = secondIsCollapsedBridge ? new GridLength(64) : new GridLength(1 - node.Ratio, GridUnitType.Star) });
            splitter = new GridSplitter
            {
                Height = 8, HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center, ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = Brushes.Transparent,
                ShowsPreview = false, Cursor = Cursors.SizeNS,
                IsEnabled = true, ToolTip = "Drag to resize these panels"
            };
            splitter.MouseEnter += (_, _) => splitter.Background = CreateSplitterHoverBrush(horizontalLine: true);
            splitter.MouseLeave += (_, _) => splitter.Background = Brushes.Transparent;
            var firstMinimum = GetDashboardMinimumSize(node.First);
            var secondMinimum = GetDashboardMinimumSize(node.Second);
            grid.MinWidth = Math.Max(firstMinimum.Width, secondMinimum.Width);
            grid.MinHeight = firstMinimum.Height + secondMinimum.Height + 8;
            grid.RowDefinitions[0].MinHeight = firstIsCollapsedBridge ? 64 : firstMinimum.Height;
            grid.RowDefinitions[2].MinHeight = secondIsCollapsedBridge ? 64 : secondMinimum.Height;
            var first = BuildDashboardLayoutElement(node.First);
            var second = BuildDashboardLayoutElement(node.Second);
            Grid.SetRow(first, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(second, 2);
            grid.Children.Add(first);
            grid.Children.Add(splitter);
            grid.Children.Add(second);
            splitter.DragCompleted += (_, _) =>
            {
                var total = grid.RowDefinitions[0].ActualHeight + grid.RowDefinitions[2].ActualHeight;
                if (total > 0)
                {
                    node.Ratio = Math.Clamp(grid.RowDefinitions[0].ActualHeight / total, 0.12, 0.88);
                    SaveConfig();
                }
            };
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(node.Ratio, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - node.Ratio, GridUnitType.Star) });
            splitter = new GridSplitter
            {
                Width = 8, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = Brushes.Transparent,
                ShowsPreview = false, Cursor = Cursors.SizeWE,
                IsEnabled = true, ToolTip = "Drag to resize these panels"
            };
            splitter.MouseEnter += (_, _) => splitter.Background = CreateSplitterHoverBrush(horizontalLine: false);
            splitter.MouseLeave += (_, _) => splitter.Background = Brushes.Transparent;
            var firstMinimum = GetDashboardMinimumSize(node.First);
            var secondMinimum = GetDashboardMinimumSize(node.Second);
            grid.MinWidth = firstMinimum.Width + secondMinimum.Width + 8;
            grid.MinHeight = Math.Max(firstMinimum.Height, secondMinimum.Height);
            grid.ColumnDefinitions[0].MinWidth = firstMinimum.Width;
            grid.ColumnDefinitions[2].MinWidth = secondMinimum.Width;
            var first = BuildDashboardLayoutElement(node.First);
            var second = BuildDashboardLayoutElement(node.Second);
            Grid.SetColumn(first, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(second, 2);
            grid.Children.Add(first);
            grid.Children.Add(splitter);
            grid.Children.Add(second);
            splitter.DragCompleted += (_, _) =>
            {
                var total = grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[2].ActualWidth;
                if (total > 0)
                {
                    node.Ratio = Math.Clamp(grid.ColumnDefinitions[0].ActualWidth / total, 0.12, 0.88);
                    SaveConfig();
                }
            };
        }
        _dashboardSplitters.Add(splitter);
        return grid;
    }

    private static void DetachDashboardElement(UIElement element)
    {
        var parent = VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element);
        switch (parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Decorator decorator: decorator.Child = null; break;
            case ContentControl content: content.Content = null; break;
        }
    }

    private void HandleWidgetDragOver(string targetKey, Border target, DragEventArgs args)
    {
        if (!args.Data.GetDataPresent(DataFormats.StringFormat)) return;
        if (args.Data.GetData(DataFormats.StringFormat) is not string incoming || !ConfigStore.Sections.Contains(incoming)) return;
        if (incoming == targetKey)
        {
            if (_activeWidgetDragKey == incoming)
            {
                args.Effects = DragDropEffects.Move;
                args.Handled = true;
                ScheduleWidgetDragLayoutRemoval(incoming);
            }
            return;
        }
        var visibleKeys = FlattenDashboardLayout(_config.DashboardLayout);
        var isExistingWidget = visibleKeys.Contains(incoming, StringComparer.Ordinal) || _activeWidgetDragKey == incoming;
        if (!_isDashboardEditing && !isExistingWidget)
        {
            args.Effects = DragDropEffects.None;
            args.Handled = true;
            return;
        }
        var side = GetDashboardDropSide(target, args.GetPosition(target));
        ShowWidgetDropHint(targetKey, target, side);
        args.Effects = isExistingWidget ? DragDropEffects.Move : DragDropEffects.Copy;
        args.Handled = true;
        if (isExistingWidget) ScheduleWidgetDragLayoutRemoval(incoming);
    }

    private void HandleWidgetDrop(string targetKey, Border target, DragEventArgs args)
    {
        if (args.Data.GetData(DataFormats.StringFormat) is not string incoming || incoming == targetKey) return;
        var isExistingWidget = FlattenDashboardLayout(_config.DashboardLayout).Contains(incoming, StringComparer.Ordinal) || _activeWidgetDragKey == incoming;
        if (!_isDashboardEditing && !isExistingWidget) return;
        var side = GetDashboardDropSide(target, args.GetPosition(target));
        HideWidgetDropHints();
        args.Handled = true;
        if (_activeWidgetDragKey == incoming)
        {
            _pendingWidgetDropTarget = targetKey;
            _pendingWidgetDropSide = side;
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyWidgetDrop(targetKey, incoming, side)), DispatcherPriority.Background);
        }
    }

    private void WidgetRemoveDropZone_DragOver(object sender, DragEventArgs args)
    {
        var isDashboardWidget = _isDashboardEditing &&
            args.Data.GetData(DataFormats.StringFormat) is string key &&
            (FlattenDashboardLayout(_config.DashboardLayout).Contains(key, StringComparer.Ordinal) || _activeWidgetDragKey == key);
        args.Effects = isDashboardWidget ? DragDropEffects.Move : DragDropEffects.None;
        if (ReferenceEquals(sender, WidgetRemoveDropZone))
            WidgetRemoveDropZone.Background = isDashboardWidget ? Brush("#492B36") : Brush("#0E1525");
        else
            WidgetEditorPanel.BorderBrush = isDashboardWidget ? Brush("#B34A61") : Brush("#26324A");
        args.Handled = true;
        if (isDashboardWidget && args.Data.GetData(DataFormats.StringFormat) is string movingKey)
            ScheduleWidgetDragLayoutRemoval(movingKey);
    }

    private void WidgetRemoveDropZone_DragLeave(object sender, DragEventArgs args)
    {
        if (ReferenceEquals(sender, WidgetRemoveDropZone))
            WidgetRemoveDropZone.Background = Brush("#0E1525");
        else
            WidgetEditorPanel.BorderBrush = Brush("#26324A");
    }

    private void WidgetRemoveDropZone_Drop(object sender, DragEventArgs args)
    {
        if (args.Data.GetData(DataFormats.StringFormat) is not string key) return;
        var isActiveDrag = _activeWidgetDragKey == key;
        if (!isActiveDrag && !FlattenDashboardLayout(_config.DashboardLayout).Contains(key, StringComparer.Ordinal)) return;
        args.Handled = true;
        WidgetRemoveDropZone.Background = Brush("#0E1525");
        WidgetEditorPanel.BorderBrush = Brush("#26324A");
        if (isActiveDrag)
            _pendingWidgetDragRemoval = true;
        else
            Dispatcher.BeginInvoke(new Action(() => RemoveFromDashboard(key)), DispatcherPriority.Background);
    }

    private void ShowWidgetDropHint(string targetKey, Border target, DashboardDropSide side)
    {
        HideWidgetDropHints();
        if (!_widgetDropHints.TryGetValue(targetKey, out var hint)) return;
        var name = side.ToString();
        ((TextBlock)hint.Child).Text = $"Drop {name.ToLowerInvariant()} to split";
        hint.Margin = side switch
        {
            DashboardDropSide.Left => new Thickness(4, 4, target.ActualWidth * 0.52, 4),
            DashboardDropSide.Right => new Thickness(target.ActualWidth * 0.52, 4, 4, 4),
            DashboardDropSide.Top => new Thickness(4, 4, 4, target.ActualHeight * 0.52),
            _ => new Thickness(4, target.ActualHeight * 0.52, 4, 4)
        };
        hint.Visibility = Visibility.Visible;
    }

    private void HideWidgetDropHints()
    {
        foreach (var hint in _widgetDropHints.Values) hint.Visibility = Visibility.Collapsed;
    }

    private void ApplyWidgetDrop(string targetKey, string incomingKey, DashboardDropSide side)
    {
        var currentKeys = FlattenDashboardLayout(_config.DashboardLayout);
        var movingExistingWidget = currentKeys.Contains(incomingKey, StringComparer.Ordinal);
        var activeDragSource = _activeWidgetDragKey == incomingKey;
        if ((!_isDashboardEditing && !movingExistingWidget && !activeDragSource) || !ConfigStore.Sections.Contains(incomingKey) || incomingKey == targetKey) return;
        var layout = RemoveWidgetFromLayout(_config.DashboardLayout, incomingKey);
        if (layout is null && !FlattenDashboardLayout(_config.DashboardLayout).Contains(targetKey, StringComparer.Ordinal))
            targetKey = FlattenDashboardLayout(_config.DashboardLayout).FirstOrDefault() ?? "";
        if (layout is null && string.IsNullOrEmpty(targetKey))
        {
            _config.DashboardLayout = new DashboardLayoutNode { WidgetKey = incomingKey };
        }
        else
        {
            var axis = side is DashboardDropSide.Left or DashboardDropSide.Right ? "columns" : "rows";
            var before = side is DashboardDropSide.Left or DashboardDropSide.Top;
            _config.DashboardLayout = InsertWidgetBeside(layout, targetKey, incomingKey, axis, before);
        }
        SyncDashboardSectionsFromLayout();
        RefreshWidgetEditorLists();
        BuildDashboardGrid("dashboard");
        if (!_isDashboardEditing) SaveConfig();
    }

    private void ScheduleWidgetDragLayoutRemoval(string key)
    {
        if (_activeWidgetDragKey != key || _widgetDragLayoutDetached || _widgetDragRemovalQueued) return;
        _widgetDragRemovalQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _widgetDragRemovalQueued = false;
            if (_activeWidgetDragKey != key || _widgetDragLayoutDetached || _pendingWidgetDropTarget is not null || _pendingWidgetDragRemoval) return;
            _config.DashboardLayout = RemoveWidgetFromLayout(_config.DashboardLayout, key);
            _widgetDragLayoutDetached = true;
            BuildDashboardGrid("dashboard");
        }), DispatcherPriority.Background);
    }

    private Size GetDashboardMinimumSize(DashboardLayoutNode? node)
    {
        if (node is null) return new Size(0, 0);
        if (node.IsLeaf)
        {
            if (node.WidgetKey == "bridge") return _config.CollapsedSections.Contains("bridge") ? new Size(260, 64) : new Size(300, 250);
            return new Size(260, 210);
        }
        var first = GetDashboardMinimumSize(node.First);
        var second = GetDashboardMinimumSize(node.Second);
        return node.SplitAxis == "rows"
            ? new Size(Math.Max(first.Width, second.Width), first.Height + second.Height + 8)
            : new Size(first.Width + second.Width + 8, Math.Max(first.Height, second.Height));
    }

    private bool IsCollapsedBridgeLeaf(DashboardLayoutNode? node) =>
        node is { IsLeaf: true, WidgetKey: "bridge" } && _config.CollapsedSections.Contains("bridge");

    private static Brush CreateSplitterHoverBrush(bool horizontalLine)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = horizontalLine ? new Point(0, 1) : new Point(1, 0)
        };
        var clear = Color.FromArgb(0, 101, 120, 255);
        var accent = Color.FromArgb(235, 101, 120, 255);
        brush.GradientStops.Add(new GradientStop(clear, 0));
        brush.GradientStops.Add(new GradientStop(clear, 0.38));
        brush.GradientStops.Add(new GradientStop(accent, 0.5));
        brush.GradientStops.Add(new GradientStop(clear, 0.62));
        brush.GradientStops.Add(new GradientStop(clear, 1));
        return brush;
    }

    private void WorkspaceGrid_DragOver(object sender, DragEventArgs args)
    {
        if (!args.Data.GetDataPresent(DataFormats.StringFormat) ||
            args.Data.GetData(DataFormats.StringFormat) is not string incoming || !ConfigStore.Sections.Contains(incoming))
        {
            args.Effects = DragDropEffects.None;
            args.Handled = true;
            return;
        }

        var isExistingWidget = FlattenDashboardLayout(_config.DashboardLayout).Contains(incoming, StringComparer.Ordinal) || _activeWidgetDragKey == incoming;
        if (!_isDashboardEditing && !isExistingWidget)
        {
            args.Effects = DragDropEffects.None;
            args.Handled = true;
            return;
        }

        // Let the widget's own PreviewDragOver handler choose the edge and show
        // its split target. The workspace only claims drags over empty space.
        var point = args.GetPosition(WorkspaceGrid);
        var hit = WorkspaceGrid.InputHitTest(point) as DependencyObject;
        while (hit is not null)
        {
            if (hit is Border border && border.Tag is string key && _cards.TryGetValue(key, out var card) && ReferenceEquals(card, border))
                return;
            hit = VisualTreeHelper.GetParent(hit) ?? LogicalTreeHelper.GetParent(hit);
        }

        args.Effects = isExistingWidget ? DragDropEffects.Move : DragDropEffects.Copy;
        args.Handled = true;
    }

    private void WorkspaceGrid_Drop(object sender, DragEventArgs args)
    {
        if (!_isDashboardEditing || args.Data.GetData(DataFormats.StringFormat) is not string incoming || !ConfigStore.Sections.Contains(incoming)) return;
        args.Handled = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var leaves = FlattenDashboardLayout(_config.DashboardLayout);
            if (leaves.Count == 0)
                _config.DashboardLayout = new DashboardLayoutNode { WidgetKey = incoming };
            else if (!leaves.Contains(incoming, StringComparer.Ordinal))
                _config.DashboardLayout = InsertWidgetBeside(_config.DashboardLayout, leaves[0], incoming, "columns", before: false);
            SyncDashboardSectionsFromLayout();
            RefreshWidgetEditorLists();
            BuildDashboardGrid("dashboard");
        }), DispatcherPriority.Background);
    }

    private enum DashboardDropSide { Left, Right, Top, Bottom }

    private void WorkspaceGrid_Drop_Legacy(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat)) return;
        var key = e.Data.GetData(DataFormats.StringFormat) as string;
        if (string.IsNullOrWhiteSpace(key) || !_cards.ContainsKey(key)) return;
        if (!_config.DashboardSections.Contains(key))
        {
            AddToDashboard(key);
            ShowPage("dashboard");
            return;
        }
        if (_activePage != "dashboard") { ShowPage("dashboard"); return; }
        if (_dashboardDragOriginalOrder is null) return;
        _dashboardDragDropped = true;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void WorkspaceGrid_DragOver_Legacy(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        var key = e.Data.GetData(DataFormats.StringFormat) as string;
        if (string.IsNullOrWhiteSpace(key) || !_cards.ContainsKey(key))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        var isDashboardCard = _activePage == "dashboard" && _config.DashboardSections.Contains(key);
        e.Effects = isDashboardCard ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
        if (isDashboardCard && _dashboardDragOriginalOrder is not null)
        {
            var point = e.GetPosition(WorkspaceGrid);
            _dashboardDragAdorner?.Move(point);
            PreviewDashboardReorder(key, point);
        }
    }

    private void PreviewDashboardReorder(string draggedKey, Point point)
    {
        if (_activePage != "dashboard" || _dashboardDragBasePositions is null || _dashboardDragPreviewOrder is null || !_config.DashboardSections.Contains(draggedKey)) return;
        var target = _cards.Where(pair => pair.Key != draggedKey &&
                (_dashboardLeftColumn?.Children.Contains(pair.Value) == true || _dashboardRightColumn?.Children.Contains(pair.Value) == true))
            .FirstOrDefault(pair =>
            {
                var origin = _dashboardDragBasePositions.TryGetValue(pair.Key, out var initialPosition)
                    ? initialPosition
                    : pair.Value.TranslatePoint(new Point(0, 0), WorkspaceGrid);
                return new Rect(origin, new Size(pair.Value.ActualWidth, pair.Value.ActualHeight)).Contains(point);
            });
        if (target.Value is null) return;

        var targetOrigin = _dashboardDragBasePositions.TryGetValue(target.Key, out var targetInitialPosition)
            ? targetInitialPosition
            : target.Value.TranslatePoint(new Point(0, 0), WorkspaceGrid);
        var targetHeight = Math.Max(1, target.Value.ActualHeight);
        var verticalPosition = (point.Y - targetOrigin.Y) / targetHeight;
        if (verticalPosition is > 0.35 and < 0.65) return;

        // Derive every preview from the starting arrangement so moving cards
        // cannot slide under the pointer and become a new target.
        var visibleOrder = _dashboardDragOriginalOrder!
            .Where(_config.DashboardSections.Contains)
            .ToList();
        visibleOrder.Remove(draggedKey);
        var targetIndex = visibleOrder.IndexOf(target.Key);
        if (targetIndex < 0) return;
        var insertIndex = targetIndex + (verticalPosition >= 0.65 ? 1 : 0);
        visibleOrder.Insert(Math.Clamp(insertIndex, 0, visibleOrder.Count), draggedKey);
        if (_dashboardDragPreviewOrder.SequenceEqual(visibleOrder)) return;
        _dashboardDragPreviewOrder = visibleOrder;
        AnimateDashboardPreview(visibleOrder);
    }

    private Point GetDashboardDragPosition(string key, Border card)
    {
        if (_dashboardDragBasePositions is null || !_dashboardDragBasePositions.TryGetValue(key, out var basePosition))
            return card.TranslatePoint(new Point(0, 0), WorkspaceGrid);
        if (card.RenderTransform is TranslateTransform transform)
            return new Point(basePosition.X + transform.X, basePosition.Y + transform.Y);
        return basePosition;
    }

    private Dictionary<string, Point> CaptureDashboardCardPositions()
    {
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        foreach (var (key, card) in _cards)
        {
            if (_dashboardLeftColumn?.Children.Contains(card) != true && _dashboardRightColumn?.Children.Contains(card) != true) continue;
            positions[key] = card.TranslatePoint(new Point(0, 0), WorkspaceGrid);
        }
        return positions;
    }

    private void AnimateDashboardReflow(Dictionary<string, Point> oldPositions)
    {
        foreach (var (key, card) in _cards)
        {
            if (!oldPositions.TryGetValue(key, out var oldPosition)) continue;
            var newPosition = card.TranslatePoint(new Point(0, 0), WorkspaceGrid);
            var offsetX = oldPosition.X - newPosition.X;
            var offsetY = oldPosition.Y - newPosition.Y;
            if (Math.Abs(offsetX) < 1 && Math.Abs(offsetY) < 1) continue;

            var transform = new TranslateTransform(offsetX, offsetY);
            card.RenderTransform = transform;
            var duration = TimeSpan.FromMilliseconds(170);
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    private void AnimateDashboardPreview(IReadOnlyList<string> previewOrder)
    {
        if (_dashboardDragBasePositions is null || _dashboardDragColumnStarts is null) return;
        var nextY = new[] { _dashboardDragColumnStarts[0].Y, _dashboardDragColumnStarts[1].Y };
        var previousCard = new Border?[2];
        var targetPositions = new Dictionary<string, Point>(StringComparer.Ordinal);
        for (var index = 0; index < previewOrder.Count; index++)
        {
            var key = previewOrder[index];
            var column = index % 2;
            var card = _cards[key];
            if (previousCard[column] is { } previous)
                nextY[column] += previous.ActualHeight + previous.Margin.Bottom + card.Margin.Top;
            targetPositions[key] = new Point(_dashboardDragColumnStarts[column].X, nextY[column]);
            previousCard[column] = card;
        }

        foreach (var (key, targetPosition) in targetPositions)
        {
            var card = _cards[key];
            var basePosition = _dashboardDragBasePositions[key];
            var targetX = targetPosition.X - basePosition.X;
            var targetY = targetPosition.Y - basePosition.Y;
            var transform = card.RenderTransform as TranslateTransform ?? new TranslateTransform();
            card.RenderTransform = transform;
            var duration = TimeSpan.FromMilliseconds(170);
            transform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(transform.X, targetX, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            transform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(transform.Y, targetY, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    private List<string> BuildSectionOrder(IReadOnlyList<string> visibleOrder)
    {
        var visibleIndex = 0;
        return _config.SectionOrder.Select(section => _config.DashboardSections.Contains(section)
            ? visibleOrder[visibleIndex++] : section).ToList();
    }

    private void ResetDashboardCardTransforms()
    {
        foreach (var card in _cards.Values)
        {
            card.RenderTransform = Transform.Identity;
            card.BeginAnimation(UIElement.RenderTransformProperty, null);
        }
    }

    private void BeginDashboardCardDrag(string key)
    {
        if (_config.DashboardLayoutLocked) return;
        if (!_cards.TryGetValue(key, out var card) || card.ActualWidth < 2 || card.ActualHeight < 2) return;
        ResetDashboardCardTransforms();
        _dashboardDragOriginalOrder = _config.SectionOrder.ToList();
        _dashboardDragPreviewOrder = _config.SectionOrder.Where(_config.DashboardSections.Contains).ToList();
        _dashboardDragDropped = false;
        _dashboardDraggedCard = card;
        _dashboardDragBasePositions = CaptureDashboardCardPositions();
        var leftTop = _dashboardLeftColumn?.Children.OfType<Border>().FirstOrDefault();
        var rightTop = _dashboardRightColumn?.Children.OfType<Border>().FirstOrDefault();
        var leftPoint = leftTop is null ? _dashboardLeftColumn!.TranslatePoint(new Point(0, 0), WorkspaceGrid) : _dashboardDragBasePositions[leftTop.Tag!.ToString()!];
        var rightPoint = rightTop is null ? _dashboardRightColumn!.TranslatePoint(new Point(0, 0), WorkspaceGrid) : _dashboardDragBasePositions[rightTop.Tag!.ToString()!];
        _dashboardDragColumnStarts = [leftPoint, rightPoint];
        var layer = AdornerLayer.GetAdornerLayer(WorkspaceGrid);
        if (layer is null) return;

        var dpi = VisualTreeHelper.GetDpi(card);
        var width = Math.Max(1, (int)Math.Ceiling(card.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(card.ActualHeight * dpi.DpiScaleY));
        var snapshot = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        snapshot.Render(card);
        snapshot.Freeze();
        _dashboardDragAdorner = new DashboardDragAdorner(WorkspaceGrid, snapshot, card.ActualWidth, card.ActualHeight);
        _dashboardAdornerLayer = layer;
        layer.Add(_dashboardDragAdorner);
        card.Opacity = 0.28;
    }

    private void EndDashboardCardDrag(bool cancelled)
    {
        if (_dashboardDragOriginalOrder is not null)
        {
            var oldVisualPositions = _dashboardDragBasePositions is null
                ? new Dictionary<string, Point>(StringComparer.Ordinal)
                : _cards.Where(pair => _dashboardDragBasePositions.ContainsKey(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => GetDashboardDragPosition(pair.Key, pair.Value), StringComparer.Ordinal);
            var shouldCommit = !cancelled && _dashboardDragDropped && _dashboardDragPreviewOrder is not null;
            var finalOrder = shouldCommit ? BuildSectionOrder(_dashboardDragPreviewOrder!) : _dashboardDragOriginalOrder;
            _config.SectionOrder.Clear();
            _config.SectionOrder.AddRange(finalOrder);
            ResetDashboardCardTransforms();
            BuildDashboardGrid("dashboard");
            WorkspaceGrid.UpdateLayout();
            AnimateDashboardReflow(oldVisualPositions);
            if (shouldCommit) SaveConfig();
        }
        if (_dashboardAdornerLayer is not null && _dashboardDragAdorner is not null)
            _dashboardAdornerLayer.Remove(_dashboardDragAdorner);
        if (_dashboardDraggedCard is not null) _dashboardDraggedCard.Opacity = 1;
        _dashboardAdornerLayer = null;
        _dashboardDragAdorner = null;
        _dashboardDraggedCard = null;
        _dashboardDragOriginalOrder = null;
        _dashboardDragPreviewOrder = null;
        _dashboardDragBasePositions = null;
        _dashboardDragColumnStarts = null;
        _dashboardDragDropped = false;
    }

    private void AttachDragSource(UIElement source, string key, bool dashboardOnly)
    {
        Point? dragStart = null;
        source.PreviewMouseLeftButtonDown += (_, args) =>
        {
            dragStart = IsDragExcluded(args.OriginalSource as DependencyObject, source)
                ? null
                : args.GetPosition(source);
        };
        source.PreviewMouseLeftButtonUp += (_, _) => dragStart = null;
        source.PreviewMouseMove += (_, args) =>
        {
            if (args.LeftButton != MouseButtonState.Pressed || dragStart is not Point start ||
                !dashboardOnly || _activePage != "dashboard") return;
            var current = args.GetPosition(source);
            if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragStart = null;
            var card = _cards[key];
            _activeWidgetDragKey = key;
            _layoutBeforeWidgetDrag = CloneDashboardLayout(_config.DashboardLayout);
            _pendingWidgetDropTarget = null;
            _pendingWidgetDropSide = null;
            _pendingWidgetDragRemoval = false;
            _widgetDragLayoutDetached = false;
            _widgetDragRemovalQueued = false;
            card.Opacity = 0.45;
            try
            {
                DragDrop.DoDragDrop(source, new DataObject(DataFormats.StringFormat, key), DragDropEffects.Move);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                card.Opacity = 1;
                if (_activeWidgetDragKey == key)
                {
                    if (_pendingWidgetDropTarget is { } targetKey && _pendingWidgetDropSide is { } side)
                    {
                        ApplyWidgetDrop(targetKey, key, side);
                    }
                    else if (_pendingWidgetDragRemoval)
                    {
                        _config.DashboardLayout = RemoveWidgetFromLayout(_config.DashboardLayout, key);
                        SyncDashboardSectionsFromLayout();
                        RefreshWidgetEditorLists();
                        BuildDashboardGrid("dashboard");
                        if (!_isDashboardEditing) SaveConfig();
                    }
                    else
                    {
                        _config.DashboardLayout = CloneDashboardLayout(_layoutBeforeWidgetDrag);
                        SyncDashboardSectionsFromLayout();
                        RefreshWidgetEditorLists();
                        BuildDashboardGrid("dashboard");
                    }
                    _activeWidgetDragKey = null;
                    _layoutBeforeWidgetDrag = null;
                    _pendingWidgetDropTarget = null;
                    _pendingWidgetDropSide = null;
                    _pendingWidgetDragRemoval = false;
                    _widgetDragLayoutDetached = false;
                    _widgetDragRemovalQueued = false;
                }
            }
        };
    }

    private static bool IsDragExcluded(DependencyObject? source, UIElement dragSurface)
    {
        while (source is not null && !ReferenceEquals(source, dragSurface))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase) return true;
            source = VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void ShowPage(string page)
    {
        _activePage = page == "settings" ? "settings" : "dashboard";
        var isSettings = _activePage == "settings";
        if (isSettings && _isDashboardEditing) CancelDashboardEdit();
        SettingsSearchHost.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;
        SettingsBackButton.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;
        AddWidgetButton.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
        WidgetEditorPanel.Visibility = Visibility.Collapsed;
        WidgetEditorColumn.Width = new GridLength(0);
        HeaderTitle.Text = isSettings ? "Settings" : "Dashboard";
        foreach (var key in _cards.Keys) UpdateCardActions(key);
        BuildDashboardGrid(_activePage);
    }

    private void UpdateCardActions(string key)
    {
        if (!_addButtons.TryGetValue(key, out var add) || !_removeButtons.TryGetValue(key, out var remove) || !_collapseButtons.TryGetValue(key, out var collapse)) return;
        var onDashboard = FlattenDashboardLayout(_config.DashboardLayout).Contains(key, StringComparer.Ordinal);
        collapse.Content = CollapseGlyph(_config.CollapsedSections.Contains(key));
        if (_activePage == "dashboard")
        {
            add.Visibility = Visibility.Collapsed;
            remove.Visibility = onDashboard && _isDashboardEditing ? Visibility.Visible : Visibility.Collapsed;
            collapse.Visibility = key == "bridge" && onDashboard ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            add.Content = onDashboard ? "Added" : "Add";
            add.IsEnabled = !onDashboard;
            add.Visibility = Visibility.Visible;
            remove.Visibility = onDashboard ? Visibility.Visible : Visibility.Collapsed;
            collapse.Visibility = Visibility.Collapsed;
        }
    }

    private void AddToDashboard(string key)
    {
        var existing = FlattenDashboardLayout(_config.DashboardLayout);
        if (existing.Contains(key, StringComparer.Ordinal)) return;
        _config.DashboardLayout = _config.DashboardLayout is null
            ? new DashboardLayoutNode { WidgetKey = key }
            : InsertWidgetBeside(_config.DashboardLayout, existing[0], key, "columns", before: false);
        SyncDashboardSectionsFromLayout();
        RefreshWidgetEditorLists();
        BuildDashboardGrid("dashboard");
    }

    private void RemoveFromDashboard(string key)
    {
        _config.DashboardLayout = RemoveWidgetFromLayout(_config.DashboardLayout, key);
        SyncDashboardSectionsFromLayout();
        RefreshWidgetEditorLists();
        BuildDashboardGrid(_activePage);
        UpdateCardActions(key);
    }

    private void ToggleCollapse(string key)
    {
        var collapsed = !_config.CollapsedSections.Contains(key);
        if (collapsed) _config.CollapsedSections.Add(key);
        else _config.CollapsedSections.Remove(key);
        SetCardCollapsedState(key, collapsed);
        SaveConfig();
        BuildDashboardGrid("dashboard");
    }

    private void SetCardCollapsedState(string key, bool collapsed)
    {
        if (key == "free_chest" && _freeChestExpandedBody is not null && _freeChestCompactBody is not null)
        {
            _cardBodies[key].Visibility = Visibility.Visible;
            _freeChestExpandedBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            _freeChestCompactBody.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            UpdateFreeChestCompactControls();
        }
        else
        {
            _cardBodies[key].Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        if (key == "bridge" && _cards.TryGetValue(key, out var bridgeCard))
        {
            bridgeCard.MinWidth = 300;
            bridgeCard.MinHeight = collapsed ? 64 : 250;
        }
        _collapseButtons[key].Content = CollapseGlyph(collapsed);
    }

    private async Task RefreshRewardsAsync()
    {
        if (_twitch.User is null) return;
        try
        {
            ConnectionStatus.Text = "Loading rewards…";
            _lastRewards = await _twitch.GetRewardsAsync(CancellationToken.None);
            RenderRewards(_lastRewards);
            ConnectionStatus.Text = $"Connected to {_twitch.User.DisplayName}";
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = "Could not load rewards";
            MessageBox.Show(this, ex.Message, "Twitch rewards", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RenderRewards(IReadOnlyList<TwitchReward> rewards)
    {
        if (_rewardContainer is null) return;
        _rewardContainer.Children.Clear();
        _mappingInputs.Clear();
        _packInputs.Clear();
        _cardCountInputs.Clear();
        _rewardEnabledControls.Clear();
        _rewardHideControls.Clear();
        _rewardMappingFields.Clear();
        _rewardChestFields.Clear();
        _rewardCardFields.Clear();
        var showHidden = _showHiddenRewards?.IsChecked == true;
        var visible = rewards.Where(r => (r.IsEnabled || (_showInactive?.IsChecked == true)) && (showHidden || !_config.HiddenRewardIds.Contains(r.Id, StringComparer.Ordinal))).ToArray();
        if (visible.Length == 0)
            _rewardContainer.Children.Add(Label(rewards.Count > 0 && _config.HiddenRewardIds.Count > 0 ? "No rewards to show. Enable Hidden Rewards to restore hidden items." : "No active rewards found."));
        foreach (var reward in visible)
        {
            var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            var info = new StackPanel { Margin = new Thickness(8, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            var option = _config.RewardGiftOptions.GetValueOrDefault(reward.Id) ?? new RewardGiftOption();
            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            titleRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var isHidden = _config.HiddenRewardIds.Contains(reward.Id, StringComparer.Ordinal);
            var hide = MakeButton(isHidden ? "Show" : "Hide", () =>
            {
                if (_config.HiddenRewardIds.Contains(reward.Id, StringComparer.Ordinal)) _config.HiddenRewardIds.Remove(reward.Id);
                else _config.HiddenRewardIds.Add(reward.Id);
                SaveConfig();
                RenderRewards(_lastRewards);
            }, isHidden ? "#174331" : "#492B36");
            hide.Foreground = isHidden ? Brush("#52DB91") : Brush("#FF8585");
            hide.Width = 48;
            hide.Height = 22;
            hide.VerticalAlignment = VerticalAlignment.Center;
            hide.MinHeight = 0;
            hide.Padding = new Thickness(2, 0, 2, 0);
            hide.Margin = new Thickness(4, 0, 0, 0);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(hide);
            var enabledToggle = CreateSwitch("", option.Enabled, enabled =>
            {
                option.Enabled = enabled;
                _config.RewardGiftOptions[reward.Id] = option;
                SaveConfig();
            });
            enabledToggle.ToolTip = "Enable or disable automatic gifting for this redeem";
            var title = new TextBlock
            {
                Text = reward.Title,
                Foreground = Brush("#DCE3F5"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = reward.Title,
                Margin = new Thickness(0, 0, 4, 0)
            };
            titleRow.Children.Add(title);
            Grid.SetRow(title, 0);
            Grid.SetColumn(title, 0);
            titleRow.Children.Add(actions);
            Grid.SetRowSpan(actions, 2);
            Grid.SetColumn(actions, 1);
            info.Children.Add(titleRow);
            var pointStatus = new TextBlock
            {
                Text = $"{reward.Cost:N0} points · {(reward.IsEnabled ? "Active" : "Inactive")}",
                Foreground = Brush(reward.IsEnabled ? "#35C979" : "#E05252"),
                FontSize = 10,
                Margin = new Thickness(0, 1, 0, 0),
                TextWrapping = TextWrapping.NoWrap
            };
            titleRow.Children.Add(pointStatus);
            Grid.SetRow(pointStatus, 1);
            Grid.SetColumn(pointStatus, 0);
            var enabledField = CreateResponsiveField("", enabledToggle, 40);
            row.Children.Add(enabledField);
            row.Children.Add(CreateResponsiveField("Reward", info, 180));
            _rewardEnabledControls[reward.Id] = enabledField;
            _rewardHideControls[reward.Id] = hide;
            var input = new TextBox { Text = _config.Mappings.GetValueOrDefault(reward.Id, ""), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Streamloots collection" };
            var mappingField = CreateResponsiveField("Collection", input, 138);
            row.Children.Add(mappingField);
            _mappingInputs[reward.Id] = input;
            _rewardMappingFields[reward.Id] = mappingField;
            var packs = new TextBox { Text = option.Packs.ToString(), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Chests to gift (1–50)" };
            var chestField = CreateResponsiveField("Chests", packs, 52);
            row.Children.Add(chestField);
            _packInputs[reward.Id] = packs;
            _rewardChestFields[reward.Id] = chestField;
            var cards = new ComboBox { ItemsSource = new[] { 1, 2, 3 }, SelectedItem = option.Cards, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Cards per chest" };
            var cardField = CreateResponsiveField("Cards", cards, 60);
            row.Children.Add(cardField);
            _cardCountInputs[reward.Id] = cards;
            _rewardCardFields[reward.Id] = cardField;
            _rewardContainer.Children.Add(new Border
            {
                Background = Brush("#10182A"),
                BorderBrush = Brush("#26324A"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(2),
                Margin = new Thickness(2, 3, 2, 3),
                Child = row
            });
        }
    }

    private void SaveMappings()
    {
        var packValues = new Dictionary<string, int>();
        foreach (var (rewardId, input) in _packInputs)
        {
            if (!int.TryParse(input.Text, out var packs) || packs is < 1 or > 50)
            {
                ConnectionStatus.Text = "Chest amounts must be between 1 and 50.";
                input.Focus();
                return;
            }
            packValues[rewardId] = packs;
        }
        foreach (var (rewardId, input) in _mappingInputs)
        {
            var collection = input.Text.Trim();
            if (collection.Length == 0) _config.Mappings.Remove(rewardId);
            else _config.Mappings[rewardId] = collection;
            var currentOption = _config.RewardGiftOptions.GetValueOrDefault(rewardId);
            _config.RewardGiftOptions[rewardId] = new RewardGiftOption
            {
                Packs = packValues[rewardId],
                Cards = (int)(_cardCountInputs.GetValueOrDefault(rewardId)?.SelectedItem ?? 3),
                Enabled = currentOption?.Enabled ?? false
            };
        }
        SaveConfig();
        ConnectionStatus.Text = "Reward gift settings saved";
    }

    private void AddFreeChestRule()
    {
        var keyword = _keywordInput?.Text.Trim() ?? "";
        var collection = _collectionInput?.Text.Trim() ?? "";
        if (keyword.Length == 0 || collection.Length == 0 || !int.TryParse(_packsInput?.Text, out var packs) || packs is < 1 or > 50)
        {
            if (_freeChestStatus is not null) _freeChestStatus.Text = "Enter a keyword, collection, and chest count from 1 to 50.";
            return;
        }
        if (_config.FreeChestRules.Any(r => !ReferenceEquals(r, _editingFreeChestRule) && r.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            if (_freeChestStatus is not null) _freeChestStatus.Text = "That keyword is already configured.";
            return;
        }
        if (_editingFreeChestRule is null)
        {
            _config.FreeChestRules.Add(new FreeChestRule { Keyword = keyword, Collection = collection, Packs = packs, Cards = (int)(_cardsInput?.SelectedItem ?? 3) });
        }
        else
        {
            _editingFreeChestRule.Keyword = keyword;
            _editingFreeChestRule.Collection = collection;
            _editingFreeChestRule.Packs = packs;
            _editingFreeChestRule.Cards = (int)(_cardsInput?.SelectedItem ?? 3);
        }
        _editingFreeChestRule = null;
        _keywordInput!.Clear();
        _collectionInput!.Clear();
        _packsInput!.Text = "1";
        _addFreeChestRuleButton!.Content = "Add keyword";
        _cancelFreeChestRuleEditButton!.Visibility = Visibility.Collapsed;
        SaveConfig();
        RefreshActiveFreeChestRules();
        RenderFreeChestRules();
        UpdateFreeChestCompactControls();
    }

    private void BeginFreeChestRuleEdit(FreeChestRule rule)
    {
        _editingFreeChestRule = rule;
        _keywordInput!.Text = rule.Keyword;
        _collectionInput!.Text = rule.Collection;
        _packsInput!.Text = rule.Packs.ToString();
        _cardsInput!.SelectedItem = rule.Cards;
        _addFreeChestRuleButton!.Content = "Save changes";
        _cancelFreeChestRuleEditButton!.Visibility = Visibility.Visible;
        _keywordInput.Focus();
    }

    private void CancelFreeChestRuleEdit()
    {
        _editingFreeChestRule = null;
        _keywordInput?.Clear();
        _collectionInput?.Clear();
        if (_packsInput is not null) _packsInput.Text = "1";
        _cardsInput?.SetCurrentValue(ComboBox.SelectedItemProperty, 3);
        if (_addFreeChestRuleButton is not null) _addFreeChestRuleButton.Content = "Add keyword";
        if (_cancelFreeChestRuleEditButton is not null) _cancelFreeChestRuleEditButton.Visibility = Visibility.Collapsed;
    }

    private void RemoveFreeChestRule(FreeChestRule rule)
    {
        if (ReferenceEquals(rule, _editingFreeChestRule)) CancelFreeChestRuleEdit();
        _config.FreeChestRules.Remove(rule);
        SaveConfig();
        RefreshActiveFreeChestRules();
        RenderFreeChestRules();
        UpdateFreeChestCompactControls();
    }

    private void RefreshActiveFreeChestRules()
    {
        if (_freeChestUntil.HasValue)
            _activeRules = _config.FreeChestRules.Where(rule => rule.Enabled).Select(CloneRule).ToArray();
    }

    private void RenderFreeChestRules(bool includeGuideExample = false)
    {
        if (_freeChestRulesContainer is null) return;
        var parent = _freeChestRulesContainer;
        parent.Children.Clear();
        _freeChestRuleSwitch = null;
        _freeChestRuleEditRemoveActions = null;
        var rules = _config.FreeChestRules.ToList();
        FreeChestRule? guideExample = null;
        if (includeGuideExample && rules.Count == 0)
        {
            guideExample = new FreeChestRule { Keyword = "s1", Collection = "How to Fish", Packs = 1, Cards = 3, Enabled = true };
            rules.Add(guideExample);
        }
        if (rules.Count == 0)
        {
            var empty = Label("No keywords yet. Add one above (for example s1).", false, "#8491AA");
            parent.Children.Add(empty);
        }
        foreach (var rule in rules)
        {
            var isGuideExample = ReferenceEquals(rule, guideExample);
            var content = new DockPanel();
            var row = new Border { Background = Brush("#10182A"), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 4, 7, 4), Margin = new Thickness(0, 3, 0, 3), Child = content };
            var remove = MakeButton("Remove", () => RemoveFreeChestRule(rule), "#392833");
            remove.Padding = new Thickness(8, 3, 8, 3);
            remove.MinHeight = 0;
            remove.Height = 25;
            remove.Margin = new Thickness(8, 2, 0, 2);
            remove.IsEnabled = !isGuideExample;
            var edit = MakeButton("Edit", () => BeginFreeChestRuleEdit(rule), "#25324A");
            edit.Width = 48;
            edit.Padding = new Thickness(5, 3, 5, 3);
            edit.MinHeight = 0;
            edit.Height = 25;
            edit.Margin = new Thickness(6, 2, 0, 2);
            edit.IsEnabled = !isGuideExample;
            var ruleActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ruleActions.Children.Add(edit);
            ruleActions.Children.Add(remove);
            remove.Margin = new Thickness(8, 2, 0, 2);
            DockPanel.SetDock(ruleActions, Dock.Right);
            content.Children.Add(ruleActions);
            _freeChestRuleEditRemoveActions ??= ruleActions;
            var enabledToggle = CreateSwitch("", rule.Enabled, enabled =>
            {
                rule.Enabled = enabled;
                SaveConfig();
                RefreshActiveFreeChestRules();
                UpdateFreeChestCompactControls();
            });
            enabledToggle.ToolTip = rule.Enabled ? "Keyword is enabled" : "Keyword is disabled";
            enabledToggle.Margin = new Thickness(0, 0, 10, 0);
            enabledToggle.IsEnabled = !isGuideExample;
            DockPanel.SetDock(enabledToggle, Dock.Left);
            content.Children.Add(enabledToggle);
            _freeChestRuleSwitch ??= enabledToggle;
            var summary = new TextBlock
            {
                Text = $"{(isGuideExample ? "Example · " : "")}{rule.Keyword}  →  {rule.Collection}  ·  {rule.Packs} {ChestWord(rule.Packs)} × {rule.Cards} cards",
                Foreground = Brush(rule.Enabled ? "#DCE3F5" : "#8491AA"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"{rule.Keyword} → {rule.Collection} · {rule.Packs} {ChestWord(rule.Packs)} × {rule.Cards} cards"
            };
            content.Children.Add(summary);
            parent.Children.Add(row);
        }
    }

    private void StartFreeChestHour()
    {
        if (!_twitch.IsChatReady)
        {
            if (_freeChestStatus is not null) _freeChestStatus.Text = "Wait for the Twitch chat connection before starting the timer.";
            return;
        }
        if (!int.TryParse(_freeChestDurationInput?.Text, out var durationMinutes) || durationMinutes is < 1 or > 60)
        {
            if (_freeChestStatus is not null) _freeChestStatus.Text = "Duration must be between 1 and 60 minutes.";
            return;
        }
        var enabledRules = _config.FreeChestRules.Where(rule => rule.Enabled).Select(CloneRule).ToArray();
        if (_twitch.User is null || enabledRules.Length == 0)
        {
            if (_freeChestStatus is not null) _freeChestStatus.Text = _twitch.User is null ? "Connect Twitch first to listen for chat keywords." : "Enable at least one keyword before starting.";
            return;
        }
        _config.FreeChestDurationMinutes = durationMinutes;
        SaveConfig();
        _activeWindowId = Guid.NewGuid().ToString("N");
        _freeChestUntil = DateTimeOffset.UtcNow.AddMinutes(durationMinutes);
        _activeRules = enabledRules;
        _claimedViewers.Clear();
        _freeChestStatus!.Text = "Free Chest Hour is live · one claim per viewer";
        _freeChestStatus.Foreground = Brush("#35C979");
        _freeChestCountdown!.Visibility = Visibility.Visible;
        _startFreeChestButton!.IsEnabled = false;
        _cancelFreeChestButton!.IsEnabled = true;
        UpdateFreeChestCompactControls();
        _freeChestTimer.Start();
        UpdateFreeChestTimer();
    }

    private void StopFreeChestHour(string status)
    {
        var wasActive = _freeChestUntil.HasValue;
        _freeChestUntil = null;
        _activeWindowId = null;
        _activeRules = [];
        _claimedViewers.Clear();
        _freeChestTimer.Stop();
        if (_freeChestStatus is not null)
        {
            _freeChestStatus.Text = wasActive ? status : "Free Chest Hour is inactive.";
            _freeChestStatus.Foreground = Brush("#A6B2C8");
        }
        if (_freeChestCountdown is not null)
        {
            _freeChestCountdown.Text = "00:00";
            _freeChestCountdown.Visibility = Visibility.Collapsed;
        }
        if (_startFreeChestButton is not null) _startFreeChestButton.IsEnabled = true;
        if (_cancelFreeChestButton is not null) _cancelFreeChestButton.IsEnabled = false;
        if (_freeChestCompactCountdown is not null)
        {
            _freeChestCompactCountdown.Text = "00:00";
            _freeChestCompactCountdown.Visibility = Visibility.Collapsed;
        }
        UpdateFreeChestCompactControls();
    }

    private void UpdateFreeChestTimer()
    {
        if (_freeChestCountdown is null) return;
        if (_freeChestUntil is null) return;
        var remaining = _freeChestUntil.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) { StopFreeChestHour("Free Chest Hour ended."); return; }
        var timeText = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        _freeChestCountdown.Text = timeText;
        if (_freeChestCompactCountdown is not null) _freeChestCompactCountdown.Text = timeText;
    }

    private void UpdateFreeChestCompactControls()
    {
        var isActive = _freeChestUntil.HasValue;
        var canStart = !isActive && _twitch.IsChatReady && _config.FreeChestRules.Any(rule => rule.Enabled);
        if (_startFreeChestButton is not null) _startFreeChestButton.IsEnabled = canStart;
        if (_compactStartFreeChestButton is not null) _compactStartFreeChestButton.IsEnabled = canStart;
        if (_cancelFreeChestButton is not null) _cancelFreeChestButton.IsEnabled = isActive;
        if (_compactStopFreeChestButton is not null) _compactStopFreeChestButton.IsEnabled = isActive;
        if (_freeChestCompactCountdown is not null)
            _freeChestCompactCountdown.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HandleRedemption(RedemptionEvent redemption)
    {
        if (!_config.RewardGiftOptions.TryGetValue(redemption.RewardId, out var configuredOption) || !configuredOption.Enabled)
            return;
        var collection = _config.Mappings.GetValueOrDefault(redemption.RewardId, "");
        var option = configuredOption;
        var enteredRecipient = _config.RequireStreamlootsUsername
            ? redemption.UserInput.Trim()
            : string.IsNullOrWhiteSpace(redemption.UserLogin) ? redemption.UserName.Trim() : redemption.UserLogin.Trim();
        var recipient = ResolveViewerAlias(enteredRecipient);
        var request = new GiftRequest(redemption.Id, redemption.UserName, recipient, collection, redemption.RewardTitle, Packs: option.Packs, Cards: option.Cards);
        QueueRequest(request, redemption.UserName, redemption.RewardTitle, recipient);
    }

    private void HandleChatMessage(ChatMessageEvent message)
    {
        if (_freeChestChatActivity is not null)
            _freeChestChatActivity.Text = $"Last chat message received · {DateTime.Now:HH:mm:ss}";
        if (_freeChestUntil is null || DateTimeOffset.UtcNow >= _freeChestUntil || string.IsNullOrWhiteSpace(message.ChatterId) || string.IsNullOrWhiteSpace(message.Login)) return;
        var keyword = message.Text.Trim();
        var rule = _activeRules.FirstOrDefault(r => r.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
        if (rule is null || !_claimedViewers.Add(message.ChatterId)) return;
        var request = new GiftRequest(
            $"free-chest:{_activeWindowId}:{message.ChatterId}",
            message.DisplayName,
            message.Login,
            rule.Collection,
            $"Free Chest Hour · {rule.Keyword}",
            "free_chest_hour",
            rule.Keyword,
            rule.Packs,
            rule.Cards);
        QueueRequest(request, message.DisplayName, request.RewardName, message.Login);
    }

    private (string Status, string Detail) QueueRequest(GiftRequest request, string viewer, string reward, string recipient)
    {
        var (status, detail) = _giftQueue.Enqueue(request);
        if (detail == "This redemption was already queued.") return (status, detail);
        var activity = new GiftActivity
        {
            Viewer = viewer,
            Reward = reward,
            Recipient = recipient,
            Collection = request.Collection,
            Source = request.Source,
            Keyword = request.Keyword,
            Packs = request.Packs,
            Cards = request.Cards,
            Status = status == "pending" ? "Gift queued for Streamloots" : $"Gift not queued: {detail}",
            Detail = detail
        };
        activity.JobId = _giftQueue.History().FirstOrDefault(job => job.RedemptionId == request.RedemptionId)?.JobId ?? "";
        _activityById[request.RedemptionId] = activity;
        _activity.Insert(0, activity);
        if (_activity.Count > 200)
        {
            var removed = _activity[^1];
            _activity.RemoveAt(_activity.Count - 1);
            var removedId = _activityById.FirstOrDefault(pair => ReferenceEquals(pair.Value, removed)).Key;
            if (removedId is not null) _activityById.Remove(removedId);
        }
        RefreshHistoryNameSuggestions();
        RefreshActivityNameSuggestions();
        RenderActivity();
        RenderHistory();
        return (status, detail);
    }

    private void OnGiftUpdated(QueueJob job)
    {
        if (_activityById.TryGetValue(job.RedemptionId, out var activity))
        {
            activity.JobId = job.JobId;
            activity.Status = job.Status switch
            {
                "sent" => "Gift sent automatically",
                "not_found" => "Streamloots username not found",
                "pending" => "Gift queued for Streamloots",
                "processing" => "Sending gift…",
                _ => "Gift needs attention"
            };
            activity.Detail = job.Detail;
            RenderActivity();
        }
        RenderHistory();
    }

    private void RenderHistory()
    {
        if (_historyList is null) return;
        _historyList.Children.Clear();
        _historyFirstRow = null;
        var selected = _historyFilter?.SelectedItem as string ?? _config.HistoryFilter;
        var sourceFilter = _historySourceFilter?.SelectedItem as string ?? _config.ClaimHistorySourceFilter;
        var username = (_historyUserFilter?.Text ?? _config.ClaimHistoryUserFilter).Trim();
        var jobs = _giftQueue.History();
        var statuses = selected switch
        {
            "Gifted" => new[] { "sent" },
            "Username not found" => new[] { "not_found" },
            "Needs attention" => new[] { "needs_attention" },
            "Queued" => new[] { "pending" },
            "Sending" => new[] { "processing" },
            _ => Array.Empty<string>()
        };
        if (statuses.Length > 0) jobs = jobs.Where(j => statuses.Contains(j.Status)).ToArray();
        if (sourceFilter != "All") jobs = jobs.Where(j => j.Source == SourceCode(sourceFilter)).ToArray();
        if (username.Length > 0)
            jobs = jobs.Where(j => j.Viewer.Contains(username, StringComparison.OrdinalIgnoreCase) || j.Recipient.Contains(username, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (jobs.Count == 0)
        {
            var empty = Label("No claims match this filter yet.", false, "#8491AA");
            empty.HorizontalAlignment = HorizontalAlignment.Left;
            _historyList.Children.Add(empty);
            _historyFirstRow = empty;
            return;
        }
        foreach (var job in jobs)
        {
            var content = new StackPanel();
            var row = new Border { Background = Brush("#10182A"), BorderBrush = Brush("#26324A"), BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 6, 9, 6), Margin = new Thickness(0, 4, 0, 4), Child = content };
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var status = Label(HistoryStatus(job.Status), true, HistoryColor(job.Status));
            DockPanel.SetDock(status, Dock.Right);
            top.Children.Add(status);
            top.Children.Add(Label($"{job.Viewer}  ·  {GiftSourceLabel(job.Source)}", true));
            content.Children.Add(top);
            content.Children.Add(Label(
                $"Recipient: {(string.IsNullOrWhiteSpace(job.Recipient) ? "Not provided" : job.Recipient)}",
                false,
                "#A5B0C6"));
            var details = Label($"{job.Collection}  ·  {job.Packs} {ChestWord(job.Packs)} × {job.Cards} cards", false, "#A5B0C6");
            details.Margin = new Thickness(0, 0, 0, 1);
            content.Children.Add(details);
            var footer = new DockPanel { Margin = new Thickness(0, 2, 0, 0), LastChildFill = true };
            if (job.Status == "needs_attention")
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var markSent = MakeButton("Mark gifted", () => MarkGiftAsSent(job), "#174331");
                markSent.Foreground = Brush("#52DB91");
                markSent.Margin = new Thickness(6, 2, 0, 2);
                markSent.Padding = new Thickness(8, 5, 8, 5);
                markSent.ToolTip = "Use after confirming the gift was delivered in Streamloots.";
                actions.Children.Add(markSent);
                var retry = MakeButton("Retry", () => RetryGift(job), "#202B40");
                retry.Foreground = Brush("#52DB91");
                retry.Margin = new Thickness(6, 2, 0, 2);
                retry.Padding = new Thickness(8, 5, 8, 5);
                retry.ToolTip = "Queue another attempt if the gift was not delivered.";
                actions.Children.Add(retry);
                DockPanel.SetDock(actions, Dock.Right);
                footer.Children.Add(actions);
            }
            var historyTime = job.Source == "streamloots_card" && !string.IsNullOrWhiteSpace(job.PlayedAt)
                ? $"Card played · {FormatCardPlayedTime(job.PlayedAt)}"
                : FormatTime(job.CreatedAt);
            footer.Children.Add(Label(historyTime, false, "#7F8BA4"));
            content.Children.Add(footer);
            _historyList.Children.Add(row);
            _historyFirstRow ??= row;
        }
    }

    private void OpenHistoryCleanupWindow()
    {
        if (_historyCleanupWindow is { IsVisible: true } openWindow)
        {
            openWindow.Activate();
            return;
        }

        var cleanupWindow = new Window
        {
            Title = "Claim history cleanup",
            Width = 560,
            Height = 470,
            MinWidth = 460,
            MinHeight = 420,
            Owner = this,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0B1020")
        };
        ConfigureBorderlessWindow(cleanupWindow, captionHeight: 48, resizable: true);
        _historyCleanupWindow = cleanupWindow;
        cleanupWindow.Closed += (_, _) => _historyCleanupWindow = null;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var frame = new Border
        {
            Background = Brush("#151D30"),
            BorderBrush = Brush("#25324A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            Child = layout
        };

        var titleBar = new Border
        {
            Background = Brush("#25324A"),
            CornerRadius = new CornerRadius(13, 13, 0, 0),
            Height = 48,
            Child = new DockPanel { LastChildFill = true, Margin = new Thickness(14, 0, 10, 0) }
        };
        var titleDock = (DockPanel)titleBar.Child;
        var close = MakeButton("×", cleanupWindow.Close, "#492B36");
        close.Width = 32;
        close.Height = 30;
        close.MinHeight = 0;
        close.Padding = new Thickness(0);
        close.Foreground = Brush("#FF8585");
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        DockPanel.SetDock(close, Dock.Right);
        titleDock.Children.Add(close);
        titleDock.Children.Add(new TextBlock
        {
            Text = "Claim history cleanup",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#F4F6FF"),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetRow(titleBar, 0);
        layout.Children.Add(titleBar);

        var username = CreateUsernameTextFilter("", null, () => _historyNameSuggestions, () => { }, _ => { });
        username.ToolTip = "Matches viewer or recipient username; unique names autocomplete inline when they fit";
        var source = new ComboBox
        {
            ItemsSource = ConfigStore.GiftSourceFilters,
            SelectedIndex = 0,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var status = new ComboBox
        {
            ItemsSource = ConfigStore.HistoryFilters,
            SelectedIndex = 0,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var age = new ComboBox
        {
            ItemsSource = new[] { "Any age", "Older than 7 days", "Older than 30 days", "Older than 90 days", "Older than 6 months", "Older than 1 year" },
            SelectedIndex = 0,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var filters = new Grid { Margin = new Thickness(18, 16, 18, 10) };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCleanupField(filters, "Username", username, 0, 0);
        AddCleanupField(filters, "Gifting type", source, 1, 0);
        AddCleanupField(filters, "Status", status, 0, 1);
        AddCleanupField(filters, "Age", age, 1, 1);
        var helperText = Label("Choose any combination. All and Any age leave that filter open. Clearing history also removes queued or sending entries.", false, "#9AA7C0");
        helperText.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(helperText, 2);
        Grid.SetColumnSpan(helperText, 2);
        filters.Children.Add(helperText);
        var scroll = new ScrollViewer
        {
            Content = filters,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        var footer = new StackPanel { Margin = new Thickness(18, 8, 18, 16) };
        var footerStatus = Label("", false, "#9AA7C0");
        footerStatus.Margin = new Thickness(0, 0, 0, 8);
        footer.Children.Add(footerStatus);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var clearButton = MakeButton("Clear entire history", () =>
        {
            var count = _giftQueue.History().Count;
            if (count == 0)
            {
                footerStatus.Text = "Claim history is already empty.";
                return;
            }
            if (!ShowDashboardConfirmation(cleanupWindow, "Clear entire history",
                    $"Permanently delete all {count} saved claim entries? This includes queued and sending gifts, which may be discarded. This cannot be undone.",
                    "Clear history")) return;
            var removed = _giftQueue.DeleteHistory();
            RenderHistory();
            RefreshHistoryNameSuggestions();
            cleanupWindow.Close();
            ShowDashboardConfirmation(this, "History cleared", $"Deleted {removed} claim record(s).", "Done", affirmativeColor: "#174331", confirmOnly: true);
        }, "#492B36");
        clearButton.Foreground = Brush("#FF8585");
        clearButton.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(clearButton);
        var deleteButton = MakeButton("Delete matching", () =>
        {
            var usernameFilter = username.Text.Trim();
            var sourceFilter = source.SelectedItem as string ?? "All";
            var statusFilter = status.SelectedItem as string ?? "All";
            var ageFilter = age.SelectedItem as string ?? "Any age";
            var sourceCode = sourceFilter == "All" ? null : SourceCode(sourceFilter);
            var statusCode = CleanupHistoryStatusCode(statusFilter);
            var cutoff = CleanupHistoryCutoff(ageFilter);
            var matches = _giftQueue.History().Where(job =>
                (usernameFilter.Length == 0 || job.Viewer.Contains(usernameFilter, StringComparison.OrdinalIgnoreCase) || job.Recipient.Contains(usernameFilter, StringComparison.OrdinalIgnoreCase)) &&
                (sourceCode is null || job.Source == sourceCode) &&
                (statusCode is null || job.Status == statusCode) &&
                (cutoff is null || (DateTimeOffset.TryParse(job.CreatedAt, out var created) && created < cutoff.Value))).ToArray();
            if (matches.Length == 0)
            {
                footerStatus.Text = "No claim entries match those filters.";
                return;
            }
            if (!ShowDashboardConfirmation(cleanupWindow, "Delete matching claims",
                    $"Permanently delete these {matches.Length} claim record(s)? Matching queued or sending gifts will be discarded. This cannot be undone.",
                    "Delete claims")) return;
            var removed = _giftQueue.DeleteHistory(usernameFilter, sourceCode, statusCode, cutoff);
            RenderHistory();
            RefreshHistoryNameSuggestions();
            footerStatus.Text = $"Deleted {removed} claim record(s).";
        }, "#5D6EFF");
        deleteButton.MinWidth = 130;
        buttons.Children.Add(deleteButton);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        cleanupWindow.Content = frame;
        cleanupWindow.Show();
    }

    private static void AddCleanupField(Grid grid, string caption, FrameworkElement control, int column, int row)
    {
        var field = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 8, 0, column == 0 ? 8 : 0, 12) };
        field.Children.Add(Label(caption, true, "#9AA7C0"));
        field.Children.Add(control);
        Grid.SetColumn(field, column);
        Grid.SetRow(field, row);
        grid.Children.Add(field);
    }

    private bool ShowDashboardConfirmation(Window owner, string title, string message, string confirmText, string affirmativeColor = "#492B36", bool confirmOnly = false)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            Height = confirmOnly ? 210 : 260,
            MinWidth = 390,
            MinHeight = confirmOnly ? 190 : 230,
            Owner = owner,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0B1020")
        };
        ConfigureBorderlessWindow(dialog, captionHeight: 48, resizable: false);
        var rows = new Grid();
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var border = new Border { Background = Brush("#151D30"), BorderBrush = Brush("#26324A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Child = rows };
        var header = new Border { Background = Brush("#25324A"), CornerRadius = new CornerRadius(13, 13, 0, 0), Padding = new Thickness(16, 0, 12, 0) };
        var headerDock = new DockPanel { LastChildFill = true };
        var close = MakeButton("×", () => dialog.DialogResult = false, "#492B36");
        close.Width = 30;
        close.Height = 28;
        close.MinHeight = 0;
        close.Padding = new Thickness(0);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        DockPanel.SetDock(close, Dock.Right);
        headerDock.Children.Add(close);
        headerDock.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#F4F6FF"), VerticalAlignment = VerticalAlignment.Center });
        header.Child = headerDock;
        rows.Children.Add(header);
        var body = Label(message, false, "#DCE3F5");
        body.Margin = new Thickness(18, 16, 18, 12);
        Grid.SetRow(body, 1);
        rows.Children.Add(body);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 16) };
        if (!confirmOnly)
        {
            var cancel = MakeButton("Cancel", () => dialog.DialogResult = false, "#202B40");
            cancel.Width = 82;
            cancel.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(cancel);
        }
        var confirm = MakeButton(confirmText, () => dialog.DialogResult = true, affirmativeColor);
        confirm.MinWidth = 88;
        actions.Children.Add(confirm);
        Grid.SetRow(actions, 2);
        rows.Children.Add(actions);
        dialog.Content = border;
        return dialog.ShowDialog() == true;
    }

    private static string? CleanupHistoryStatusCode(string label) => label switch
    {
        "Gifted" => "sent",
        "Username not found" => "not_found",
        "Needs attention" => "needs_attention",
        "Queued" => "pending",
        "Sending" => "processing",
        _ => null
    };

    private static void ConfigureBorderlessWindow(Window window, double captionHeight, bool resizable)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = captionHeight,
            ResizeBorderThickness = resizable ? new Thickness(8) : new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            UseAeroCaptionButtons = false
        });
    }

    private static DateTimeOffset? CleanupHistoryCutoff(string label) => label switch
    {
        "Older than 7 days" => DateTimeOffset.Now.AddDays(-7),
        "Older than 30 days" => DateTimeOffset.Now.AddDays(-30),
        "Older than 90 days" => DateTimeOffset.Now.AddDays(-90),
        "Older than 6 months" => DateTimeOffset.Now.AddMonths(-6),
        "Older than 1 year" => DateTimeOffset.Now.AddYears(-1),
        _ => null
    };

    private void RefreshHistoryNameSuggestions()
    {
        _historyNameSuggestions = _giftQueue.History()
            .SelectMany(job => new[] { job.Viewer, job.Recipient })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void RefreshActivityNameSuggestions()
    {
        _activityNameSuggestions = _activity.SelectMany(item => new[] { item.Viewer, item.Recipient })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static StackPanel CreateResponsiveField(string caption, FrameworkElement control, double controlWidth)
    {
        control.Width = controlWidth;
        control.Margin = new Thickness(0);
        var field = new StackPanel
        {
            Width = controlWidth + 8,
            Margin = new Thickness(0, 0, 0, 6),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        if (string.IsNullOrWhiteSpace(caption))
        {
            field.Children.Add(new Border { Height = 15, Background = Brushes.Transparent });
        }
        else
        {
            field.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#8491AA"),
                Margin = new Thickness(5, 0, 0, 3),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        field.Children.Add(control);
        return field;
    }

    private static void AddFilterCaption(Grid grid, string caption, int column, double leftMargin = 0)
    {
        var label = new TextBlock
        {
            Text = caption,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#8491AA"),
            Margin = new Thickness(leftMargin, 0, 0, 3)
        };
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private TextBox CreateUsernameTextFilter(string initialText, double? width, Func<IReadOnlyList<string>> suggestions, Action onChanged, Action<string> onCommit)
    {
        var textBox = new TextBox
        {
            Text = initialText,
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Type a username; a unique match autocompletes inline when it fits"
        };
        if (width is double fixedWidth) textBox.Width = fixedWidth;
        textBox.TextChanged += (_, _) =>
        {
            if (_updatingUserSuggestions) return;
            var typed = textBox.Text;
            if (typed.Length > 0)
            {
                var matches = suggestions()
                    .Where(name => name.Length > typed.Length && name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToArray();
                if (matches.Length == 1 && CanFitUsernameCompletion(textBox, matches[0]))
                {
                    _updatingUserSuggestions = true;
                    textBox.Text = matches[0];
                    textBox.Select(typed.Length, matches[0].Length - typed.Length);
                    _updatingUserSuggestions = false;
                }
            }
            onChanged();
        };
        textBox.LostFocus += (_, _) => onCommit(textBox.Text.Trim());
        return textBox;
    }

    private static bool CanFitUsernameCompletion(TextBox textBox, string text)
    {
        if (textBox.ActualWidth <= 0) return false;
        var dpi = VisualTreeHelper.GetDpi(textBox).PixelsPerDip;
        var measured = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch),
            textBox.FontSize,
            textBox.Foreground,
            dpi);
        return measured.Width <= textBox.ActualWidth - 26;
    }

    private void RetryGift(QueueJob job)
    {
        var retried = _giftQueue.Retry(job.JobId);
        if (retried is null)
        {
            MessageBox.Show(this, "This gift is no longer waiting for attention.", "Retry gift", MessageBoxButton.OK, MessageBoxImage.Information);
            RenderHistory();
            return;
        }
        RenderHistory();
    }

    private void MarkGiftAsSent(QueueJob job)
    {
        var marked = _giftQueue.MarkAsSent(job.JobId);
        if (marked is null)
        {
            MessageBox.Show(this, "This gift is no longer waiting for attention.", "Mark gifted", MessageBoxButton.OK, MessageBoxImage.Information);
            RenderHistory();
            return;
        }
        RenderHistory();
    }

    private void RenderActivity()
    {
        if (_activityList is null) return;
        _activityList.Children.Clear();
        if (_activity.Count == 0) { _activityList.Children.Add(Label("No Twitch redeems received this session yet.", false, "#8491AA")); return; }
        var sourceFilter = _activitySourceFilter?.SelectedItem as string ?? _config.ActivitySourceFilter;
        var username = (_activityUserFilter?.Text ?? _config.ActivityUserFilter).Trim();
        var selectedStatuses = _config.ActivityStatusFilters;
        var entries = _activity.Where(item =>
            selectedStatuses.Contains(ActivityStatusLabel(item.Status), StringComparer.Ordinal) &&
            (sourceFilter == "All" || item.Source == SourceCode(sourceFilter)) &&
            (username.Length == 0 || item.Viewer.Contains(username, StringComparison.OrdinalIgnoreCase) || item.Recipient.Contains(username, StringComparison.OrdinalIgnoreCase)))
            .Take(100).ToArray();
        if (entries.Length == 0) { _activityList.Children.Add(Label("No activity matches these filters.", false, "#8491AA")); return; }
        foreach (var item in entries)
        {
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = new Border { Background = Brush("#10182A"), BorderBrush = Brush("#26324A"), BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 3, 0, 3), Child = content };
            var failed = IsActivityFailure(item.Status);
            var linesAndAction = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var textLines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var headline = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 0, 2) };
            headline.Inlines.Add(new Run($"{item.Viewer}  ·  {GiftSourceLabel(item.Source)}  ·  "));
            headline.Inlines.Add(new Run(ActivityStatusLabel(item.Status)) { Foreground = Brush(ActivityStatusColor(item.Status)) });
            headline.VerticalAlignment = VerticalAlignment.Center;
            textLines.Children.Add(headline);
            var reward = Label(item.Reward, false, "#A5B0C6");
            reward.Margin = new Thickness(0);
            textLines.Children.Add(reward);
            linesAndAction.Children.Add(textLines);
            var actionButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (item.Status.Contains("attention", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.JobId))
            {
                var retry = MakeButton("Retry", () => RetryActivityGift(item), "#174331");
                retry.Foreground = Brush("#52DB91");
                retry.Height = 28;
                retry.MinHeight = 0;
                retry.Padding = new Thickness(9, 2, 9, 2);
                retry.Margin = new Thickness(8, 0, 0, 0);
                retry.ToolTip = "Try sending this queued gift again";
                actionButtons.Children.Add(retry);
            }
            var again = MakeButton("↻", () => RepeatActivityGift(item), "#202B40");
            again.Width = 28;
            again.Height = 28;
            again.MinHeight = 0;
            again.Padding = new Thickness(0);
            again.Content = new TextBlock
            {
                Text = "↻", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            again.FontWeight = FontWeights.Bold;
            again.VerticalAlignment = VerticalAlignment.Center;
            again.Margin = new Thickness(8, 0, 0, 0);
            again.ToolTip = "Gift again";
            actionButtons.Children.Add(again);
            linesAndAction.Children.Add(actionButtons);
            Grid.SetRow(linesAndAction, 0);
            content.Children.Add(linesAndAction);
            if (failed)
            {
                var error = string.IsNullOrWhiteSpace(item.Detail) ? item.Status : item.Detail;
                var errorLabel = Label(error, false, "#FF8585");
                errorLabel.Margin = new Thickness(0, 2, 0, 0);
                Grid.SetRow(errorLabel, 1);
                content.Children.Add(errorLabel);
            }
            _activityList.Children.Add(row);
        }
    }

    private static bool IsActivityFailure(string status) =>
        status.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("not queued", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("attention", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("failed", StringComparison.OrdinalIgnoreCase);

    private static string ActivityStatusLabel(string status)
    {
        if (status.Contains("sent automatically", StringComparison.OrdinalIgnoreCase)) return "Gifted";
        if (status.Contains("not found", StringComparison.OrdinalIgnoreCase)) return "Username not found";
        if (status.Contains("queued", StringComparison.OrdinalIgnoreCase)) return "Queued";
        if (status.Contains("sending", StringComparison.OrdinalIgnoreCase)) return "Sending";
        if (status.Contains("attention", StringComparison.OrdinalIgnoreCase) || status.Contains("not queued", StringComparison.OrdinalIgnoreCase)) return "Needs attention";
        return status;
    }

    private static string ActivityStatusColor(string status)
    {
        if (status.Contains("sent automatically", StringComparison.OrdinalIgnoreCase)) return "#35C979";
        if (status.Contains("not found", StringComparison.OrdinalIgnoreCase) || status.Contains("failed", StringComparison.OrdinalIgnoreCase)) return "#FF8585";
        if (IsActivityFailure(status)) return "#FFC45C";
        return "#91A7FF";
    }

    private void RepeatActivityGift(GiftActivity item)
    {
        var request = new GiftRequest(
            $"manual-repeat:{Guid.NewGuid():N}",
            item.Viewer,
            item.Recipient,
            item.Collection,
            item.Reward,
            item.Source,
            item.Keyword,
            item.Packs,
            item.Cards);
        QueueRequest(request, item.Viewer, item.Reward, item.Recipient);
    }

    private void RetryActivityGift(GiftActivity item)
    {
        var job = _giftQueue.History().FirstOrDefault(candidate => candidate.JobId == item.JobId);
        if (job is null)
        {
            MessageBox.Show(this, "This queued gift is no longer available to retry.", "Retry gift", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        RetryGift(job);
    }

    private void SaveConfig()
    {
        try { _configStore.Save(_config); }
        catch (IOException ex) { MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void HistoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_historyFilter?.SelectedItem is string selected)
        {
            _config.HistoryFilter = selected;
            SaveConfig();
            RenderHistory();
        }
    }

    private static string SourceCode(string filter) => filter switch
    {
        "Channel point redeem" => "channel_points",
        "Free Chest Hour" => "free_chest_hour",
        "Streamloots card" => "streamloots_card",
        _ => ""
    };

    private static ToggleButton CreateSwitch(string label, bool initialState, Action<bool>? onChanged = null)
    {
        var toggle = new ToggleButton
        {
            IsChecked = initialState,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FocusVisualStyle = null,
            Template = new ControlTemplate(typeof(ToggleButton))
            {
                VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
            }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrWhiteSpace(label))
        {
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brush("#DCE3F5"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        var track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(2), Margin = new Thickness(0) };
        var thumb = new Ellipse { Width = 14, Height = 14, Fill = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var switchGrid = new Grid { Width = 40, Height = 22, Margin = new Thickness(string.IsNullOrWhiteSpace(label) ? 0 : 10, 0, 0, 0) };
        switchGrid.Children.Add(track);
        switchGrid.Children.Add(thumb);
        row.Children.Add(switchGrid);
        toggle.Content = row;
        void UpdateSwitch(bool isOn)
        {
            track.Background = Brush(isOn ? "#5D6EFF" : "#323B50");
            track.BorderBrush = Brush(isOn ? "#AAB4FF" : "#66738C");
            thumb.HorizontalAlignment = isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            thumb.Margin = new Thickness(isOn ? 0 : 4, 0, isOn ? 4 : 0, 0);
        }
        UpdateSwitch(initialState);
        toggle.Checked += (_, _) => { UpdateSwitch(true); onChanged?.Invoke(true); };
        toggle.Unchecked += (_, _) => { UpdateSwitch(false); onChanged?.Invoke(false); };
        return toggle;
    }

    private static TextBlock Label(string text, bool bold = false, string? color = null) => new()
    {
        Text = text,
        Foreground = Brush(color ?? "#DCE3F5"),
        FontSize = bold ? 12 : 11,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 3)
    };

    private static FrameworkElement CloseGlyph(string color)
    {
        var glyph = new Grid { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.Children.Add(new Line { X1 = 2, Y1 = 2, X2 = 10, Y2 = 10, Stroke = Brush(color), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        glyph.Children.Add(new Line { X1 = 10, Y1 = 2, X2 = 2, Y2 = 10, Stroke = Brush(color), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        return glyph;
    }

    private static FrameworkElement TrashGlyph()
    {
        const string color = "#FF8585";
        var glyph = new Grid { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.Children.Add(new Border { Width = 12, Height = 1.7, Background = Brush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) });
        glyph.Children.Add(new Border { Width = 5, Height = 1.7, Background = Brush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) });
        glyph.Children.Add(new Border { Width = 9, Height = 10, BorderBrush = Brush(color), BorderThickness = new Thickness(1.3), CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
        glyph.Children.Add(new Border { Width = 1.2, Height = 5, Background = Brush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) });
        glyph.Children.Add(new Border { Width = 1.2, Height = 5, Background = Brush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 3) });
        glyph.Children.Add(new Border { Width = 1.2, Height = 5, Background = Brush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 0, 3) });
        return glyph;
    }

    private static FrameworkElement CollapseGlyph(bool collapsed)
    {
        var glyph = new Grid { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.Children.Add(new Border { Width = 10, Height = 2, CornerRadius = new CornerRadius(1), Background = Brush("#F4F6FF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (collapsed)
            glyph.Children.Add(new Border { Width = 2, Height = 10, CornerRadius = new CornerRadius(1), Background = Brush("#F4F6FF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return glyph;
    }

    private static Button MakeButton(string text, Action action, string background = "#25324A")
    {
        var isCloseGlyph = text == "×";
        var button = new Button
        {
            Content = isCloseGlyph ? CreateCenteredCloseGlyph() : text,
            Background = Brush(background),
            Margin = new Thickness(0, 1, 0, 1),
            Padding = isCloseGlyph ? new Thickness(0) : new Thickness(12, 8, 12, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (background.Equals("#5D6EFF", StringComparison.OrdinalIgnoreCase)) button.Foreground = Brushes.White;
        button.Click += (_, _) => action();
        return button;
    }

    private static Button MakeButton(string text, Func<Task> action, string background = "#25324A")
    {
        var isCloseGlyph = text == "×";
        var button = new Button
        {
            Content = isCloseGlyph ? CreateCenteredCloseGlyph() : text,
            Background = Brush(background),
            Margin = new Thickness(0, 1, 0, 1),
            Padding = isCloseGlyph ? new Thickness(0) : new Thickness(12, 8, 12, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (background.Equals("#5D6EFF", StringComparison.OrdinalIgnoreCase)) button.Foreground = Brushes.White;
        button.Click += async (_, _) => await action();
        return button;
    }

    private static UIElement CreateCenteredCloseGlyph()
    {
        var glyph = new Grid { Width = 11, Height = 11, IsHitTestVisible = false };
        foreach (var angle in new[] { 45d, -45d })
        {
            var stroke = new Border
            {
                Width = 11,
                Height = 1.7,
                Background = Brush("#F4F6FF"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(angle)
            };
            glyph.Children.Add(stroke);
        }
        return glyph;
    }

    private static SolidColorBrush Brush(string color)
    {
        var role = color.ToUpperInvariant() switch
        {
            "#0B1020" => "background_start",
            "#111A30" => "background_end",
            "#151D30" or "#10182A" => "panel",
            "#0E1525" => "input",
            "#25324A" or "#202B40" => "button",
            "#26324A" => "line",
            "#174331" => "success_soft",
            "#492B36" or "#392833" => "danger_soft",
            "#5D6EFF" or "#91A4FF" => "accent",
            "#35C979" or "#52DB91" => "success",
            "#FF8585" or "#B34A61" => "danger",
            "#E5B94C" => "warning",
            "#F4F6FF" or "#DCE3F5" => "text",
            "#9AA7C0" or "#8D9AB3" or "#8491AA" or "#A5B0C6" => "muted",
            _ => ""
        };
        if (role.Length == 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        if (_themeBrushes.TryGetValue(role, out var brush)) return brush;
        brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ThemeColor(role)));
        _themeBrushes[role] = brush;
        return brush;
    }

    private static SolidColorBrush FixedBrush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private void ApplyThemePreset(string? presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName) || !ThemePresets.TryGetValue(presetName, out var palette))
            presetName = "Midnight";
        palette = ThemePresets[presetName];
        _config.ThemePreset = presetName;
        _config.ThemeBackgroundStart = _themeBackgroundStart = palette.BackgroundStart;
        _config.ThemeBackgroundEnd = _themeBackgroundEnd = palette.BackgroundEnd;
        _config.ThemePanel = _themePanel = palette.Panel;
        _config.ThemeInput = _themeInput = palette.Input;
        _config.ThemeButton = _themeButton = palette.Button;
        _config.ThemeAccent = _themeAccent = palette.Accent;
        _config.ThemeText = _themeText = palette.Text;
        _config.ThemeMuted = _themeMuted = palette.Muted;
        _themeLine = palette.Line;
        _themeSuccess = palette.Success;
        _themeDanger = palette.Danger;
        _themeWarning = palette.Warning;
        _themeSuccessSoft = palette.SuccessSoft;
        _themeDangerSoft = palette.DangerSoft;
        ApplyThemeResources();
    }

    private sealed record WidgetGuideStep(IReadOnlyList<FrameworkElement> Targets, string Title, string Body)
    {
        public WidgetGuideStep(FrameworkElement target, string title, string body) : this([target], title, body) { }
    }
    private sealed record SettingsSectionEntry(string Key, string Title, string SearchTerms, FrameworkElement Container, FrameworkElement Body, System.Windows.Shapes.Path Chevron);
    private sealed record ThemePalette(string BackgroundStart, string BackgroundEnd, string Panel, string Input, string Button, string Accent, string Text, string Muted, string Line, string Success, string Danger, string Warning, string SuccessSoft, string DangerSoft);

    private void ApplyThemeResources()
    {
        Application.Current.Resources["WindowBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeBackgroundStart));
        Application.Current.Resources["PanelBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themePanel));
        Application.Current.Resources["InputBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeInput));
        Application.Current.Resources["ButtonBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeButton));
        Application.Current.Resources["LineBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeLine));
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeAccent));
        Application.Current.Resources["TextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeText));
        Application.Current.Resources["MutedBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeMuted));
        Application.Current.Resources["GreenBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeSuccess));
        Application.Current.Resources["RedBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeDanger));
        Application.Current.Resources["WarningBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_themeWarning));
        RefreshThemeBrush("background_start", _themeBackgroundStart);
        RefreshThemeBrush("background_end", _themeBackgroundEnd);
        RefreshThemeBrush("panel", _themePanel);
        RefreshThemeBrush("input", _themeInput);
        RefreshThemeBrush("button", _themeButton);
        RefreshThemeBrush("line", _themeLine);
        RefreshThemeBrush("accent", _themeAccent);
        RefreshThemeBrush("text", _themeText);
        RefreshThemeBrush("muted", _themeMuted);
        RefreshThemeBrush("success", _themeSuccess);
        RefreshThemeBrush("danger", _themeDanger);
        RefreshThemeBrush("warning", _themeWarning);
        RefreshThemeBrush("success_soft", _themeSuccessSoft);
        RefreshThemeBrush("danger_soft", _themeDangerSoft);
    }

    private static void RefreshThemeBrush(string role, string color)
    {
        if (_themeBrushes.TryGetValue(role, out var brush))
            brush.Color = (Color)ColorConverter.ConvertFromString(color);
    }

    private static string ThemeColor(string role) => role switch
    {
        "background_start" => _themeBackgroundStart,
        "background_end" => _themeBackgroundEnd,
        "panel" => _themePanel,
        "input" => _themeInput,
        "button" => _themeButton,
        "line" => _themeLine,
        "success" => _themeSuccess,
        "danger" => _themeDanger,
        "warning" => _themeWarning,
        "success_soft" => _themeSuccessSoft,
        "danger_soft" => _themeDangerSoft,
        "accent" => _themeAccent,
        "text" => _themeText,
        "muted" => _themeMuted,
        _ => "#FFFFFF"
    };

    private static string ShortToken(string value) => value.Length <= 12 ? value : value[..6] + "…" + value[^4..];
    private static string FormatTime(string value) => DateTimeOffset.TryParse(value, out var timestamp) ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : value;

    private static string FormatCardPlayedTime(string value) =>
        DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")
            : FormatTime(value);
    private static string HistoryStatus(string value) => value switch { "sent" => "Gifted", "not_found" => "Username not found", "needs_attention" => "Needs attention", "pending" => "Queued", "processing" => "Sending", _ => value };
    private static string GiftSourceLabel(string value) => value switch
    {
        "free_chest_hour" => "Free Chest Hour",
        "streamloots_card" => "Streamloots card",
        _ => "Channel point redeem"
    };
    private static string ChestWord(int count) => count == 1 ? "chest" : "chests";
    private static string HistoryColor(string value) => value switch { "sent" => "#35C979", "not_found" => "#FF8585", "needs_attention" => "#E5B94C", _ => "#91A4FF" };
    private static FreeChestRule CloneRule(FreeChestRule rule) => new() { Keyword = rule.Keyword, Collection = rule.Collection, Packs = rule.Packs, Cards = rule.Cards, Enabled = rule.Enabled };

    private static Dictionary<string, string> LoadEnvironment(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;
                var split = trimmed.Split(new[] { '=' }, 2, StringSplitOptions.None);
                values[split[0].Trim()] = split[1].Trim().Trim('"', '\'');
            }
        }
        catch (IOException) { }
        return values;
    }

    private IReadOnlyList<TwitchReward> _lastRewards = [];
    private StackPanel? _rewardContainer;
    private ToggleButton? _showInactive;
}
