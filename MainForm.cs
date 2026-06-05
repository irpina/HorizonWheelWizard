using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace HorizonSimTool;

internal sealed class MainForm : Form
{
    private const string InputZipName = "inputmappingprofiles.zip";
    private const string WheelZipName = "wheeltunablesettingspc.zip";

    private readonly TextBox _mediaFolderText = new();
    private readonly TextBox _installFolderText = new();
    private readonly TextBox _inputZipText = new();
    private readonly TextBox _wheelZipText = new();
    private readonly TextBox _outputFolderText = new();
    private readonly TextBox _newProfileNameText = new();
    private readonly TextBox _logText = new();
    private readonly Label _statusLabel = new();
    private readonly ComboBox _presetCombo = new();

    private readonly DataGridView _devicesGrid = new();
    private readonly DataGridView _dInputGrid = new();
    private readonly CheckBox _controllerDevicesOnlyCheck = new();
    private readonly CheckBox _advancedSettingsCheck = new();
    private readonly GroupBox _advancedSettingsGroup = new();

    private readonly ComboBox _profileCombo = new();
    private readonly ComboBox _primaryDeviceCombo = new();
    private readonly ComboBox _pedalsDeviceCombo = new();
    private readonly ComboBox _shifterDeviceCombo = new();
    private readonly ComboBox _handbrakeDeviceCombo = new();
    private readonly ComboBox _ffbTemplateCombo = new();

    private readonly CheckBox _patchInPlaceCheck = new();
    private readonly CheckBox _useBackupSourcesCheck = new();
    private readonly CheckBox _setFfbInvertCheck = new();
    private readonly CheckBox _ffbInvertValueCheck = new();

    private readonly Label _wheelbaseStatusLabel = new();
    private readonly Label _pedalsStatusLabel = new();
    private readonly Label _shifterStatusLabel = new();
    private readonly Label _handbrakeStatusLabel = new();
    private readonly Label _profileStatusLabel = new();
    private readonly Label _zipStatusLabel = new();
    private readonly Label _backupStatusLabel = new();
    private readonly Label _generatedStatusLabel = new();
    private readonly Label _silenceStatusLabel = new();

    private readonly TextBox _wheelbaseSummaryText = new();
    private readonly TextBox _pedalsSummaryText = new();
    private readonly TextBox _shifterSummaryText = new();
    private readonly TextBox _handbrakeSummaryText = new();
    private readonly DataGridView _silenceGrid = new();
    private readonly CheckBox _autoSilenceCheck = new();
    private readonly CheckBox _minimizeToTrayCheck = new();
    private NotifyIcon? _trayIcon;

    private IReadOnlyList<DeviceInfo> _devices = Array.Empty<DeviceInfo>();
    private IReadOnlyList<DeviceInfo> _controllerDevices = Array.Empty<DeviceInfo>();
    private IReadOnlyList<ZipProfileInfo> _profiles = Array.Empty<ZipProfileInfo>();
    private IReadOnlyList<ZipWheelTuneInfo> _ffbTemplates = Array.Empty<ZipWheelTuneInfo>();
    private XDocument? _currentDocument;
    private ZipProfileInfo? _selectedProfile;
    private string? _generatedInputZip;
    private string? _generatedWheelZip;
    private readonly HashSet<string> _silencedDeviceInstanceIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _silenceApplied;
    private bool _updatingMediaDefaults;
    private bool _loadingPresetList;
    private bool _applyingPreset;
    private bool _forceClose;
    private bool _trayBalloonShown;

    public MainForm()
    {
        Text = "Horizon SimTool";
        Width = 1480;
        Height = 900;
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(246, 248, 251);
        var appIcon = TryLoadApplicationIcon();
        if (appIcon is not null)
        {
            Icon = appIcon;
        }

        ConfigureStatusLabels();
        ConfigureTextInputs();
        ConfigureTrayIcon();
        BuildLayout();
        WireEvents();
        LoadPresetList();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshDevices();
        if (!TryApplyDefaultPresetAtStartup())
        {
            FindInstall();
            LoadProfilesAndTemplates();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_forceClose && e.CloseReason == CloseReason.UserClosing && _minimizeToTrayCheck.Checked)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (_silenceApplied)
        {
            try
            {
                TryStopSilence(showMessages: false);
            }
            catch (Exception ex)
            {
                Log("Could not stop Silence during shutdown: " + ex.Message);
            }
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_forceClose && WindowState == FormWindowState.Minimized && _minimizeToTrayCheck.Checked)
        {
            HideToTray();
        }
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Regular)
        };
        tabs.TabPages.Add(BuildMappingTab());
        tabs.TabPages.Add(BuildGenerateTab());
        tabs.TabPages.Add(BuildSilenceTab());
        root.Controls.Add(tabs, 0, 1);

        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.AutoSize = true;
        _statusLabel.MinimumSize = new Size(0, 30);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = Color.FromArgb(52, 73, 94);
        _statusLabel.Text = "Ready";
        root.Controls.Add(_statusLabel, 0, 2);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(24, 32, 45),
            Padding = new Padding(18, 10, 18, 10),
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titlePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2
        };
        titlePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titlePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = "Horizon SimTool",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 17F),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = "Create wheel mapping XML/INI fixes, package them correctly, and install them with backups.",
            ForeColor = Color.FromArgb(196, 207, 222),
            Font = new Font("Segoe UI", 9.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            MaximumSize = new Size(1200, 0)
        };

        titlePanel.Controls.Add(title, 0, 0);
        titlePanel.Controls.Add(subtitle, 0, 1);
        panel.Controls.Add(titlePanel, 0, 0);

        var presetPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = false,
            Padding = new Padding(0, 2, 0, 0)
        };
        presetPanel.Controls.Add(new Label
        {
            Text = "Preset",
            AutoSize = true,
            MinimumSize = new Size(48, 30),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(226, 232, 240),
            Margin = new Padding(0, 4, 4, 0)
        });
        _presetCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _presetCombo.Width = 260;
        _presetCombo.DropDownWidth = 420;
        _presetCombo.Margin = new Padding(0, 4, 10, 0);
        presetPanel.Controls.Add(_presetCombo);
        presetPanel.Controls.Add(CreateButton("Save Preset", SavePreset, 104));
        presetPanel.Controls.Add(CreateButton("Set Default", SetDefaultPreset, 100));
        presetPanel.Controls.Add(CreateButton("Delete Preset", DeletePreset, 112));
        presetPanel.Controls.Add(CreateButton("Import", ImportPreset, 82));
        presetPanel.Controls.Add(CreateButton("Export", ExportPreset, 82));
        panel.Controls.Add(presetPanel, 0, 1);

        return panel;
    }

    private Control BuildGameFilesGroup()
    {
        var paths = CreateGroup("Game files");
        paths.MinimumSize = new Size(0, TableGroupHeight(rowCount: 5, minimumRowHeight: 42));
        var pathLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 5,
            Padding = new Padding(12)
        };
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        AddFixedRows(pathLayout, 5, 42);

        pathLayout.Controls.Add(CreateLabel("Media folder"), 0, 0);
        pathLayout.Controls.Add(_mediaFolderText, 1, 0);
        pathLayout.Controls.Add(CreateCellButton("Browse", BrowseMediaFolder), 2, 0);
        pathLayout.Controls.Add(CreateCellButton("Find", FindInstall), 3, 0);

        pathLayout.Controls.Add(CreateLabel("Input zip"), 0, 1);
        pathLayout.Controls.Add(_inputZipText, 1, 1);
        pathLayout.Controls.Add(CreateCellButton("Browse", BrowseInputZip), 2, 1);
        pathLayout.Controls.Add(CreateCellButton("Load files", LoadProfilesAndTemplates), 3, 1);

        pathLayout.Controls.Add(CreateLabel("Wheel tune zip"), 0, 2);
        pathLayout.Controls.Add(_wheelZipText, 1, 2);
        pathLayout.Controls.Add(CreateCellButton("Browse", BrowseWheelZip), 2, 2);

        _useBackupSourcesCheck.Text = "Use HST-BACKUP as source when available";
        _useBackupSourcesCheck.Checked = true;
        _useBackupSourcesCheck.AutoSize = true;
        pathLayout.Controls.Add(_useBackupSourcesCheck, 1, 3);
        pathLayout.SetColumnSpan(_useBackupSourcesCheck, 3);
        pathLayout.Controls.Add(_backupStatusLabel, 1, 4);
        pathLayout.SetColumnSpan(_backupStatusLabel, 3);

        paths.Controls.Add(pathLayout);
        return paths;
    }

    private Control BuildConnectedDevicesGroup()
    {
        var devicesGroup = CreateGroup("Connected devices");
        var devicesLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var deviceButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        deviceButtons.Controls.Add(CreateButton("Refresh", RefreshDevices, 84));
        _controllerDevicesOnlyCheck.Text = "Controllers only";
        _controllerDevicesOnlyCheck.Checked = true;
        _controllerDevicesOnlyCheck.AutoSize = true;
        _controllerDevicesOnlyCheck.Margin = new Padding(8, 8, 6, 0);
        deviceButtons.Controls.Add(_controllerDevicesOnlyCheck);
        deviceButtons.Controls.Add(CreateButton("Set wheelbase", () => UseSelectedDevice(_primaryDeviceCombo), 112));
        deviceButtons.Controls.Add(CreateButton("Set pedals", () => UseSelectedDevice(_pedalsDeviceCombo), 96));
        deviceButtons.Controls.Add(CreateButton("Set shifter", () => UseSelectedDevice(_shifterDeviceCombo), 96));
        deviceButtons.Controls.Add(CreateButton("Set handbrake", () => UseSelectedDevice(_handbrakeDeviceCombo), 118));
        deviceButtons.Controls.Add(CreateHint("Names prefer Windows Settings labels."));
        devicesLayout.Controls.Add(deviceButtons, 0, 0);

        ConfigureDeviceGrid();
        devicesLayout.Controls.Add(_devicesGrid, 0, 1);
        devicesGroup.Controls.Add(devicesLayout);
        return devicesGroup;
    }

    private Control BuildRoleSummaryPanel()
    {
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 8,
            RowCount = 1
        };
        for (var i = 0; i < 4; i++)
        {
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, i == 0 ? 86 : 78));
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        ConfigureSummaryText(_wheelbaseSummaryText);
        ConfigureSummaryText(_pedalsSummaryText);
        ConfigureSummaryText(_shifterSummaryText);
        ConfigureSummaryText(_handbrakeSummaryText);

        summary.Controls.Add(CreateLabel("Wheelbase"), 0, 0);
        summary.Controls.Add(_wheelbaseSummaryText, 1, 0);
        summary.Controls.Add(CreateLabel("Pedals"), 2, 0);
        summary.Controls.Add(_pedalsSummaryText, 3, 0);
        summary.Controls.Add(CreateLabel("Shifter"), 4, 0);
        summary.Controls.Add(_shifterSummaryText, 5, 0);
        summary.Controls.Add(CreateLabel("Handbrake"), 6, 0);
        summary.Controls.Add(_handbrakeSummaryText, 7, 0);
        return summary;
    }

    private TabPage BuildMappingTab()
    {
        var tab = new TabPage("1. Mapping")
        {
            BackColor = BackColor,
            AutoScroll = true,
            AutoScrollMinSize = new Size(1040, 720)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tab.Controls.Add(layout);

        var setupSplit = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            MinimumSize = new Size(0, 300)
        };
        setupSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        setupSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        setupSplit.Controls.Add(BuildGameFilesGroup(), 0, 0);
        setupSplit.Controls.Add(BuildConnectedDevicesGroup(), 1, 0);
        layout.Controls.Add(setupSplit, 0, 0);

        var profileGroup = CreateGroup("Profile and device roles");
        profileGroup.MinimumSize = new Size(0, 300);
        var profileLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 6,
            Padding = new Padding(12)
        };
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        for (var i = 0; i < 5; i++)
        {
            profileLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        profileLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ConfigureCombo(_profileCombo, ComboBoxStyle.DropDownList, 980);
        ConfigureCombo(_ffbTemplateCombo, ComboBoxStyle.DropDownList, 720);
        ConfigureCombo(_primaryDeviceCombo, ComboBoxStyle.DropDown, 720);
        ConfigureCombo(_pedalsDeviceCombo, ComboBoxStyle.DropDown, 720);
        ConfigureCombo(_shifterDeviceCombo, ComboBoxStyle.DropDown, 720);
        ConfigureCombo(_handbrakeDeviceCombo, ComboBoxStyle.DropDown, 720);

        profileLayout.Controls.Add(CreateLabel("XML profile"), 0, 0);
        profileLayout.Controls.Add(_profileCombo, 1, 0);
        profileLayout.SetColumnSpan(_profileCombo, 3);
        profileLayout.Controls.Add(_profileStatusLabel, 4, 0);

        profileLayout.Controls.Add(CreateLabel("FFB INI template"), 0, 1);
        profileLayout.Controls.Add(_ffbTemplateCombo, 1, 1);
        profileLayout.SetColumnSpan(_ffbTemplateCombo, 3);
        profileLayout.Controls.Add(_zipStatusLabel, 4, 1);

        profileLayout.Controls.Add(CreateLabel("Wheelbase / FFB"), 0, 2);
        profileLayout.Controls.Add(_primaryDeviceCombo, 1, 2);
        profileLayout.SetColumnSpan(_primaryDeviceCombo, 3);
        profileLayout.Controls.Add(_wheelbaseStatusLabel, 4, 2);

        profileLayout.Controls.Add(CreateLabel("Pedals"), 0, 3);
        profileLayout.Controls.Add(_pedalsDeviceCombo, 1, 3);
        profileLayout.SetColumnSpan(_pedalsDeviceCombo, 3);
        profileLayout.Controls.Add(_pedalsStatusLabel, 4, 3);

        profileLayout.Controls.Add(CreateLabel("Shifter"), 0, 4);
        profileLayout.Controls.Add(_shifterDeviceCombo, 1, 4);
        profileLayout.SetColumnSpan(_shifterDeviceCombo, 3);
        profileLayout.Controls.Add(_shifterStatusLabel, 4, 4);

        profileLayout.Controls.Add(CreateLabel("Handbrake"), 0, 5);
        profileLayout.Controls.Add(_handbrakeDeviceCombo, 1, 5);
        profileLayout.SetColumnSpan(_handbrakeDeviceCombo, 3);
        profileLayout.Controls.Add(_handbrakeStatusLabel, 4, 5);
        profileGroup.Controls.Add(profileLayout);
        layout.Controls.Add(profileGroup, 0, 1);

        var advancedLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2
        };
        advancedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        advancedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var advancedTogglePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        _advancedSettingsCheck.Text = "Advanced settings";
        _advancedSettingsCheck.AutoSize = true;
        _advancedSettingsCheck.Margin = new Padding(4, 10, 12, 0);
        advancedTogglePanel.Controls.Add(_advancedSettingsCheck);
        advancedTogglePanel.Controls.Add(CreateHint("Show command VID/PID and DirectInput overrides for custom pedal, shifter, or handbrake command mapping."));
        advancedLayout.Controls.Add(advancedTogglePanel, 0, 0);

        _advancedSettingsGroup.Text = "Command VID/PID and optional DirectInput overrides";
        _advancedSettingsGroup.Dock = DockStyle.Top;
        _advancedSettingsGroup.MinimumSize = new Size(0, 420);
        _advancedSettingsGroup.Padding = new Padding(8);
        _advancedSettingsGroup.BackColor = Color.FromArgb(246, 248, 251);
        _advancedSettingsGroup.ForeColor = Color.FromArgb(33, 43, 54);
        _advancedSettingsGroup.Visible = false;
        var dInputLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12)
        };
        dInputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dInputLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var dInputButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        dInputButtons.Controls.Add(CreateButton("Set commands to wheelbase", () => SetSelectedCommandRowsVidPid(_primaryDeviceCombo.Text), 168));
        dInputButtons.Controls.Add(CreateButton("Set commands to pedals", () => SetSelectedCommandRowsVidPid(_pedalsDeviceCombo.Text), 150));
        dInputButtons.Controls.Add(CreateButton("Set commands to shifter", () => SetSelectedCommandRowsVidPid(_shifterDeviceCombo.Text), 150));
        dInputButtons.Controls.Add(CreateButton("Set commands to handbrake", () => SetSelectedCommandRowsVidPid(_handbrakeDeviceCombo.Text), 164));
        _setFfbInvertCheck.Text = "Set FFB invert";
        _setFfbInvertCheck.AutoSize = true;
        _ffbInvertValueCheck.Text = "Invert FFB direction";
        _ffbInvertValueCheck.AutoSize = true;
        _ffbInvertValueCheck.Enabled = false;
        dInputButtons.Controls.Add(_setFfbInvertCheck);
        dInputButtons.Controls.Add(_ffbInvertValueCheck);
        dInputButtons.Controls.Add(CreateHint("Edit DInputIndex or DInputInvertAxis only for commands that need fixing."));
        dInputLayout.Controls.Add(dInputButtons, 0, 0);
        ConfigureDInputGrid();
        dInputLayout.Controls.Add(_dInputGrid, 0, 1);
        _advancedSettingsGroup.Controls.Add(dInputLayout);
        advancedLayout.Controls.Add(_advancedSettingsGroup, 0, 1);
        layout.Controls.Add(advancedLayout, 0, 2);

        return tab;
    }

    private TabPage BuildGenerateTab()
    {
        var tab = new TabPage("2. Generate / Install")
        {
            BackColor = BackColor,
            AutoScroll = true,
            AutoScrollMinSize = new Size(980, 560)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tab.Controls.Add(layout);

        var optionsGroup = CreateGroup("Output options");
        optionsGroup.MinimumSize = new Size(0, TableGroupHeight(rowCount: 4, minimumRowHeight: 42));
        var optionsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(12)
        };
        optionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        optionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        optionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        AddFixedRows(optionsLayout, 4, 42);

        _patchInPlaceCheck.Text = "Patch selected XML in place";
        _patchInPlaceCheck.Checked = true;
        _patchInPlaceCheck.AutoSize = true;
        _newProfileNameText.Enabled = false;
        _newProfileNameText.Text = "DefaultRawGameControllerMappingProfileCodexCustom.xml";
        _outputFolderText.ReadOnly = true;
        _outputFolderText.Text = DefaultOutputFolder();
        _installFolderText.ReadOnly = true;

        optionsLayout.Controls.Add(_patchInPlaceCheck, 0, 0);
        optionsLayout.Controls.Add(CreateHint("Uncheck to add a separate custom XML profile and preserve the original profile entry."), 1, 0);
        optionsLayout.Controls.Add(CreateLabel("New XML filename"), 0, 1);
        optionsLayout.Controls.Add(_newProfileNameText, 1, 1);
        optionsLayout.Controls.Add(CreateLabel("Game install folder"), 0, 2);
        optionsLayout.Controls.Add(_installFolderText, 1, 2);
        optionsLayout.Controls.Add(CreateCellButton("Find", FindInstall), 2, 2);
        optionsLayout.Controls.Add(CreateLabel("Generated output"), 0, 3);
        optionsLayout.Controls.Add(_outputFolderText, 1, 3);
        optionsLayout.Controls.Add(CreateCellButton("Open output folder", OpenOutputFolder), 2, 3);
        optionsGroup.Controls.Add(optionsLayout);
        layout.Controls.Add(optionsGroup, 0, 0);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6)
        };
        actionPanel.Controls.Add(CreateButton("Create backup", CreateBackup, 126));
        actionPanel.Controls.Add(CreateButton("Open backup folder", OpenBackupFolder, 154));
        actionPanel.Controls.Add(CreateButton("Restore backups", RestoreBackups, 136));
        actionPanel.Controls.Add(CreateButton("Generate files", GenerateFiles, 160));
        actionPanel.Controls.Add(CreateButton("Verify generated zips", VerifyGeneratedZips, 170));
        actionPanel.Controls.Add(CreateButton("Install generated files", InstallGeneratedFiles, 170));
        actionPanel.Controls.Add(_generatedStatusLabel);
        actionPanel.Controls.Add(CreateHint("Generate writes to the Horizon SimTool output folder. Install copies generated zips into the game media folder with HST-BACKUP protection."));
        layout.Controls.Add(actionPanel, 0, 1);

        var logGroup = CreateGroup("Status log");
        _logText.Dock = DockStyle.Fill;
        _logText.Multiline = true;
        _logText.ScrollBars = ScrollBars.Vertical;
        _logText.ReadOnly = true;
        _logText.BackColor = Color.White;
        _logText.BorderStyle = BorderStyle.FixedSingle;
        logGroup.Controls.Add(_logText);
        layout.Controls.Add(logGroup, 0, 2);

        return tab;
    }

    private TabPage BuildSilenceTab()
    {
        var tab = new TabPage("3. Silence")
        {
            BackColor = BackColor,
            AutoScroll = true,
            AutoScrollMinSize = new Size(980, 560)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tab.Controls.Add(layout);

        var statusGroup = CreateGroup("System-wide controller silence");
        statusGroup.Dock = DockStyle.Top;
        statusGroup.AutoSize = true;
        statusGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var statusLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = false,
            Padding = new Padding(12)
        };
        statusLayout.Controls.Add(_silenceStatusLabel);
        _autoSilenceCheck.Text = "Auto keep configured role devices visible";
        _autoSilenceCheck.Checked = true;
        _autoSilenceCheck.AutoSize = true;
        statusLayout.Controls.Add(_autoSilenceCheck);
        _minimizeToTrayCheck.Text = "Minimize to tray on close";
        _minimizeToTrayCheck.AutoSize = true;
        _minimizeToTrayCheck.Margin = new Padding(8, 8, 6, 0);
        statusLayout.Controls.Add(_minimizeToTrayCheck);
        statusLayout.Controls.Add(CreateButton("Check access", CheckSilenceBackend, 120));
        statusLayout.Controls.Add(CreateButton("Check status", CheckSilenceDeviceStatuses, 120));
        statusLayout.Controls.Add(CreateButton("Restore previous", RestorePreviousSilence, 132));
        statusLayout.Controls.Add(CreateHint("Only controller-class devices are listed. Hidden devices are disabled system-wide until restored."));
        statusGroup.Controls.Add(statusLayout);
        layout.Controls.Add(statusGroup, 0, 0);

        var gridGroup = CreateGroup("Controller devices");
        ConfigureSilenceGrid();
        gridGroup.Controls.Add(_silenceGrid);
        layout.Controls.Add(gridGroup, 0, 1);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6)
        };
        buttonPanel.Controls.Add(CreateButton("Refresh devices", RefreshDevices, 130));
        buttonPanel.Controls.Add(CreateButton("Auto configure", AutoConfigureSilenceRows, 132));
        buttonPanel.Controls.Add(CreateButton("Apply Silence", ApplySilence, 132));
        buttonPanel.Controls.Add(CreateButton("Stop Silence", StopSilence, 126));
        buttonPanel.Controls.Add(CreateHint("Unchecked rows are disabled. Stop Silence restores devices disabled by this app."));
        layout.Controls.Add(buttonPanel, 0, 2);

        return tab;
    }

    private void WireEvents()
    {
        _mediaFolderText.TextChanged += (_, _) =>
        {
            _installFolderText.Text = _mediaFolderText.Text;
            UpdateZipPathsFromMediaFolder();
        };
        _inputZipText.TextChanged += (_, _) => UpdateConfigurationVisuals();
        _wheelZipText.TextChanged += (_, _) => UpdateConfigurationVisuals();
        _profileCombo.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
        _primaryDeviceCombo.TextChanged += (_, _) =>
        {
            SelectBestFfbTemplate();
            PopulateSilenceGrid();
            UpdateConfigurationVisuals();
        };
        _pedalsDeviceCombo.TextChanged += (_, _) =>
        {
            PopulateSilenceGrid();
            UpdateConfigurationVisuals();
        };
        _shifterDeviceCombo.TextChanged += (_, _) =>
        {
            PopulateSilenceGrid();
            UpdateConfigurationVisuals();
        };
        _handbrakeDeviceCombo.TextChanged += (_, _) =>
        {
            PopulateSilenceGrid();
            UpdateConfigurationVisuals();
        };
        _setFfbInvertCheck.CheckedChanged += (_, _) => _ffbInvertValueCheck.Enabled = _setFfbInvertCheck.Checked;
        _patchInPlaceCheck.CheckedChanged += (_, _) => _newProfileNameText.Enabled = !_patchInPlaceCheck.Checked;
        _useBackupSourcesCheck.CheckedChanged += (_, _) => UpdateZipPathsFromMediaFolder();
        _controllerDevicesOnlyCheck.CheckedChanged += (_, _) =>
        {
            RefreshDeviceGrid();
            PopulateDeviceCombos();
        };
        _presetCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingPresetList && !_applyingPreset && _presetCombo.SelectedItem is PresetComboItem item)
            {
                RunGuarded(() => ApplyPresetFromPath(item.FilePath, startup: false));
            }
        };
        _advancedSettingsCheck.CheckedChanged += (_, _) => UpdateAdvancedSettingsVisibility();
        _autoSilenceCheck.CheckedChanged += (_, _) =>
        {
            if (_autoSilenceCheck.Checked)
            {
                AutoConfigureSilenceRows();
            }
        };
        _dInputGrid.CellValueChanged += (_, _) => HighlightDInputRows();
        _silenceGrid.CellValueChanged += (_, _) => HighlightSilenceRows();
        _silenceGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_silenceGrid.IsCurrentCellDirty)
            {
                _silenceGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
    }

    private void ConfigureStatusLabels()
    {
        foreach (var label in new[]
        {
            _wheelbaseStatusLabel,
            _pedalsStatusLabel,
            _shifterStatusLabel,
            _handbrakeStatusLabel,
            _profileStatusLabel,
            _zipStatusLabel,
            _backupStatusLabel,
            _silenceStatusLabel,
            _generatedStatusLabel
        })
        {
            label.AutoSize = true;
            label.MinimumSize = new Size(150, 30);
            label.Padding = new Padding(8, 4, 8, 4);
            label.Margin = new Padding(4, 4, 4, 4);
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font("Segoe UI Semibold", 8.5F);
            label.BorderStyle = BorderStyle.FixedSingle;
        }

        SetStatusLabel(_profileStatusLabel, "Profile needed", configured: false);
        SetStatusLabel(_zipStatusLabel, "Files needed", configured: false);
        SetStatusLabel(_wheelbaseStatusLabel, "Wheelbase needed", configured: false);
        SetStatusLabel(_pedalsStatusLabel, "Pedals optional", configured: false);
        SetStatusLabel(_shifterStatusLabel, "Shifter optional", configured: false);
        SetStatusLabel(_handbrakeStatusLabel, "Handbrake optional", configured: false);
        SetStatusLabel(_backupStatusLabel, "Backup needed", configured: false);
        SetStatusLabel(_silenceStatusLabel, "Not active", configured: false);
        SetStatusLabel(_generatedStatusLabel, "Not generated", configured: false);
    }

    private static void SetStatusLabel(Label label, string text, bool configured)
    {
        label.Text = text;
        label.BackColor = configured ? Color.FromArgb(210, 244, 224) : Color.FromArgb(255, 239, 205);
        label.ForeColor = configured ? Color.FromArgb(17, 97, 52) : Color.FromArgb(126, 82, 0);
    }

    private void ConfigureTextInputs()
    {
        foreach (var textBox in new[]
        {
            _mediaFolderText,
            _installFolderText,
            _inputZipText,
            _wheelZipText,
            _outputFolderText,
            _newProfileNameText
        })
        {
            textBox.Dock = DockStyle.Fill;
            textBox.MinimumSize = Size.Empty;
            textBox.Margin = new Padding(4, 4, 4, 4);
        }
    }

    private static void ConfigureCombo(ComboBox combo, ComboBoxStyle style, int dropDownWidth)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = style;
        combo.DropDownWidth = dropDownWidth;
        combo.IntegralHeight = false;
        combo.MaxDropDownItems = 24;
        if (style == ComboBoxStyle.DropDown)
        {
            combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            combo.AutoCompleteSource = AutoCompleteSource.ListItems;
        }
    }

    private void AddFixedRows(TableLayoutPanel layout, int count, float minimumHeight)
    {
        var rowHeight = FixedRowHeight(minimumHeight);
        for (var i = 0; i < count; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        }
    }

    private int TableGroupHeight(int rowCount, int minimumRowHeight)
    {
        return (int)(FixedRowHeight(minimumRowHeight) * rowCount) + 58;
    }

    private int FixedRowHeight(float minimumHeight)
    {
        return (int)Math.Ceiling(Math.Max(minimumHeight, TextRenderer.MeasureText("Hg", Font).Height + 20));
    }

    private static void ConfigureSummaryText(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.ReadOnly = true;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = Color.White;
        textBox.Margin = new Padding(4, 7, 12, 4);
    }

    private void ConfigureDeviceGrid()
    {
        _devicesGrid.Dock = DockStyle.Fill;
        _devicesGrid.ReadOnly = true;
        _devicesGrid.AllowUserToAddRows = false;
        _devicesGrid.AllowUserToDeleteRows = false;
        _devicesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _devicesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _devicesGrid.BackgroundColor = Color.White;
        _devicesGrid.BorderStyle = BorderStyle.FixedSingle;
        if (_devicesGrid.Columns.Count > 0)
        {
            return;
        }

        _devicesGrid.Columns.Add("Name", "Device");
        _devicesGrid.Columns.Add("Vid", "VID");
        _devicesGrid.Columns.Add("Pid", "PID");
        _devicesGrid.Columns.Add("Xml", "XML value");
        _devicesGrid.Columns.Add("Class", "Class");
        _devicesGrid.Columns.Add("NameSource", "Name source");
        _devicesGrid.Columns["Name"]!.FillWeight = 230;
        _devicesGrid.Columns["Class"]!.FillWeight = 110;
        _devicesGrid.Columns["NameSource"]!.FillWeight = 105;
    }

    private void ConfigureDInputGrid()
    {
        _dInputGrid.Dock = DockStyle.Fill;
        _dInputGrid.AllowUserToAddRows = false;
        _dInputGrid.AllowUserToDeleteRows = false;
        _dInputGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _dInputGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _dInputGrid.BackgroundColor = Color.White;
        _dInputGrid.BorderStyle = BorderStyle.FixedSingle;
        _dInputGrid.Columns.Add("Key", "Command");
        _dInputGrid.Columns.Add("Type", "Type");
        _dInputGrid.Columns.Add("Index", "Index");
        _dInputGrid.Columns.Add("Invert", "Invert");
        _dInputGrid.Columns.Add("VidPid", "Command VID/PID");
        _dInputGrid.Columns.Add("DInputIndex", "DInputIndex");
        _dInputGrid.Columns.Add("DInputInvert", "DInputInvertAxis");
        foreach (var name in new[] { "Key", "Type", "Index", "Invert" })
        {
            _dInputGrid.Columns[name]!.ReadOnly = true;
        }

        _dInputGrid.Columns["Key"]!.FillWeight = 220;
        _dInputGrid.Columns["VidPid"]!.FillWeight = 130;
        _dInputGrid.Columns["DInputIndex"]!.FillWeight = 90;
        _dInputGrid.Columns["DInputInvert"]!.FillWeight = 115;
    }

    private void ConfigureSilenceGrid()
    {
        _silenceGrid.Dock = DockStyle.Fill;
        _silenceGrid.AllowUserToAddRows = false;
        _silenceGrid.AllowUserToDeleteRows = false;
        _silenceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _silenceGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _silenceGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders;
        _silenceGrid.BackgroundColor = Color.White;
        _silenceGrid.BorderStyle = BorderStyle.FixedSingle;
        _silenceGrid.RowHeadersVisible = false;
        _silenceGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        if (_silenceGrid.Columns.Count > 0)
        {
            return;
        }

        _silenceGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Visible",
            HeaderText = "Visible",
            Width = 96,
            FillWeight = 58
        });
        _silenceGrid.Columns.Add("Status", "Status");
        _silenceGrid.Columns.Add("Device", "Device");
        _silenceGrid.Columns.Add("VidPid", "VID/PID");
        _silenceGrid.Columns.Add("Role", "Mapped role");
        _silenceGrid.Columns.Add("Default", "Mode");
        _silenceGrid.Columns.Add("InstanceId", "Device instance path");
        _silenceGrid.Columns["Status"]!.ReadOnly = true;
        _silenceGrid.Columns["Device"]!.ReadOnly = true;
        _silenceGrid.Columns["VidPid"]!.ReadOnly = true;
        _silenceGrid.Columns["Role"]!.ReadOnly = true;
        _silenceGrid.Columns["Default"]!.ReadOnly = true;
        _silenceGrid.Columns["InstanceId"]!.ReadOnly = true;
        _silenceGrid.Columns["InstanceId"]!.Visible = false;
        _silenceGrid.Columns["Status"]!.FillWeight = 82;
        _silenceGrid.Columns["Device"]!.FillWeight = 245;
        _silenceGrid.Columns["VidPid"]!.FillWeight = 95;
        _silenceGrid.Columns["Role"]!.FillWeight = 115;
        _silenceGrid.Columns["Default"]!.FillWeight = 120;
    }

    private IEnumerable<ComboBox> RoleDeviceCombos()
    {
        yield return _primaryDeviceCombo;
        yield return _pedalsDeviceCombo;
        yield return _shifterDeviceCombo;
        yield return _handbrakeDeviceCombo;
    }

    private void UpdateConfigurationVisuals()
    {
        SetStatusLabel(_profileStatusLabel, _profileCombo.SelectedItem is ProfileComboItem ? "Profile set" : "Profile needed", _profileCombo.SelectedItem is ProfileComboItem);
        var filesReady = File.Exists(_inputZipText.Text) && File.Exists(_wheelZipText.Text);
        SetStatusLabel(_zipStatusLabel, filesReady ? "Source files set" : "Files needed", filesReady);
        UpdatePathVisuals();

        SetRoleStatus(_wheelbaseStatusLabel, "Wheelbase", _primaryDeviceCombo.Text, required: true);
        SetRoleStatus(_pedalsStatusLabel, "Pedals", _pedalsDeviceCombo.Text, required: false);
        SetRoleStatus(_shifterStatusLabel, "Shifter", _shifterDeviceCombo.Text, required: false);
        SetRoleStatus(_handbrakeStatusLabel, "Handbrake", _handbrakeDeviceCombo.Text, required: false);
        SetSummaryBox(_wheelbaseSummaryText, _primaryDeviceCombo.Text, required: true);
        SetSummaryBox(_pedalsSummaryText, _pedalsDeviceCombo.Text, required: false);
        SetSummaryBox(_shifterSummaryText, _shifterDeviceCombo.Text, required: false);
        SetSummaryBox(_handbrakeSummaryText, _handbrakeDeviceCombo.Text, required: false);
        var backupReady = Directory.Exists(GameFileInstaller.GetBackupFolder(_mediaFolderText.Text))
            && File.Exists(GameFileInstaller.GetBackupInputZipPath(_mediaFolderText.Text))
            && File.Exists(GameFileInstaller.GetBackupWheelZipPath(_mediaFolderText.Text));
        SetStatusLabel(_backupStatusLabel, backupReady ? "Backup ready" : "Backup needed", backupReady);
        if (_autoSilenceCheck.Checked && !_silenceGrid.IsCurrentCellInEditMode)
        {
            AutoConfigureSilenceRows();
        }

        HighlightDInputRows();
        HighlightSilenceRows();
    }

    private void UpdatePathVisuals()
    {
        SetPathTextBoxState(_mediaFolderText, Directory.Exists(_mediaFolderText.Text));
        SetPathTextBoxState(_installFolderText, Directory.Exists(_installFolderText.Text));
        SetPathTextBoxState(_inputZipText, File.Exists(_inputZipText.Text));
        SetPathTextBoxState(_wheelZipText, File.Exists(_wheelZipText.Text));
        SetPathTextBoxState(_outputFolderText, Directory.Exists(_outputFolderText.Text));
    }

    private static void SetPathTextBoxState(TextBox textBox, bool configured)
    {
        textBox.BackColor = configured ? Color.FromArgb(232, 247, 238) : Color.FromArgb(255, 248, 224);
        textBox.ForeColor = configured ? Color.FromArgb(17, 97, 52) : Color.FromArgb(126, 82, 0);
    }

    private void ConfigureTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Horizon SimTool", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _forceClose = true;
            RestoreFromTray();
            Close();
        });

        _trayIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "Horizon SimTool",
            ContextMenuStrip = menu,
            Visible = false
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Icon = Icon ?? SystemIcons.Application;
        _trayIcon.Visible = true;
        Hide();
        ShowInTaskbar = false;
        if (!_trayBalloonShown)
        {
            _trayIcon.ShowBalloonTip(
                1800,
                "Horizon SimTool",
                "Still running in the tray. Use the tray icon to restore or exit.",
                ToolTipIcon.Info);
            _trayBalloonShown = true;
        }
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void UpdateAdvancedSettingsVisibility()
    {
        _advancedSettingsGroup.Visible = _advancedSettingsCheck.Checked;
        if (_advancedSettingsCheck.Checked)
        {
            _advancedSettingsGroup.PerformLayout();
            _advancedSettingsGroup.FindForm()?.BeginInvoke(() =>
            {
                ScrollControlIntoVisibleParent(_advancedSettingsGroup);
            });
        }
    }

    private static void ScrollControlIntoVisibleParent(Control control)
    {
        for (Control? current = control.Parent; current is not null; current = current.Parent)
        {
            if (current is ScrollableControl { AutoScroll: true } scrollable)
            {
                scrollable.ScrollControlIntoView(control);
                return;
            }
        }
    }

    private void SetRoleStatus(Label label, string roleName, string value, bool required)
    {
        var configured = VidPid.TryParse(value, out _);
        var text = configured
            ? roleName + " set"
            : required
                ? roleName + " needed"
                : roleName + " optional";
        SetStatusLabel(label, text, configured);
    }

    private static void SetSummaryBox(TextBox textBox, string value, bool required)
    {
        var configured = VidPid.TryParse(value, out _);
        textBox.Text = configured
            ? value
            : required
                ? "Not configured"
                : "Optional";
        textBox.BackColor = configured ? Color.FromArgb(232, 247, 238) : Color.FromArgb(255, 248, 224);
        textBox.ForeColor = configured ? Color.FromArgb(17, 97, 52) : Color.FromArgb(126, 82, 0);
    }

    private void HighlightDInputRows()
    {
        foreach (DataGridViewRow row in _dInputGrid.Rows)
        {
            var vidPidText = CellText(row, "VidPid");
            var invalidVidPid = !string.IsNullOrWhiteSpace(vidPidText) && !VidPid.TryParse(vidPidText, out _);
            var configured = !string.IsNullOrWhiteSpace(CellText(row, "DInputIndex"))
                || !string.IsNullOrWhiteSpace(CellText(row, "DInputInvert"));
            row.DefaultCellStyle.BackColor = invalidVidPid
                ? Color.FromArgb(255, 235, 230)
                : configured
                    ? Color.FromArgb(232, 244, 255)
                    : Color.White;
        }
    }

    private void HighlightSilenceRows()
    {
        foreach (DataGridViewRow row in _silenceGrid.Rows)
        {
            var visible = CellBool(row, "Visible");
            var role = CellText(row, "Role");
            var defaultText = CellText(row, "Default");
            var status = CellText(row, "Status");
            row.DefaultCellStyle.BackColor = GetSilenceRowColor(visible, role, defaultText, status);
            row.DefaultCellStyle.ForeColor = Color.FromArgb(33, 43, 54);
        }
    }

    private static Color GetSilenceRowColor(bool visible, string role, string defaultText, string status)
    {
        if (status.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromArgb(255, 228, 225);
        }

        if (status.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromArgb(235, 238, 245);
        }

        if (visible)
        {
            return !string.IsNullOrWhiteSpace(role)
                ? Color.FromArgb(232, 247, 238)
                : Color.White;
        }

        return defaultText.Contains("controller", StringComparison.OrdinalIgnoreCase)
            ? Color.FromArgb(255, 239, 205)
            : Color.FromArgb(255, 235, 230);
    }

    private void RefreshDevices()
    {
        RunGuarded(() =>
        {
            _devices = DeviceEnumerator.GetPresentVidPidDevices();
            _controllerDevices = DeviceEnumerator.GetPresentControllerDevices();
            RefreshDeviceGrid();
            PopulateDeviceCombos();
            PopulateSilenceGrid();
            UpdateConfigurationVisuals();
            Log($"Loaded {_devices.Count} VID/PID devices and {_controllerDevices.Count} controller devices.");
        });
    }

    private void RefreshDeviceGrid()
    {
        var visibleDevices = VisibleMappingDevices();
        _devicesGrid.Rows.Clear();
        foreach (var device in visibleDevices)
        {
            var rowIndex = _devicesGrid.Rows.Add(
                device.Name,
                device.VidPid.Vid,
                device.VidPid.Pid,
                device.VidPid.ToXmlString(),
                device.ClassName,
                device.UsesWindowsSettingsName ? "Windows Settings" : "Driver");
            _devicesGrid.Rows[rowIndex].Tag = device;
            if (device.UsesWindowsSettingsName)
            {
                _devicesGrid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(232, 247, 238);
            }
        }
    }

    private void FindInstall()
    {
        RunGuarded(() =>
        {
            var folder = ForzaInstallFinder.FindMediaFolders().FirstOrDefault();
            if (folder is null)
            {
                Log("No Forza Horizon 6 media folder was found automatically. Browse to it manually.");
                return;
            }

            _mediaFolderText.Text = folder;
            Log("Found game media folder: " + folder);
        });
    }

    private void LoadProfilesAndTemplates()
    {
        RunGuarded(() =>
        {
            ValidateSourcePaths();

            _profiles = ZipProfileReader.ReadProfiles(_inputZipText.Text);
            _profileCombo.Items.Clear();
            foreach (var profile in _profiles)
            {
                _profileCombo.Items.Add(new ProfileComboItem(profile));
            }

            _ffbTemplates = ZipWheelTuneReader.ReadEntries(_wheelZipText.Text);
            _ffbTemplateCombo.Items.Clear();
            foreach (var template in _ffbTemplates)
            {
                _ffbTemplateCombo.Items.Add(new TemplateComboItem(template));
            }

            var defaultProfileIndex = FindDefaultProfileIndex(_profiles);
            if (_profileCombo.Items.Count > 0)
            {
                _profileCombo.SelectedIndex = defaultProfileIndex;
            }

            _generatedInputZip = null;
            _generatedWheelZip = null;
            SetStatusLabel(_generatedStatusLabel, "Not generated", configured: false);
            UpdateConfigurationVisuals();
            Log($"Loaded {_profiles.Count} XML profiles and {_ffbTemplates.Count} FFB templates.");
        });
    }

    private void LoadSelectedProfile()
    {
        RunGuarded(() =>
        {
            if (_profileCombo.SelectedItem is not ProfileComboItem item)
            {
                return;
            }

            _selectedProfile = item.Profile;
            _currentDocument = ZipProfileReader.ReadXml(_inputZipText.Text, item.Profile.EntryName);
            var profileElement = XmlProfileEditor.GetProfileElement(_currentDocument);
            var currentPrimary = XmlProfileEditor.TryGetProfileVidPid(profileElement, "PrimaryDeviceVidPid");
            var currentFfb = XmlProfileEditor.TryGetProfileVidPid(profileElement, "FFBDeviceVidPid")
                ?? XmlProfileEditor.TryGetProfileVidPid(profileElement, "FFBVidPid")
                ?? currentPrimary;

            if (string.IsNullOrWhiteSpace(_primaryDeviceCombo.Text))
            {
                SetComboTextForVidPid(_primaryDeviceCombo, currentPrimary);
            }

            PopulateDInputGrid();
            SelectBestFfbTemplate(currentFfb ?? currentPrimary);
            UpdateConfigurationVisuals();
            Log("Loaded profile: " + item.Profile.EntryName);
        });
    }

    private void PopulateDeviceCombos()
    {
        var existingPrimary = _primaryDeviceCombo.Text;
        var existingPedals = _pedalsDeviceCombo.Text;
        var existingShifter = _shifterDeviceCombo.Text;
        var existingHandbrake = _handbrakeDeviceCombo.Text;

        foreach (var combo in RoleDeviceCombos())
        {
            combo.Items.Clear();
        }

        foreach (var device in VisibleMappingDevices())
        {
            _primaryDeviceCombo.Items.Add(new DeviceComboItem(device));
            _pedalsDeviceCombo.Items.Add(new DeviceComboItem(device));
            _shifterDeviceCombo.Items.Add(new DeviceComboItem(device));
            _handbrakeDeviceCombo.Items.Add(new DeviceComboItem(device));
        }

        if (!string.IsNullOrWhiteSpace(existingPrimary))
        {
            _primaryDeviceCombo.Text = existingPrimary;
        }

        if (!string.IsNullOrWhiteSpace(existingPedals))
        {
            _pedalsDeviceCombo.Text = existingPedals;
        }

        if (!string.IsNullOrWhiteSpace(existingShifter))
        {
            _shifterDeviceCombo.Text = existingShifter;
        }

        if (!string.IsNullOrWhiteSpace(existingHandbrake))
        {
            _handbrakeDeviceCombo.Text = existingHandbrake;
        }

        AutoAssignEmptyRolesFromDeviceNames();
        UpdateConfigurationVisuals();
    }

    private IReadOnlyList<DeviceInfo> VisibleMappingDevices()
    {
        return _controllerDevicesOnlyCheck.Checked ? _controllerDevices : _devices;
    }

    private void SavePreset()
    {
        RunGuarded(() =>
        {
            CommitPresetGridEdits();
            var currentName = (_presetCombo.SelectedItem as PresetComboItem)?.Name ?? "My Preset";
            var presetName = PromptForPresetName("Save Preset", currentName);
            if (string.IsNullOrWhiteSpace(presetName))
            {
                Log("Save preset cancelled.");
                return;
            }

            var presetPath = PresetStore.GetPresetPath(presetName);
            if (File.Exists(presetPath))
            {
                var answer = MessageBox.Show(
                    $"A preset named \"{presetName}\" already exists.\n\nOverwrite it?",
                    "Save Preset",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    Log("Save preset cancelled. Existing preset was not overwritten.");
                    return;
                }
            }

            var preset = CapturePreset(presetName);
            PresetStore.SavePreset(presetPath, preset);
            LoadPresetList(presetPath);
            Log("Saved preset: " + presetName);
        });
    }

    private void SetDefaultPreset()
    {
        RunGuarded(() =>
        {
            var item = SelectedPresetOrThrow();
            PresetStore.SetDefaultPreset(item.FilePath);
            LoadPresetList(item.FilePath);
            Log("Default preset set: " + item.Name);
        });
    }

    private void DeletePreset()
    {
        RunGuarded(() =>
        {
            var item = SelectedPresetOrThrow();
            var answer = MessageBox.Show(
                $"Delete preset \"{item.Name}\"?\n\nThis removes the saved preset file from HST-PRESETS.",
                "Delete Preset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                Log("Delete preset cancelled.");
                return;
            }

            PresetStore.ClearDefaultIfMatches(item.FilePath);
            File.Delete(item.FilePath);
            LoadPresetList();
            Log("Deleted preset: " + item.Name);
        });
    }

    private void ImportPreset()
    {
        RunGuarded(() =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Import Horizon SimTool preset",
                Filter = "Horizon SimTool presets (*.hstpreset.json;*.json)|*.hstpreset.json;*.json|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(PresetStore.Folder) ? PresetStore.Folder : AppContext.BaseDirectory
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                Log("Import preset cancelled.");
                return;
            }

            var preset = PresetStore.LoadPreset(dialog.FileName);
            if (string.IsNullOrWhiteSpace(preset.PresetName))
            {
                preset.PresetName = Path.GetFileNameWithoutExtension(dialog.FileName);
            }

            var destination = PresetStore.GetPresetPath(preset.PresetName);
            if (File.Exists(destination))
            {
                var answer = MessageBox.Show(
                    $"A preset named \"{preset.PresetName}\" already exists.\n\nOverwrite it?",
                    "Import Preset",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    Log("Import preset cancelled. Existing preset was not overwritten.");
                    return;
                }
            }

            PresetStore.SavePreset(destination, preset);
            LoadPresetList(destination);
            ApplyPresetFromPath(destination, startup: false);
            Log("Imported and loaded preset: " + preset.PresetName);
        });
    }

    private void ExportPreset()
    {
        RunGuarded(() =>
        {
            var item = SelectedPresetOrThrow();
            using var dialog = new SaveFileDialog
            {
                Title = "Export Horizon SimTool preset",
                Filter = "Horizon SimTool preset (*.hstpreset.json)|*.hstpreset.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = Path.GetFileName(item.FilePath),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                Log("Export preset cancelled.");
                return;
            }

            File.Copy(item.FilePath, dialog.FileName, overwrite: true);
            Log("Exported preset to: " + dialog.FileName);
        });
    }

    private void LoadPresetList(string? selectPath = null)
    {
        _loadingPresetList = true;
        try
        {
            _presetCombo.Items.Clear();
            var defaultPath = PresetStore.GetDefaultPresetPath();
            foreach (var presetPath in PresetStore.GetPresetFiles())
            {
                try
                {
                    var preset = PresetStore.LoadPreset(presetPath);
                    var name = string.IsNullOrWhiteSpace(preset.PresetName)
                        ? Path.GetFileNameWithoutExtension(presetPath)
                        : preset.PresetName;
                    _presetCombo.Items.Add(new PresetComboItem(presetPath, name, PresetStore.PathsEqual(presetPath, defaultPath)));
                }
                catch (Exception ex)
                {
                    Log($"Skipped preset {Path.GetFileName(presetPath)}: {ex.Message}");
                }
            }

            SelectPresetComboPath(selectPath ?? defaultPath);
        }
        finally
        {
            _loadingPresetList = false;
        }
    }

    private bool TryApplyDefaultPresetAtStartup()
    {
        var defaultPath = PresetStore.GetDefaultPresetPath();
        if (string.IsNullOrWhiteSpace(defaultPath))
        {
            return false;
        }

        try
        {
            ApplyPresetFromPath(defaultPath, startup: true);
            return true;
        }
        catch (Exception ex)
        {
            Log("Could not load default preset: " + ex.Message);
            return false;
        }
    }

    private void ApplyPresetFromPath(string presetPath, bool startup)
    {
        var preset = PresetStore.LoadPreset(presetPath);
        _applyingPreset = true;
        try
        {
            ApplyPreset(preset);
            SelectPresetComboPath(presetPath);
        }
        finally
        {
            _applyingPreset = false;
        }

        Log((startup ? "Loaded default preset: " : "Loaded preset: ") + preset.PresetName);
    }

    private void ApplyPreset(AppPreset preset)
    {
        _controllerDevicesOnlyCheck.Checked = preset.ControllerDevicesOnly;
        _useBackupSourcesCheck.Checked = preset.UseBackupSources;
        _advancedSettingsCheck.Checked = preset.AdvancedSettingsVisible;
        _patchInPlaceCheck.Checked = preset.PatchSelectedXmlInPlace;
        _newProfileNameText.Text = string.IsNullOrWhiteSpace(preset.NewProfileName)
            ? "DefaultRawGameControllerMappingProfileCodexCustom.xml"
            : preset.NewProfileName;
        _setFfbInvertCheck.Checked = preset.SetFfbInvert;
        _ffbInvertValueCheck.Checked = preset.FfbInvertValue;
        _autoSilenceCheck.Checked = preset.AutoSilence;
        _minimizeToTrayCheck.Checked = preset.MinimizeToTrayOnClose;

        if (!string.IsNullOrWhiteSpace(preset.MediaFolder))
        {
            _mediaFolderText.Text = preset.MediaFolder;
        }

        if (!string.IsNullOrWhiteSpace(preset.InputZip))
        {
            _inputZipText.Text = preset.InputZip;
        }

        if (!string.IsNullOrWhiteSpace(preset.WheelTuneZip))
        {
            _wheelZipText.Text = preset.WheelTuneZip;
        }

        _outputFolderText.Text = string.IsNullOrWhiteSpace(preset.OutputFolder)
            ? DefaultOutputFolder()
            : preset.OutputFolder;

        RefreshDeviceGrid();
        PopulateDeviceCombos();
        SetRoleComboTexts(preset);

        if (Directory.Exists(_mediaFolderText.Text) && File.Exists(_inputZipText.Text) && File.Exists(_wheelZipText.Text))
        {
            LoadProfilesAndTemplates();
            SelectProfileByEntryName(preset.XmlProfileEntryName);
            SelectTemplateByEntryName(preset.FfbTemplateEntryName);
        }

        SetRoleComboTexts(preset);
        ApplyDInputPresetRows(preset.CommandRows);
        PopulateSilenceGrid();
        UpdateConfigurationVisuals();
        ApplySilencePresetRows(preset.SilenceDevices);
        RefreshSilenceDeviceStatuses();
        _generatedInputZip = null;
        _generatedWheelZip = null;
        SetStatusLabel(_generatedStatusLabel, "Not generated", configured: false);
    }

    private AppPreset CapturePreset(string presetName)
    {
        return new AppPreset
        {
            PresetName = presetName.Trim(),
            SavedAt = DateTimeOffset.Now,
            MediaFolder = _mediaFolderText.Text.Trim(),
            InputZip = _inputZipText.Text.Trim(),
            WheelTuneZip = _wheelZipText.Text.Trim(),
            OutputFolder = _outputFolderText.Text.Trim(),
            UseBackupSources = _useBackupSourcesCheck.Checked,
            ControllerDevicesOnly = _controllerDevicesOnlyCheck.Checked,
            AdvancedSettingsVisible = _advancedSettingsCheck.Checked,
            XmlProfileEntryName = (_profileCombo.SelectedItem as ProfileComboItem)?.Profile.EntryName ?? "",
            FfbTemplateEntryName = (_ffbTemplateCombo.SelectedItem as TemplateComboItem)?.Template.EntryName ?? "",
            WheelbaseDevice = _primaryDeviceCombo.Text.Trim(),
            PedalsDevice = _pedalsDeviceCombo.Text.Trim(),
            ShifterDevice = _shifterDeviceCombo.Text.Trim(),
            HandbrakeDevice = _handbrakeDeviceCombo.Text.Trim(),
            PatchSelectedXmlInPlace = _patchInPlaceCheck.Checked,
            NewProfileName = _newProfileNameText.Text.Trim(),
            SetFfbInvert = _setFfbInvertCheck.Checked,
            FfbInvertValue = _ffbInvertValueCheck.Checked,
            AutoSilence = _autoSilenceCheck.Checked,
            MinimizeToTrayOnClose = _minimizeToTrayCheck.Checked,
            CommandRows = CaptureDInputRows(),
            SilenceDevices = CaptureSilenceRows()
        };
    }

    private List<PresetCommandRow> CaptureDInputRows()
    {
        return _dInputGrid.Rows
            .Cast<DataGridViewRow>()
            .Select(row => new PresetCommandRow
            {
                RowIndex = row.Index,
                Key = CellText(row, "Key"),
                Type = CellText(row, "Type"),
                Index = CellText(row, "Index"),
                VidPid = CellText(row, "VidPid"),
                DInputIndex = CellText(row, "DInputIndex"),
                DInputInvert = CellText(row, "DInputInvert")
            })
            .ToList();
    }

    private List<PresetSilenceDevice> CaptureSilenceRows()
    {
        return _silenceGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => !string.IsNullOrWhiteSpace(CellText(row, "InstanceId")) || !string.IsNullOrWhiteSpace(CellText(row, "VidPid")))
            .Select(row => new PresetSilenceDevice
            {
                InstanceId = CellText(row, "InstanceId"),
                VidPid = CellText(row, "VidPid"),
                DeviceName = CellText(row, "Device"),
                Visible = CellBool(row, "Visible")
            })
            .ToList();
    }

    private void ApplyDInputPresetRows(IReadOnlyList<PresetCommandRow> rows)
    {
        if (rows.Count == 0 || _dInputGrid.Rows.Count == 0)
        {
            return;
        }

        var usedRows = new HashSet<int>();
        foreach (var presetRow in rows)
        {
            var target = FindDInputPresetTarget(presetRow, usedRows);
            if (target is null)
            {
                continue;
            }

            usedRows.Add(target.Index);
            target.Cells["VidPid"].Value = presetRow.VidPid;
            target.Cells["DInputIndex"].Value = presetRow.DInputIndex;
            target.Cells["DInputInvert"].Value = presetRow.DInputInvert;
        }

        HighlightDInputRows();
    }

    private DataGridViewRow? FindDInputPresetTarget(PresetCommandRow presetRow, HashSet<int> usedRows)
    {
        if (presetRow.RowIndex >= 0
            && presetRow.RowIndex < _dInputGrid.Rows.Count
            && !usedRows.Contains(presetRow.RowIndex)
            && DInputRowsMatch(_dInputGrid.Rows[presetRow.RowIndex], presetRow))
        {
            return _dInputGrid.Rows[presetRow.RowIndex];
        }

        return _dInputGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row => !usedRows.Contains(row.Index) && DInputRowsMatch(row, presetRow));
    }

    private static bool DInputRowsMatch(DataGridViewRow row, PresetCommandRow presetRow)
    {
        return CellText(row, "Key").Equals(presetRow.Key, StringComparison.OrdinalIgnoreCase)
            && CellText(row, "Type").Equals(presetRow.Type, StringComparison.OrdinalIgnoreCase)
            && CellText(row, "Index").Equals(presetRow.Index, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplySilencePresetRows(IReadOnlyList<PresetSilenceDevice> devices)
    {
        if (devices.Count == 0 || _silenceGrid.Rows.Count == 0)
        {
            return;
        }

        var byInstanceId = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.InstanceId))
            .GroupBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var byVidPid = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.VidPid))
            .GroupBy(device => device.VidPid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (DataGridViewRow row in _silenceGrid.Rows)
        {
            var instanceId = CellText(row, "InstanceId");
            var vidPid = CellText(row, "VidPid");
            if (byInstanceId.TryGetValue(instanceId, out var instanceMatch)
                || byVidPid.TryGetValue(vidPid, out instanceMatch))
            {
                row.Cells["Visible"].Value = instanceMatch.Visible;
            }
        }

        HighlightSilenceRows();
    }

    private void SetRoleComboTexts(AppPreset preset)
    {
        _primaryDeviceCombo.Text = preset.WheelbaseDevice ?? "";
        _pedalsDeviceCombo.Text = preset.PedalsDevice ?? "";
        _shifterDeviceCombo.Text = preset.ShifterDevice ?? "";
        _handbrakeDeviceCombo.Text = preset.HandbrakeDevice ?? "";
    }

    private void SelectProfileByEntryName(string? entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return;
        }

        foreach (var item in _profileCombo.Items)
        {
            if (item is ProfileComboItem profileItem
                && profileItem.Profile.EntryName.Equals(entryName, StringComparison.OrdinalIgnoreCase))
            {
                _profileCombo.SelectedItem = item;
                return;
            }
        }

        Log("Preset XML profile was not found in the selected input zip: " + entryName);
    }

    private void SelectTemplateByEntryName(string? entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return;
        }

        foreach (var item in _ffbTemplateCombo.Items)
        {
            if (item is TemplateComboItem templateItem
                && templateItem.Template.EntryName.Equals(entryName, StringComparison.OrdinalIgnoreCase))
            {
                _ffbTemplateCombo.SelectedItem = item;
                return;
            }
        }

        Log("Preset FFB INI template was not found in the selected wheel tune zip: " + entryName);
    }

    private PresetComboItem SelectedPresetOrThrow()
    {
        return _presetCombo.SelectedItem as PresetComboItem
            ?? throw new InvalidOperationException("Select or save a preset first.");
    }

    private void SelectPresetComboPath(string? presetPath)
    {
        if (string.IsNullOrWhiteSpace(presetPath))
        {
            _presetCombo.SelectedIndex = -1;
            return;
        }

        for (var i = 0; i < _presetCombo.Items.Count; i++)
        {
            if (_presetCombo.Items[i] is PresetComboItem item && PresetStore.PathsEqual(item.FilePath, presetPath))
            {
                _presetCombo.SelectedIndex = i;
                return;
            }
        }
    }

    private void CommitPresetGridEdits()
    {
        if (_dInputGrid.IsCurrentCellDirty)
        {
            _dInputGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        if (_silenceGrid.IsCurrentCellDirty)
        {
            _silenceGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        _dInputGrid.EndEdit();
        _silenceGrid.EndEdit();
    }

    private void PopulateSilenceGrid()
    {
        var existingVisibility = _silenceGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => !string.IsNullOrWhiteSpace(CellText(row, "InstanceId")))
            .ToDictionary(row => CellText(row, "InstanceId"), row => CellBool(row, "Visible"), StringComparer.OrdinalIgnoreCase);
        var persistedHidden = new HashSet<string>(DeviceSilencer.LoadSilencedDeviceIds(), StringComparer.OrdinalIgnoreCase);

        _silenceGrid.Rows.Clear();
        foreach (var device in _controllerDevices)
        {
            var role = GetDeviceRole(device);
            var defaultVisible = !string.IsNullOrWhiteSpace(role);
            var visible = existingVisibility.TryGetValue(device.InstanceId, out var preserved)
                ? preserved
                : defaultVisible && !persistedHidden.Contains(device.InstanceId);
            var defaultText = !string.IsNullOrWhiteSpace(role)
                ? "Mapped role"
                : "Extra controller";
            _silenceGrid.Rows.Add(
                visible,
                FormatDeviceStatus(DeviceSilencer.GetDeviceStatus(device.InstanceId)),
                device.Name,
                device.VidPid.ToXmlString(),
                role,
                defaultText,
                device.InstanceId);
        }

        HighlightSilenceRows();
    }

    private void CheckSilenceDeviceStatuses()
    {
        RunGuarded(() =>
        {
            RefreshSilenceDeviceStatuses();
            Log($"Checked current status for {_silenceGrid.Rows.Count} controller devices.");
        });
    }

    private void RefreshSilenceDeviceStatuses()
    {
        foreach (DataGridViewRow row in _silenceGrid.Rows)
        {
            var instanceId = CellText(row, "InstanceId");
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                row.Cells["Status"].Value = "Unknown";
                continue;
            }

            row.Cells["Status"].Value = FormatDeviceStatus(DeviceSilencer.GetDeviceStatus(instanceId));
        }

        HighlightSilenceRows();
    }

    private static string FormatDeviceStatus(DeviceSilencerStatus status)
    {
        return status switch
        {
            DeviceSilencerStatus.Enabled => "Enabled",
            DeviceSilencerStatus.Disabled => "Disabled",
            _ => "Unknown"
        };
    }

    private void AutoConfigureSilenceRows()
    {
        foreach (DataGridViewRow row in _silenceGrid.Rows)
        {
            var role = CellText(row, "Role");
            row.Cells["Visible"].Value = !string.IsNullOrWhiteSpace(role);
        }

        HighlightSilenceRows();
    }

    private void AutoAssignEmptyRolesFromDeviceNames()
    {
        AssignRoleIfEmpty(_primaryDeviceCombo, LooksLikeWheelbase);
        AssignRoleIfEmpty(_pedalsDeviceCombo, LooksLikePedals);
        AssignRoleIfEmpty(_shifterDeviceCombo, LooksLikeShifter);
        AssignRoleIfEmpty(_handbrakeDeviceCombo, LooksLikeHandbrake);
    }

    private void AssignRoleIfEmpty(ComboBox combo, Func<DeviceInfo, bool> predicate)
    {
        if (!string.IsNullOrWhiteSpace(combo.Text))
        {
            return;
        }

        var device = VisibleMappingDevices().FirstOrDefault(predicate);
        if (device is not null)
        {
            combo.Text = new DeviceComboItem(device).ToString();
        }
    }

    private static bool LooksLikeWheelbase(DeviceInfo device)
    {
        var name = device.Name;
        return name.Contains("wheel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("wheelbase", StringComparison.OrdinalIgnoreCase)
            || name.Contains("base", StringComparison.OrdinalIgnoreCase)
            || name.Contains("SIMAGIC Alpha", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Fanatec", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Logitech G", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Thrustmaster", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Moza", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Simucube", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePedals(DeviceInfo device)
    {
        var name = device.Name;
        return name.Contains("pedal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("P1000", StringComparison.OrdinalIgnoreCase)
            || name.Contains("P2000", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pro X", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeShifter(DeviceInfo device)
    {
        var name = device.Name;
        return name.Contains("shifter", StringComparison.OrdinalIgnoreCase)
            || name.Contains("hifter", StringComparison.OrdinalIgnoreCase)
            || name.Contains("H-Shifter", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHandbrake(DeviceInfo device)
    {
        var name = device.Name;
        return name.Contains("handbrake", StringComparison.OrdinalIgnoreCase)
            || name.Contains("hand brake", StringComparison.OrdinalIgnoreCase)
            || name.Contains("HB", StringComparison.OrdinalIgnoreCase);
    }

    private string GetDeviceRole(DeviceInfo device)
    {
        var roles = new List<string>();
        AddRoleIfMatches(roles, "Wheelbase/FFB", _primaryDeviceCombo.Text, device);
        AddRoleIfMatches(roles, "Pedals", _pedalsDeviceCombo.Text, device);
        AddRoleIfMatches(roles, "Shifter", _shifterDeviceCombo.Text, device);
        AddRoleIfMatches(roles, "Handbrake", _handbrakeDeviceCombo.Text, device);
        return string.Join(", ", roles);
    }

    private static void AddRoleIfMatches(List<string> roles, string roleName, string roleValue, DeviceInfo device)
    {
        if (VidPid.TryParse(roleValue, out var vidPid) && vidPid == device.VidPid)
        {
            roles.Add(roleName);
        }
    }

    private void UseSelectedDevice(ComboBox target)
    {
        if (_devicesGrid.SelectedRows.Count == 0)
        {
            return;
        }

        if (_devicesGrid.SelectedRows[0].Tag is not DeviceInfo device)
        {
            return;
        }

        target.Text = new DeviceComboItem(device).ToString();
    }

    private void PopulateDInputGrid()
    {
        _dInputGrid.Rows.Clear();
        if (_currentDocument is null)
        {
            return;
        }

        var values = XmlProfileEditor.GetInputValues(_currentDocument)
            .OrderBy(v => v.Key)
            .ThenBy(v => v.VidPid?.ToXmlString())
            .ToList();

        foreach (var value in values)
        {
            var rowIndex = _dInputGrid.Rows.Add(
                value.Key,
                value.InputType,
                value.Index,
                value.InvertAxis,
                value.VidPid?.ToXmlString() ?? "",
                value.Element.Attribute("DInputIndex")?.Value ?? "",
                value.Element.Attribute("DInputInvertAxis")?.Value ?? "");
            _dInputGrid.Rows[rowIndex].Tag = value;
        }

        HighlightDInputRows();
    }

    private void SetSelectedCommandRowsVidPid(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var targetVidPid = ParseVidPid(target, "Role device");
        foreach (DataGridViewRow row in _dInputGrid.SelectedRows)
        {
            row.Cells["VidPid"].Value = targetVidPid.ToXmlString();
        }

        UpdateConfigurationVisuals();
    }

    private void GenerateFiles()
    {
        RunGuarded(() =>
        {
            PrepareForGeneration();
            ValidateGenerationInputs();

            var selectedProfile = _selectedProfile ?? throw new InvalidOperationException("Choose an XML profile first.");
            var sourceInputZip = _inputZipText.Text;
            var sourceWheelZip = _wheelZipText.Text;
            var document = ZipProfileReader.ReadXml(sourceInputZip, selectedProfile.EntryName);
            var profileElement = XmlProfileEditor.GetProfileElement(document);
            var primary = ParseVidPid(_primaryDeviceCombo.Text, "Primary wheelbase");
            var replacements = CreateWheelbaseReplacements(document, primary);

            ApplyDInputGrid(document);
            XmlProfileEditor.ApplyVidPidReplacements(document, replacements);
            XmlProfileEditor.SetPrimaryAndFfb(profileElement, primary, primary);

            if (_setFfbInvertCheck.Checked)
            {
                profileElement.SetAttributeValue("DInputFFBInvertAxis", _ffbInvertValueCheck.Checked ? "true" : "false");
            }
            var destinationProfileName = selectedProfile.EntryName;
            if (!_patchInPlaceCheck.Checked)
            {
                destinationProfileName = NormalizeXmlFileName(_newProfileNameText.Text);
                XmlProfileEditor.AssignNewProfileId(profileElement);
            }

            var patchedXml = XmlProfileEditor.ToUtf8Xml(document);

            var outputFolder = GetOutputFolder();
            Directory.CreateDirectory(outputFolder);
            var previewFolder = outputFolder;
            Directory.CreateDirectory(previewFolder);
            var xmlPreviewPath = Path.Combine(previewFolder, Path.GetFileName(destinationProfileName));

            var template = (_ffbTemplateCombo.SelectedItem as TemplateComboItem)?.Template
                ?? throw new InvalidOperationException("Choose an FFB INI template.");
            var templateIni = ZipWheelTuneReader.ReadText(sourceWheelZip, template.EntryName);
            var patchedIni = IniEditor.SetVendorProduct(templateIni, primary);
            var destinationIniName = $"ControllerFFB-0X{primary.Compact}.ini";
            var iniPreviewPath = Path.Combine(previewFolder, destinationIniName);

            _generatedInputZip = Path.Combine(outputFolder, InputZipName);
            _generatedWheelZip = Path.Combine(outputFolder, WheelZipName);
            if (PathsEqual(sourceInputZip, _generatedInputZip) || PathsEqual(sourceWheelZip, _generatedWheelZip))
            {
                throw new InvalidOperationException("The generated output folder would overwrite the selected source zips. Choose game source zips outside the Horizon SimTool output folder.");
            }

            if (!ConfirmOverwriteGeneratedFiles(xmlPreviewPath, iniPreviewPath, _generatedInputZip, _generatedWheelZip))
            {
                _generatedInputZip = null;
                _generatedWheelZip = null;
                SetStatusLabel(_generatedStatusLabel, "Not generated", configured: false);
                Log("Generate cancelled. Existing generated files were not overwritten.");
                return;
            }

            File.WriteAllBytes(xmlPreviewPath, patchedXml);
            File.WriteAllText(iniPreviewPath, patchedIni, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            ZipWriter.WriteInputMappingZip(sourceInputZip, _generatedInputZip, selectedProfile.EntryName, destinationProfileName, patchedXml);
            ZipWriter.WriteWheelTuneZip(sourceWheelZip, _generatedWheelZip, template.EntryName, destinationIniName, patchedIni, Encoding.UTF8);

            VerifyGeneratedZipsCore();

            SetStatusLabel(_generatedStatusLabel, "Generated", configured: true);
            Log("Generated XML preview: " + xmlPreviewPath);
            Log("Generated INI preview: " + iniPreviewPath);
            Log("Generated zips in: " + outputFolder);
        });
    }

    private void PrepareForGeneration()
    {
        if (string.IsNullOrWhiteSpace(_outputFolderText.Text))
        {
            _outputFolderText.Text = DefaultOutputFolder();
        }
    }

    private void ApplyDInputGrid(XDocument document)
    {
        var values = XmlProfileEditor.GetInputValues(document)
            .OrderBy(v => v.Key)
            .ThenBy(v => v.VidPid?.ToXmlString())
            .ToList();

        for (var i = 0; i < _dInputGrid.Rows.Count && i < values.Count; i++)
        {
            var row = _dInputGrid.Rows[i];
            var vidPidText = CellText(row, "VidPid");
            var dInputIndex = CellText(row, "DInputIndex");
            var dInputInvert = CellText(row, "DInputInvert");

            if (!string.IsNullOrWhiteSpace(vidPidText))
            {
                var commandVidPid = ParseVidPid(vidPidText, $"Command VID/PID for {CellText(row, "Key")}");
                values[i].Element.SetAttributeValue("VidPid", commandVidPid.ToXmlString());
            }

            if (!string.IsNullOrWhiteSpace(dInputIndex))
            {
                if (!int.TryParse(dInputIndex, out _))
                {
                    throw new InvalidOperationException($"DInputIndex must be numeric for {CellText(row, "Key")}.");
                }

                values[i].Element.SetAttributeValue("DInputIndex", dInputIndex.Trim());
            }
            else
            {
                values[i].Element.Attribute("DInputIndex")?.Remove();
            }

            if (!string.IsNullOrWhiteSpace(dInputInvert))
            {
                if (!TryParseBool(dInputInvert, out var boolValue))
                {
                    throw new InvalidOperationException($"DInputInvertAxis must be true or false for {CellText(row, "Key")}.");
                }

                values[i].Element.SetAttributeValue("DInputInvertAxis", boolValue ? "true" : "false");
            }
            else
            {
                values[i].Element.Attribute("DInputInvertAxis")?.Remove();
            }
        }
    }

    private static Dictionary<VidPid, VidPid> CreateWheelbaseReplacements(XDocument document, VidPid wheelbase)
    {
        var replacements = new Dictionary<VidPid, VidPid>();
        foreach (var oldValue in XmlProfileEditor.GetVidPidValues(document))
        {
            replacements[oldValue] = wheelbase;
        }

        return replacements;
    }

    private bool ConfirmOverwriteGeneratedFiles(params string[] paths)
    {
        var existing = paths
            .Where(File.Exists)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existing.Count == 0)
        {
            return true;
        }

        var message = "Generated files already exist:\n\n"
            + string.Join(Environment.NewLine, existing.Select(name => "  " + name))
            + "\n\nOverwrite these files?";
        return MessageBox.Show(
            message,
            "Overwrite generated files?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private void VerifyGeneratedZips()
    {
        RunGuarded(VerifyGeneratedZipsCore);
    }

    private void VerifyGeneratedZipsCore()
    {
        if (string.IsNullOrWhiteSpace(_generatedInputZip) || string.IsNullOrWhiteSpace(_generatedWheelZip))
        {
            throw new InvalidOperationException("Generate files before verifying.");
        }

        var inputResult = ZipVerifier.VerifyStoreOnlyTopLevel(_generatedInputZip);
        var wheelResult = ZipVerifier.VerifyStoreOnlyTopLevel(_generatedWheelZip);
        if (!inputResult.Passed || !wheelResult.Passed)
        {
            throw new InvalidOperationException("One or more generated zip entries are nested or compressed.");
        }

        SetStatusLabel(_generatedStatusLabel, "Generated + verified", configured: true);
        Log($"Verified generated zips: {inputResult.EntryCount} input entries, {wheelResult.EntryCount} wheel tune entries, all Store/no compression.");
    }

    private void InstallGeneratedFiles()
    {
        RunGuarded(() =>
        {
            if (string.IsNullOrWhiteSpace(_generatedInputZip) || string.IsNullOrWhiteSpace(_generatedWheelZip))
            {
                throw new InvalidOperationException("Generate files before installing.");
            }

            ValidateSourcePaths();
            VerifyGeneratedZipsCore();
            var mediaFolder = _mediaFolderText.Text.Trim();
            var answer = MessageBox.Show(
                "This will create/check HST-BACKUP and then install the generated zips into the game media folder.\n\nContinue?",
                "Install generated files",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                Log("Install cancelled.");
                return;
            }

            var backupAlreadyExisted = GameFileInstaller.BackupExists(mediaFolder);
            var backupFolder = GameFileInstaller.BackupAndInstall(mediaFolder, _generatedInputZip, _generatedWheelZip);
            Log("Installed generated zips to: " + mediaFolder);
            Log(backupAlreadyExisted ? "Existing HST-BACKUP reused: " + backupFolder : "Created HST-BACKUP: " + backupFolder);
            MessageBox.Show("Install complete.\n\nBackups:\n" + backupFolder, "Horizon SimTool", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void BrowseMediaFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Forza Horizon 6 media folder",
            SelectedPath = Directory.Exists(_mediaFolderText.Text) ? _mediaFolderText.Text : ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _mediaFolderText.Text = dialog.SelectedPath;
        }
    }

    private void BrowseInputZip()
    {
        BrowseZip(_inputZipText, InputZipName);
    }

    private void BrowseWheelZip()
    {
        BrowseZip(_wheelZipText, WheelZipName);
    }

    private void BrowseZip(TextBox target, string fileName)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select " + fileName,
            Filter = "Zip files (*.zip)|*.zip|All files (*.*)|*.*",
            FileName = fileName,
            InitialDirectory = Directory.Exists(_mediaFolderText.Text) ? _mediaFolderText.Text : Environment.CurrentDirectory
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _useBackupSourcesCheck.Checked = false;
            target.Text = dialog.FileName;
        }
    }

    private void OpenOutputFolder()
    {
        RunGuarded(() =>
        {
            var outputFolder = GetOutputFolder();
            Directory.CreateDirectory(outputFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = outputFolder,
                UseShellExecute = true
            });
        });
    }

    private void UpdateZipPathsFromMediaFolder()
    {
        if (_updatingMediaDefaults)
        {
            return;
        }

        _updatingMediaDefaults = true;
        try
        {
            var mediaFolder = _mediaFolderText.Text.Trim();
            if (Directory.Exists(mediaFolder))
            {
                var backupInput = GameFileInstaller.GetBackupInputZipPath(mediaFolder);
                var backupWheel = GameFileInstaller.GetBackupWheelZipPath(mediaFolder);
                var input = _useBackupSourcesCheck.Checked && File.Exists(backupInput)
                    ? backupInput
                    : Path.Combine(mediaFolder, InputZipName);
                var wheel = _useBackupSourcesCheck.Checked && File.Exists(backupWheel)
                    ? backupWheel
                    : Path.Combine(mediaFolder, WheelZipName);

                if (File.Exists(input))
                {
                    _inputZipText.Text = input;
                }

                if (File.Exists(wheel))
                {
                    _wheelZipText.Text = wheel;
                }
            }

            UpdateConfigurationVisuals();
        }
        finally
        {
            _updatingMediaDefaults = false;
        }
    }

    private void TryCreateBackupForMediaFolder(string mediaFolder)
    {
        try
        {
            var backupFolder = GameFileInstaller.EnsureBackup(mediaFolder);
            Log("HST-BACKUP ready: " + backupFolder);
        }
        catch (Exception ex)
        {
            Log("Backup not created yet: " + ex.Message);
        }
    }

    private void CreateBackup()
    {
        RunGuarded(() =>
        {
            ValidateMediaFolder();
            var mediaFolder = _mediaFolderText.Text.Trim();
            if (GameFileInstaller.BackupExists(mediaFolder))
            {
                var answer = MessageBox.Show(
                    "HST-BACKUP already contains backup files.\n\nOverwrite the existing backup files with the current game files?",
                    "Overwrite existing backup?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    Log("Backup overwrite cancelled.");
                    return;
                }
            }

            var backupFolder = GameFileInstaller.CreateOrOverwriteBackup(mediaFolder);
            UpdateZipPathsFromMediaFolder();
            Log("HST-BACKUP ready: " + backupFolder);
            MessageBox.Show("Backup ready:\n\n" + backupFolder, "Horizon SimTool", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void RestoreBackups()
    {
        RunGuarded(() =>
        {
            ValidateMediaFolder();
            var answer = MessageBox.Show(
                "This will restore inputmappingprofiles.zip and wheeltunablesettingspc.zip from HST-BACKUP into the game media folder.\n\nContinue?",
                "Restore HST-BACKUP",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                Log("Restore cancelled.");
                return;
            }

            var backupFolder = GameFileInstaller.RestoreBackup(_mediaFolderText.Text.Trim());
            UpdateZipPathsFromMediaFolder();
            LoadProfilesAndTemplates();
            Log("Restored game zips from: " + backupFolder);
            MessageBox.Show("Restore complete.\n\nSource:\n" + backupFolder, "Horizon SimTool", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void OpenBackupFolder()
    {
        RunGuarded(() =>
        {
            ValidateMediaFolder();
            var backupFolder = GameFileInstaller.GetBackupFolder(_mediaFolderText.Text.Trim());
            Directory.CreateDirectory(backupFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = backupFolder,
                UseShellExecute = true
            });
        });
    }

    private void CheckSilenceBackend()
    {
        RunGuarded(() =>
        {
            var elevated = DeviceSilencer.IsRunningElevated();
            SetStatusLabel(_silenceStatusLabel, elevated ? "Built-in ready" : "Run as admin", elevated);
            Log(elevated
                ? $"Built-in controller silence ready. {_controllerDevices.Count} controller devices available."
                : "Controller silence needs Administrator rights to disable and restore devices system-wide.");
        });
    }

    private void ApplySilence()
    {
        RunGuarded(() =>
        {
            if (_silenceGrid.IsCurrentCellDirty)
            {
                _silenceGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }

            var hiddenRows = _silenceGrid.Rows
                .Cast<DataGridViewRow>()
                .Where(row => !CellBool(row, "Visible"))
                .ToList();
            var visibleRows = _silenceGrid.Rows
                .Cast<DataGridViewRow>()
                .Where(row => CellBool(row, "Visible"))
                .ToList();

            if (hiddenRows.Count == 0)
            {
                throw new InvalidOperationException("No controller devices are marked hidden. Uncheck at least one extra controller or use Auto configure.");
            }

            if (!DeviceSilencer.IsRunningElevated())
            {
                throw new InvalidOperationException("Run Horizon SimTool as Administrator to disable controller devices system-wide.");
            }

            var answer = MessageBox.Show(
                "Unchecked controller devices will be disabled system-wide until you press Stop Silence or Restore previous.\n\nContinue?",
                "Apply Silence",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                Log("Silence cancelled.");
                return;
            }

            _silencedDeviceInstanceIds.UnionWith(DeviceSilencer.LoadSilencedDeviceIds());
            foreach (var row in visibleRows)
            {
                var instanceId = CellText(row, "InstanceId");
                if (!string.IsNullOrWhiteSpace(instanceId) && _silencedDeviceInstanceIds.Contains(instanceId))
                {
                    DeviceSilencer.EnableDevice(instanceId);
                    _silencedDeviceInstanceIds.Remove(instanceId);
                }
            }

            foreach (var row in hiddenRows)
            {
                var instanceId = CellText(row, "InstanceId");
                if (!string.IsNullOrWhiteSpace(instanceId))
                {
                    DeviceSilencer.DisableDevice(instanceId);
                    _silencedDeviceInstanceIds.Add(instanceId);
                }
            }

            DeviceSilencer.SaveSilencedDeviceIds(_silencedDeviceInstanceIds);
            _silenceApplied = true;
            SetStatusLabel(_silenceStatusLabel, "Silence active", configured: true);
            RefreshSilenceDeviceStatuses();
            Log($"Silence active. Disabled controller devices: {_silencedDeviceInstanceIds.Count}.");
        });
    }

    private void StopSilence()
    {
        RunGuarded(() => TryStopSilence(showMessages: true));
    }

    private void RestorePreviousSilence()
    {
        RunGuarded(() => TryStopSilence(showMessages: true));
    }

    private void TryStopSilence(bool showMessages)
    {
        if (!DeviceSilencer.IsRunningElevated())
        {
            throw new InvalidOperationException("Run Horizon SimTool as Administrator to restore disabled controller devices.");
        }

        var restoreIds = new HashSet<string>(DeviceSilencer.LoadSilencedDeviceIds(), StringComparer.OrdinalIgnoreCase);
        restoreIds.UnionWith(_silencedDeviceInstanceIds);
        foreach (var instanceId in restoreIds)
        {
            DeviceSilencer.EnableDevice(instanceId);
        }

        var restoredCount = restoreIds.Count;
        _silencedDeviceInstanceIds.Clear();
        DeviceSilencer.ClearSilencedDeviceIds();
        _silenceApplied = false;
        SetStatusLabel(_silenceStatusLabel, "Not active", configured: false);
        RefreshDevices();
        Log($"Silence stopped. Restored controller devices: {restoredCount}.");
        if (showMessages)
        {
            MessageBox.Show("Silence stopped.\n\nRestored controller devices: " + restoredCount, "Horizon SimTool", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void SelectBestFfbTemplate(VidPid? fallbackVidPid = null)
    {
        if (_ffbTemplateCombo.Items.Count == 0)
        {
            return;
        }

        if (VidPid.TryParse(_primaryDeviceCombo.Text, out var wheelbase) && TrySelectFfbTemplate(wheelbase))
        {
            return;
        }

        if (fallbackVidPid.HasValue && TrySelectFfbTemplate(fallbackVidPid.Value))
        {
            return;
        }

        if (_ffbTemplateCombo.SelectedIndex < 0)
        {
            _ffbTemplateCombo.SelectedIndex = 0;
        }
    }

    private bool TrySelectFfbTemplate(VidPid vidPid)
    {
        for (var i = 0; i < _ffbTemplateCombo.Items.Count; i++)
        {
            if (_ffbTemplateCombo.Items[i] is TemplateComboItem item
                && item.Template.EntryName.Contains(vidPid.Compact, StringComparison.OrdinalIgnoreCase))
            {
                _ffbTemplateCombo.SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    private void SetComboTextForVidPid(ComboBox combo, VidPid? vidPid)
    {
        if (!vidPid.HasValue)
        {
            combo.Text = "";
            return;
        }

        foreach (var item in combo.Items)
        {
            if (item is DeviceComboItem deviceItem && deviceItem.Device.VidPid == vidPid.Value)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.Text = vidPid.Value.ToXmlString();
    }

    private void ValidateGenerationInputs()
    {
        ValidateSourcePaths();
        if (_profileCombo.SelectedItem is not ProfileComboItem)
        {
            throw new InvalidOperationException("Choose an XML profile.");
        }

        ParseVidPid(_primaryDeviceCombo.Text, "Primary wheelbase");
        if (_ffbTemplateCombo.SelectedItem is not TemplateComboItem)
        {
            throw new InvalidOperationException("Choose an FFB INI template.");
        }
    }

    private void ValidateSourcePaths()
    {
        ValidateMediaFolder();

        if (!File.Exists(_inputZipText.Text))
        {
            throw new InvalidOperationException("Select inputmappingprofiles.zip.");
        }

        if (!File.Exists(_wheelZipText.Text))
        {
            throw new InvalidOperationException("Select wheeltunablesettingspc.zip.");
        }
    }

    private void ValidateMediaFolder()
    {
        if (!Directory.Exists(_mediaFolderText.Text))
        {
            throw new InvalidOperationException("Select the game's media folder.");
        }
    }

    private string GetOutputFolder()
    {
        if (string.IsNullOrWhiteSpace(_outputFolderText.Text))
        {
            _outputFolderText.Text = DefaultOutputFolder();
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(_outputFolderText.Text.Trim()));
    }

    private static string DefaultOutputFolder() => Path.Combine(AppContext.BaseDirectory, "output");

    private static VidPid ParseVidPid(string value, string label)
    {
        if (!VidPid.TryParse(value, out var vidPid))
        {
            throw new InvalidOperationException($"{label} must contain a VID/PID such as 0x36700501 or VID_3670&PID_0501.");
        }

        return vidPid;
    }

    private static string NormalizeXmlFileName(string value)
    {
        var fileName = string.IsNullOrWhiteSpace(value)
            ? "DefaultRawGameControllerMappingProfileCodexCustom.xml"
            : value.Trim();

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("The new XML filename contains invalid characters.");
        }

        return fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".xml";
    }

    private static int FindDefaultProfileIndex(IReadOnlyList<ZipProfileInfo> profiles)
    {
        for (var i = 0; i < profiles.Count; i++)
        {
            var primary = profiles[i].PrimaryDeviceVidPid;
            if (primary.HasValue && !primary.Value.Compact.Equals("FFFFFFFF", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static string CellText(DataGridViewRow row, string columnName)
    {
        return row.Cells[columnName].Value?.ToString() ?? "";
    }

    private static bool CellBool(DataGridViewRow row, string columnName)
    {
        var value = row.Cells[columnName].Value;
        return value is bool boolValue && boolValue;
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(left.Trim())),
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(right.Trim())),
            StringComparison.OrdinalIgnoreCase);
    }

    private static Icon? TryLoadApplicationIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseBool(string value, out bool result)
    {
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("n", StringComparison.OrdinalIgnoreCase)
            || value.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private string? PromptForPresetName(string title, string currentName)
    {
        using var dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(420, 132),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        layout.Controls.Add(new Label
        {
            Text = "Preset name",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var nameText = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = currentName
        };
        layout.Controls.Add(nameText, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Width = 88
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 88
        };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, 2);

        dialog.Controls.Add(layout);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;
        nameText.SelectAll();

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        var presetName = nameText.Text.Trim();
        if (string.IsNullOrWhiteSpace(presetName))
        {
            MessageBox.Show("Preset name cannot be blank.", "Horizon SimTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        return presetName;
    }

    private void RunGuarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Error";
            Log("Error: " + ex.Message);
            MessageBox.Show(ex.Message, "Horizon SimTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Log(string message)
    {
        _logText.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _statusLabel.Text = message;
    }

    private static GroupBox CreateGroup(string title)
    {
        return new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(246, 248, 251),
            ForeColor = Color.FromArgb(33, 43, 54)
        };
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(45, 55, 72)
        };
    }

    private static Label CreateHint(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(12, 8, 0, 0),
            ForeColor = Color.FromArgb(91, 105, 125),
            MaximumSize = new Size(920, 0)
        };
    }

    private static Button CreateButton(string text, Action onClick, int width = 112)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(width, 32),
            Padding = new Padding(10, 3, 10, 3),
            FlatStyle = FlatStyle.System,
            Margin = new Padding(4)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Button CreateCellButton(string text, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            FlatStyle = FlatStyle.System,
            Margin = new Padding(4),
            MinimumSize = Size.Empty,
            TextAlign = ContentAlignment.MiddleCenter
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private sealed record DeviceComboItem(DeviceInfo Device)
    {
        public override string ToString()
        {
            return $"{Device.Name} ({Device.VidPid.ToXmlString()})";
        }
    }

    private sealed record ProfileComboItem(ZipProfileInfo Profile)
    {
        public override string ToString()
        {
            var primary = Profile.PrimaryDeviceVidPid?.ToXmlString() ?? "no VID/PID";
            var name = string.IsNullOrWhiteSpace(Profile.UserFacingName) ? "" : " - " + Profile.UserFacingName;
            return $"{Profile.EntryName} ({primary}){name}";
        }
    }

    private sealed record TemplateComboItem(ZipWheelTuneInfo Template)
    {
        public override string ToString() => Template.EntryName;
    }

    private sealed record PresetComboItem(string FilePath, string Name, bool IsDefault)
    {
        public override string ToString() => IsDefault ? Name + " (default)" : Name;
    }
}

internal sealed class AppPreset
{
    public int Version { get; set; } = 1;
    public string PresetName { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; }
    public string MediaFolder { get; set; } = "";
    public string InputZip { get; set; } = "";
    public string WheelTuneZip { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public bool UseBackupSources { get; set; } = true;
    public bool ControllerDevicesOnly { get; set; } = true;
    public bool AdvancedSettingsVisible { get; set; }
    public string XmlProfileEntryName { get; set; } = "";
    public string FfbTemplateEntryName { get; set; } = "";
    public string WheelbaseDevice { get; set; } = "";
    public string PedalsDevice { get; set; } = "";
    public string ShifterDevice { get; set; } = "";
    public string HandbrakeDevice { get; set; } = "";
    public bool PatchSelectedXmlInPlace { get; set; } = true;
    public string NewProfileName { get; set; } = "";
    public bool SetFfbInvert { get; set; }
    public bool FfbInvertValue { get; set; }
    public bool AutoSilence { get; set; } = true;
    public bool MinimizeToTrayOnClose { get; set; }
    public List<PresetCommandRow> CommandRows { get; set; } = new();
    public List<PresetSilenceDevice> SilenceDevices { get; set; } = new();
}

internal sealed class PresetCommandRow
{
    public int RowIndex { get; set; }
    public string Key { get; set; } = "";
    public string Type { get; set; } = "";
    public string Index { get; set; } = "";
    public string VidPid { get; set; } = "";
    public string DInputIndex { get; set; } = "";
    public string DInputInvert { get; set; } = "";
}

internal sealed class PresetSilenceDevice
{
    public string InstanceId { get; set; } = "";
    public string VidPid { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public bool Visible { get; set; }
}

internal static class PresetStore
{
    private const string FolderName = "HST-PRESETS";
    private const string Extension = ".hstpreset.json";
    private const string DefaultMarkerName = "_default.txt";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Folder => Path.Combine(AppContext.BaseDirectory, FolderName);

    public static IReadOnlyList<string> GetPresetFiles()
    {
        return Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, "*" + Extension)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : Array.Empty<string>();
    }

    public static string GetPresetPath(string presetName)
    {
        return Path.Combine(Folder, SanitizeFileName(presetName) + Extension);
    }

    public static AppPreset LoadPreset(string path)
    {
        var preset = JsonSerializer.Deserialize<AppPreset>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("The preset file is empty or invalid.");
        preset.CommandRows ??= new List<PresetCommandRow>();
        preset.SilenceDevices ??= new List<PresetSilenceDevice>();
        return preset;
    }

    public static void SavePreset(string path, AppPreset preset)
    {
        Directory.CreateDirectory(Folder);
        preset.Version = Math.Max(1, preset.Version);
        preset.SavedAt = preset.SavedAt == default ? DateTimeOffset.Now : preset.SavedAt;
        File.WriteAllText(path, JsonSerializer.Serialize(preset, JsonOptions), Encoding.UTF8);
    }

    public static string? GetDefaultPresetPath()
    {
        var markerPath = DefaultMarkerPath();
        if (!File.Exists(markerPath))
        {
            return null;
        }

        var fileName = File.ReadAllText(markerPath).Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var path = Path.Combine(Folder, fileName);
        return File.Exists(path) ? path : null;
    }

    public static void SetDefaultPreset(string presetPath)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(DefaultMarkerPath(), Path.GetFileName(presetPath), Encoding.UTF8);
    }

    public static void ClearDefaultIfMatches(string presetPath)
    {
        var defaultPath = GetDefaultPresetPath();
        if (!PathsEqual(defaultPath, presetPath))
        {
            return;
        }

        var markerPath = DefaultMarkerPath();
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }
    }

    public static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string DefaultMarkerPath()
    {
        return Path.Combine(Folder, DefaultMarkerName);
    }

    private static string SanitizeFileName(string presetName)
    {
        var name = string.IsNullOrWhiteSpace(presetName) ? "Preset" : presetName.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "Preset" : name;
    }
}
