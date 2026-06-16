using System.Xml.Linq;
using Windows.Gaming.Input;

namespace HorizonSimTool;

internal enum MappingKind { Button, Axis }

internal sealed record MappingStep(
    string Key,
    string Label,
    string Instructions,
    MappingKind ExpectedKind);

internal sealed record MappedInput(
    string Key,
    string InputType,
    int Index,
    bool InvertAxis = false,
    string DeviceVidPid = "");

internal sealed class DeviceState
{
    public readonly RawGameController Controller;
    public readonly string VidPid;
    public bool[] BaselineButtons = [];
    public double[] BaselineAxes = [];
    public bool[] LastButtons = [];

    public DeviceState(RawGameController rc)
    {
        Controller = rc;
        VidPid = rc.HardwareVendorId.ToString("X4") + rc.HardwareProductId.ToString("X4");
    }
}

internal sealed class WheelMapResult
{
    public List<MappedInput> Inputs { get; } = new();
    public VidPid DeviceVidPid { get; init; }
    public string DeviceName { get; init; } = "";
    public string ProfileName { get; init; } = "";
}

internal sealed class WheelMapWizard : Form
{
    // Logical key names used throughout — mapped to INPUTCMD_* values in XML builder
    private static readonly MappingStep[] Steps =
    [
        // --- Driving ---
        new("STEER",      "Steering",           "Turn the wheel fully to one side.",                    MappingKind.Axis),
        new("GAS",        "Gas / Throttle",      "Press the throttle pedal all the way down.",          MappingKind.Axis),
        new("BRAKE",      "Brake",               "Press the brake pedal all the way down.",             MappingKind.Axis),
        new("CLUTCH",     "Clutch",              "Press the clutch pedal all the way down. Skip if none.", MappingKind.Axis),
        new("SHIFT_UP",   "Shift Up",            "Pull the right (upshift) paddle.",                    MappingKind.Button),
        new("SHIFT_DOWN", "Shift Down",          "Pull the left (downshift) paddle.",                   MappingKind.Button),
        new("HANDBRAKE",  "Handbrake",           "Pull the handbrake lever or press the button.",       MappingKind.Button),
        new("HORN",       "Horn",                "Press your horn button.",                             MappingKind.Button),

        // --- Menu / UI ---
        new("CONFIRM",    "Confirm / A",         "Press the button used to confirm/select in menus (A equivalent).", MappingKind.Button),
        new("CANCEL",     "Cancel / B",          "Press the button used to cancel/go-back in menus (B equivalent).", MappingKind.Button),
        new("PAUSE",      "Pause / Menu",        "Press the pause or menu button.",                    MappingKind.Button),
        new("BACK",       "Back",                "Press the back/options button (if separate from Cancel).", MappingKind.Button),
        new("BTN_X",      "X Button",            "Press the face button mapped to X (if your wheel has one).", MappingKind.Button),
        new("BTN_Y",      "Y Button",            "Press the face button mapped to Y (if your wheel has one).", MappingKind.Button),

        // --- Navigation ---
        new("NAV_UP",     "Navigate Up",         "Press D-Pad Up or your up navigation button.",       MappingKind.Button),
        new("NAV_DOWN",   "Navigate Down",       "Press D-Pad Down or your down navigation button.",   MappingKind.Button),
        new("NAV_LEFT",   "Navigate Left",       "Press D-Pad Left or your left navigation button.",   MappingKind.Button),
        new("NAV_RIGHT",  "Navigate Right",      "Press D-Pad Right or your right navigation button.", MappingKind.Button),

        // --- Actions ---
        new("REWIND",     "Rewind",              "Press the rewind button.",                           MappingKind.Button),
        new("CAMERA",     "Switch Camera",       "Press the camera toggle button.",                    MappingKind.Button),
        new("ANNA",       "Anna / AI Assist",    "Press the Anna/assistant button.",                   MappingKind.Button),
        new("RADIO",      "Radio Next",          "Press the next-radio-station button.",               MappingKind.Button),
        new("PHOTO",      "Photo Mode",          "Press the photo mode toggle button.",                MappingKind.Button),
        new("QUICKCHAT",  "Quickchat",           "Press the quickchat button.",                        MappingKind.Button),
        new("TELEMETRY",  "Telemetry Toggle",    "Press the telemetry HUD toggle button.",             MappingKind.Button),
        new("MAP",        "Open Map / View",     "Press the button you want to use to open the world map.", MappingKind.Button),
    ];

    private readonly VidPid _deviceVidPid;
    private readonly string _deviceName;
    private readonly List<DeviceState> _devices = new();

    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 50 };

    private bool _baselineReady;
    private DateTime _graceUntil = DateTime.MinValue;
    private bool _needsLateAxisBaseline;
    private bool _waitingForRelease;
    private int _waitingDeviceIdx = -1;
    private bool _releasingAxis;
    private int _releasingDeviceIdx = -1;
    private int _detectedAxisIndex = -1;

    private int _currentStep;
    private readonly List<MappedInput> _results = new();

    // Live debug info shown during axis steps
    private readonly Label _liveLabel = new();

    // UI
    private readonly Label _stepCountLabel = new();
    private readonly Label _commandLabel = new();
    private readonly Label _instructionLabel = new();
    private readonly Label _detectedLabel = new();
    private readonly Button _skipButton = new();
    private readonly Button _cancelButton = new();
    private readonly ProgressBar _progressBar = new();
    private readonly TextBox _nameText = new();

    public WheelMapResult? Result { get; private set; }

    public WheelMapWizard(VidPid deviceVidPid, string deviceName, RawGameController controller)
    {
        _deviceVidPid = deviceVidPid;
        _deviceName = deviceName;
        // Enumerate ALL connected RawGameControllers so separate pedal boxes are also polled
        foreach (var rc in RawGameController.RawGameControllers)
            _devices.Add(new DeviceState(rc));
        if (_devices.Count == 0)
            _devices.Add(new DeviceState(controller));

        Text = "Map Wheel — " + deviceName;
        Width = 580;
        Height = 430;
        MinimumSize = new Size(500, 390);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5F);
        BackColor = Color.FromArgb(13, 17, 23);

        BuildLayout();
        _pollTimer.Tick += PollTick;
        FormClosed += (_, _) => _pollTimer.Stop();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(22, 18, 22, 16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // progress
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // step count
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // command
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // instruction
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // live debug
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // detected
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // name
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons
        Controls.Add(root);

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = Steps.Length;
        _progressBar.Value = 0;
        _progressBar.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(_progressBar, 0, 0);

        _stepCountLabel.AutoSize = true;
        _stepCountLabel.ForeColor = Color.FromArgb(88, 96, 105);
        _stepCountLabel.Margin = new Padding(0, 0, 0, 6);
        root.Controls.Add(_stepCountLabel, 0, 1);

        _commandLabel.AutoSize = true;
        _commandLabel.Font = new Font("Segoe UI Semibold", 14F);
        _commandLabel.ForeColor = Color.FromArgb(240, 246, 252);
        _commandLabel.Margin = new Padding(0, 0, 0, 4);
        root.Controls.Add(_commandLabel, 0, 2);

        _instructionLabel.AutoSize = true;
        _instructionLabel.ForeColor = Color.FromArgb(139, 148, 158);
        _instructionLabel.MaximumSize = new Size(520, 0);
        _instructionLabel.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(_instructionLabel, 0, 3);

        _liveLabel.AutoSize = true;
        _liveLabel.Font = new Font("Consolas", 8.5F);
        _liveLabel.ForeColor = Color.FromArgb(60, 70, 85);
        _liveLabel.Margin = new Padding(0, 0, 0, 6);
        root.Controls.Add(_liveLabel, 0, 4);

        _detectedLabel.AutoSize = true;
        _detectedLabel.Font = new Font("Segoe UI Semibold", 10F);
        _detectedLabel.ForeColor = Color.FromArgb(63, 185, 80);
        root.Controls.Add(_detectedLabel, 0, 5);

        var namePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 6, 0, 0)
        };
        namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        namePanel.Controls.Add(new Label
        {
            Text = "Profile name:",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0),
            ForeColor = Color.FromArgb(139, 148, 158)
        }, 0, 0);
        _nameText.Dock = DockStyle.Fill;
        _nameText.BackColor = Color.FromArgb(30, 36, 46);
        _nameText.ForeColor = Color.FromArgb(201, 209, 217);
        _nameText.Text = SuggestProfileName();
        namePanel.Controls.Add(_nameText, 1, 0);
        root.Controls.Add(namePanel, 0, 6);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 10, 0, 0)
        };

        _skipButton.Text = "Skip";
        _skipButton.AutoSize = true;
        _skipButton.MinimumSize = new Size(80, 32);
        _skipButton.FlatStyle = FlatStyle.System;
        _skipButton.Margin = new Padding(4);
        _skipButton.Click += (_, _) => SkipStep();
        btnPanel.Controls.Add(_skipButton);

        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        _cancelButton.MinimumSize = new Size(80, 32);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.Margin = new Padding(4);
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.Add(_cancelButton);
        root.Controls.Add(btnPanel, 0, 7);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowStep(_currentStep);
        CaptureBaseline();
        _pollTimer.Start();
    }

    private void CaptureBaseline()
    {
        _baselineReady = false;
        _releasingAxis = false;
        _releasingDeviceIdx = -1;
        _detectedAxisIndex = -1;
        _waitingForRelease = false;
        _waitingDeviceIdx = -1;

        foreach (var dev in _devices)
        {
            var buttons = new bool[dev.Controller.ButtonCount];
            var axes = new double[dev.Controller.AxisCount];
            var switches = new GameControllerSwitchPosition[dev.Controller.SwitchCount];
            dev.Controller.GetCurrentReading(buttons, switches, axes);
            dev.BaselineButtons = buttons;
            dev.BaselineAxes = (double[])axes.Clone();
            dev.LastButtons = (bool[])buttons.Clone();
        }
        _graceUntil = DateTime.UtcNow.AddMilliseconds(1200);
        _needsLateAxisBaseline = true;
        _baselineReady = true;
    }

    private void ShowStep(int index)
    {
        if (index >= Steps.Length)
        {
            FinishWizard();
            return;
        }

        var step = Steps[index];
        _progressBar.Value = index;
        _stepCountLabel.Text = $"Step {index + 1} of {Steps.Length}  —  {GetCategory(step.Key)}";
        _commandLabel.Text = step.Label;
        _instructionLabel.Text = step.Instructions;
        _detectedLabel.Text = "";
        _liveLabel.ForeColor = Color.FromArgb(60, 70, 85);
        _liveLabel.Text = step.ExpectedKind == MappingKind.Axis
            ? $"Polling {_devices.Count} device(s)  |  Total axes: {_devices.Sum(d => d.Controller.AxisCount)}  |  Buttons: {_devices.Sum(d => d.Controller.ButtonCount)}"
            : "";
    }

    private static string GetCategory(string key) => key switch
    {
        "STEER" or "GAS" or "BRAKE" or "CLUTCH" or
        "SHIFT_UP" or "SHIFT_DOWN" or "HANDBRAKE" or "HORN" => "Driving",
        "CONFIRM" or "CANCEL" or "PAUSE" or "BACK" or "BTN_X" or "BTN_Y" => "Menu / UI",
        "NAV_UP" or "NAV_DOWN" or "NAV_LEFT" or "NAV_RIGHT" => "Navigation",
        "MAP" => "Navigation",
        _ => "Actions"
    };

    private void PollTick(object? sender, EventArgs e)
    {
        if (!_baselineReady || _currentStep >= Steps.Length || _devices.Count == 0) return;
        if (DateTime.UtcNow < _graceUntil) return;
        // Re-snapshot axis baselines at end of grace period so any residual pedal position is treated as neutral
        if (_needsLateAxisBaseline)
        {
            foreach (var dev in _devices)
            {
                var axes = new double[dev.Controller.AxisCount];
                var btns = new bool[dev.Controller.ButtonCount];
                var sw = new GameControllerSwitchPosition[dev.Controller.SwitchCount];
                dev.Controller.GetCurrentReading(btns, sw, axes);
                dev.BaselineAxes = (double[])axes.Clone();
            }
            _needsLateAxisBaseline = false;
        }

        var step = Steps[_currentStep];

        // Waiting for axis to return near baseline before advancing
        if (_releasingAxis && _releasingDeviceIdx >= 0 && _detectedAxisIndex >= 0)
        {
            var dev = _devices[_releasingDeviceIdx];
            var axes = new double[dev.Controller.AxisCount];
            var btns = new bool[dev.Controller.ButtonCount];
            var sw = new GameControllerSwitchPosition[dev.Controller.SwitchCount];
            dev.Controller.GetCurrentReading(btns, sw, axes);
            if (Math.Abs(axes[_detectedAxisIndex] - dev.BaselineAxes[_detectedAxisIndex]) > 0.25) return;
            _releasingAxis = false;
            _releasingDeviceIdx = -1;
            Advance();
            return;
        }

        // Waiting for buttons on the triggering device to be released
        if (_waitingForRelease && _waitingDeviceIdx >= 0)
        {
            var dev = _devices[_waitingDeviceIdx];
            var btns = new bool[dev.Controller.ButtonCount];
            var sw = new GameControllerSwitchPosition[dev.Controller.SwitchCount];
            var axes = new double[dev.Controller.AxisCount];
            dev.Controller.GetCurrentReading(btns, sw, axes);
            if (btns.Any(b => b)) return;
            _waitingForRelease = false;
            _waitingDeviceIdx = -1;
            Advance();
            return;
        }

        // Detect button press — first rising edge across any device
        if (step.ExpectedKind == MappingKind.Button)
        {
            for (var di = 0; di < _devices.Count; di++)
            {
                var dev = _devices[di];
                var btnCount = dev.Controller.ButtonCount;
                if (btnCount == 0) continue;
                var buttons = new bool[btnCount];
                var sw = new GameControllerSwitchPosition[dev.Controller.SwitchCount];
                var axes = new double[dev.Controller.AxisCount];
                dev.Controller.GetCurrentReading(buttons, sw, axes);

                for (var i = 0; i < btnCount; i++)
                {
                    if (buttons[i] && !dev.LastButtons[i])
                    {
                        _detectedLabel.Text = $"Detected: Button {i} on {dev.VidPid}  —  release to continue";
                        _results.Add(new MappedInput(step.Key, "Button", i, DeviceVidPid: dev.VidPid));
                        _waitingForRelease = true;
                        _waitingDeviceIdx = di;
                        Array.Copy(buttons, dev.LastButtons, btnCount);
                        return;
                    }
                }
                Array.Copy(buttons, dev.LastButtons, btnCount);
            }
            return;
        }

        // Detect axis movement — largest delta across ALL devices vs baseline
        if (step.ExpectedKind == MappingKind.Axis)
        {
            var bestDelta = 0.0;
            var bestAxis = -1;
            var bestDevIdx = -1;
            var bestInvert = false;
            var liveParts = new List<string>();

            for (var di = 0; di < _devices.Count; di++)
            {
                var dev = _devices[di];
                var axisCount = dev.Controller.AxisCount;
                if (axisCount == 0) continue;
                var axes = new double[axisCount];
                var buttons = new bool[dev.Controller.ButtonCount];
                var sw = new GameControllerSwitchPosition[dev.Controller.SwitchCount];
                dev.Controller.GetCurrentReading(buttons, sw, axes);

                for (var i = 0; i < axisCount; i++)
                {
                    var delta = axes[i] - dev.BaselineAxes[i];
                    if (Math.Abs(delta) > 0.01)
                        liveParts.Add($"{dev.VidPid}.A{i}:{delta:+0.00;-0.00}");
                    if (Math.Abs(delta) > Math.Abs(bestDelta))
                    {
                        bestDelta = delta; bestAxis = i; bestDevIdx = di; bestInvert = delta < 0;
                    }
                }
            }

            var totalAxes = _devices.Sum(d => d.Controller.AxisCount);
            _liveLabel.Text = totalAxes == 0
                ? "No axes reported by any connected device"
                : liveParts.Count > 0
                    ? $"Live: {string.Join("  ", liveParts)}"
                    : $"Polling {_devices.Count} devices — move wheel or press pedal";

            if (bestAxis >= 0 && Math.Abs(bestDelta) > 0.15)
            {
                var dev = _devices[bestDevIdx];
                _detectedLabel.Text = $"Detected: Axis {bestAxis} on {dev.VidPid}  ({(bestInvert ? "inverted" : "normal")})  —  release to continue";
                _liveLabel.ForeColor = Color.FromArgb(63, 185, 80);
                _results.Add(new MappedInput(step.Key, "Axis", bestAxis, bestInvert, dev.VidPid));
                _releasingDeviceIdx = bestDevIdx;
                _detectedAxisIndex = bestAxis;
                _releasingAxis = true;
            }
        }
    }

    private void Advance()
    {
        _currentStep++;
        ShowStep(_currentStep);
        if (_currentStep < Steps.Length)
            CaptureBaseline();
    }

    private void SkipStep()
    {
        _currentStep++;
        ShowStep(_currentStep);
        if (_currentStep < Steps.Length)
            CaptureBaseline();
    }

    private void FinishWizard()
    {
        _pollTimer.Stop();

        var profileName = _nameText.Text.Trim();
        if (string.IsNullOrWhiteSpace(profileName))
            profileName = SuggestProfileName();

        Result = new WheelMapResult
        {
            DeviceVidPid = _deviceVidPid,
            DeviceName = _deviceName,
            ProfileName = profileName
        };
        Result.Inputs.AddRange(_results);

        DialogResult = DialogResult.OK;
        Close();
    }

    private string SuggestProfileName()
    {
        var safe = string.Concat(_deviceName.Select(c => char.IsLetterOrDigit(c) ? c : '_')).Trim('_');
        return "DefaultRawGameControllerMappingProfile" + safe + "Custom";
    }

    // --- XML generation -------------------------------------------------------

    public static XDocument BuildXmlDocument(WheelMapResult result)
    {
        var vidPidStr = result.DeviceVidPid.ToXmlString();
        var profileId = Guid.NewGuid().ToString().ToUpperInvariant();

        var profileElement = new XElement("RawGameControllerInputMappingProfile",
            new XAttribute("Version", "1"),
            new XAttribute("Id", profileId),
            new XAttribute("UserFacingName", result.ProfileName),
            new XAttribute("IsDefaultProfile", "0"),
            new XAttribute("PrimaryDeviceVidPid", vidPidStr),
            new XAttribute("FFBDeviceVidPid", vidPidStr),
            new XAttribute("FFBMotorIndex", "0"));

        var m = result.Inputs.ToDictionary(i => i.Key, i => i, StringComparer.OrdinalIgnoreCase);

        profileElement.Add(new XComment(" Race "));
        profileElement.Add(BuildRacingContext(m, vidPidStr));

        profileElement.Add(new XComment(" UI "));
        profileElement.Add(BuildUiContext(m, vidPidStr));

        profileElement.Add(new XComment(" Racing UI overlays "));
        profileElement.Add(BuildRacingUiContext(m, vidPidStr));

        profileElement.Add(new XComment(" Anna menu "));
        profileElement.Add(BuildAnnaContext(m, vidPidStr));

        profileElement.Add(new XComment(" Drone / Copter "));
        profileElement.Add(BuildCopterContext(m, vidPidStr));

        profileElement.Add(new XComment(" Race (restricted) "));
        profileElement.Add(BuildRacingCameraOnlyContext(m, vidPidStr));

        profileElement.Add(new XComment(" Free camera "));
        profileElement.Add(BuildFreecamContext(m, vidPidStr));

        profileElement.Add(new XComment(" Hide and Seek "));
        profileElement.Add(BuildHideSeekContext(m, vidPidStr));

        profileElement.Add(new XComment(" Eliminator "));
        profileElement.Add(BuildEliminatorContext(m, vidPidStr));

        profileElement.Add(new XComment(" Car Meets "));
        profileElement.Add(BuildCarMeetsContext(m, vidPidStr));

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("Profiles", profileElement));
    }

    // INPUTCONTEXT_RACING
    private static XElement BuildRacingContext(Dictionary<string, MappedInput> m, string vid)
    {
        var ctx = Ctx("INPUTCONTEXT_RACING");
        AddIfExists(ctx, m, "GAS",       "INPUTCMD_GAS",       vid);
        AddIfExists(ctx, m, "BRAKE",     "INPUTCMD_BRAKE",     vid);
        AddIfExists(ctx, m, "CLUTCH",    "INPUTCMD_CLUTCH",    vid);
        AddIfExists(ctx, m, "STEER",     "INPUTCMD_STEERING",  vid);
        AddIfExists(ctx, m, "SHIFT_UP",  "INPUTCMD_SHIFTUP",   vid);
        AddIfExists(ctx, m, "SHIFT_DOWN","INPUTCMD_SHIFTDOWN",  vid);
        AddIfExists(ctx, m, "SHIFT_DOWN","INPUTCMD_AUTODRIVE_CINEMATIC_CAMERA", vid);
        AddIfExists(ctx, m, "REWIND",    "INPUTCMD_MULLIGAN",  vid);
        AddIfExists(ctx, m, "HANDBRAKE", "INPUTCMD_HANDBRAKE", vid);
        AddIfExists(ctx, m, "PAUSE",     "INPUTCMD_PAUSE_GAME",vid);
        AddIfExists(ctx, m, "HORN",      "INPUTCMD_HORN",      vid);
        AddIfExists(ctx, m, "CONFIRM",   "INPUTCMD_ACTIVATE",  vid);
        AddIfExists(ctx, m, "CAMERA",    "INPUTCMD_SWITCH_CAMERA", vid);
        AddIfExists(ctx, m, "RADIO",     "INPUTCMD_RADIO_RIGHT",   vid);
        AddIfExists(ctx, m, "ANNA",      "INPUTCMD_ANNA_ACTIVATE", vid);
        AddIfExists(ctx, m, "TELEMETRY", "INPUTCMD_TELEMETRY_TOGGLE", vid);
        AddIfExists(ctx, m, "NAV_LEFT",  "INPUTCMD_TELEMETRY_PREV",  vid);
        AddIfExists(ctx, m, "NAV_RIGHT", "INPUTCMD_TELEMETRY_NEXT",  vid);
        AddIfExists(ctx, m, "MAP",       "INPUTCMD_OPEN_MAP",        vid);
        return ctx;
    }

    // INPUTCONTEXT_UI
    private static XElement BuildUiContext(Dictionary<string, MappedInput> m, string vid)
    {
        var ctx = Ctx("INPUTCONTEXT_UI");
        AddDPad(ctx, m, vid);

        // OK (Confirm)
        foreach (var cmd in new[] { "INPUTCMD_UI_OK_PRESS", "INPUTCMD_UI_OK_RELEASE", "INPUTCMD_UI_OK_REPEAT", "INPUTCMD_UI_OK_WHILEDOWN" })
            AddIfExists(ctx, m, "CONFIRM", cmd, vid);

        // Cancel
        foreach (var cmd in new[] { "INPUTCMD_UI_CANCEL_PRESS", "INPUTCMD_UI_CANCEL_RELEASE", "INPUTCMD_UI_CANCEL_REPEAT", "INPUTCMD_UI_CANCEL_WHILEDOWN" })
            AddIfExists(ctx, m, "CANCEL", cmd, vid);

        // Start/Pause
        foreach (var cmd in new[] { "INPUTCMD_UI_START_PRESS", "INPUTCMD_UI_START_RELEASE", "INPUTCMD_UI_START_REPEAT" })
            AddIfExists(ctx, m, "PAUSE", cmd, vid);

        // Back
        foreach (var cmd in new[] { "INPUTCMD_UI_BACK_PRESS", "INPUTCMD_UI_BACK_RELEASE", "INPUTCMD_UI_BACK_REPEAT" })
            AddIfExists(ctx, m, "BACK", cmd, vid);

        // Directional buttons
        foreach (var cmd in new[] { "INPUTCMD_UI_UP_PRESS", "INPUTCMD_UI_UP_RELEASE", "INPUTCMD_UI_UP_REPEAT" })
            AddIfExists(ctx, m, "NAV_UP", cmd, vid);
        foreach (var cmd in new[] { "INPUTCMD_UI_DOWN_PRESS", "INPUTCMD_UI_DOWN_RELEASE", "INPUTCMD_UI_DOWN_REPEAT" })
            AddIfExists(ctx, m, "NAV_DOWN", cmd, vid);
        foreach (var cmd in new[] { "INPUTCMD_UI_LEFT_PRESS", "INPUTCMD_UI_LEFT_RELEASE", "INPUTCMD_UI_LEFT_REPEAT" })
            AddIfExists(ctx, m, "NAV_LEFT", cmd, vid);
        foreach (var cmd in new[] { "INPUTCMD_UI_RIGHT_PRESS", "INPUTCMD_UI_RIGHT_RELEASE", "INPUTCMD_UI_RIGHT_REPEAT" })
            AddIfExists(ctx, m, "NAV_RIGHT", cmd, vid);

        // Right trigger (gas axis in menus)
        foreach (var cmd in new[] { "INPUTCMD_UI_RTRIGGER_PRESS", "INPUTCMD_UI_RTRIGGER_RELEASE", "INPUTCMD_UI_RTRIGGER_REPEAT" })
            AddIfExists(ctx, m, "GAS", cmd, vid, innerDz: "0.05", outerDz: "0.95");

        // Left/Right bumpers (shift paddles in menus)
        foreach (var cmd in new[] { "INPUTCMD_UI_LBUMPER_PRESS", "INPUTCMD_UI_LBUMPER_RELEASE", "INPUTCMD_UI_LBUMPER_REPEAT" })
            AddIfExists(ctx, m, "SHIFT_DOWN", cmd, vid);
        foreach (var cmd in new[] { "INPUTCMD_UI_RBUMPER_PRESS", "INPUTCMD_UI_RBUMPER_RELEASE", "INPUTCMD_UI_RBUMPER_REPEAT" })
            AddIfExists(ctx, m, "SHIFT_UP", cmd, vid);
        AddIfExists(ctx, m, "SHIFT_DOWN", "INPUTCMD_PREV_CATEGORY", vid);
        AddIfExists(ctx, m, "SHIFT_UP",   "INPUTCMD_NEXT_CATEGORY", vid);

        // Brick challenges (DPad again)
        foreach (var (key, cmd) in new[] { ("NAV_UP","INPUTCMD_UI_BRICKCHALLENGES_UP"), ("NAV_DOWN","INPUTCMD_UI_BRICKCHALLENGES_DOWN"), ("NAV_LEFT","INPUTCMD_UI_BRICKCHALLENGES_LEFT"), ("NAV_RIGHT","INPUTCMD_UI_BRICKCHALLENGES_RIGHT") })
            AddIfExists(ctx, m, key, cmd, vid);

        // Mulligan in UI
        AddIfExists(ctx, m, "REWIND", "INPUTCMD_MULLIGAN", vid);

        // Map move (complex pair elements) — gas/brake axes for left-right, nav buttons for up-down
        if (m.TryGetValue("BRAKE", out var brakeIn) && m.TryGetValue("GAS", out var gasIn))
        {
            ctx.Add(BuildComplexAxisPair("INPUTCMD_UI_MAP_MOVE_LEFTRIGHT", brakeIn, gasIn, vid));
            ctx.Add(BuildComplexAxisPair("INPUTCMD_UI_REPLAY_SPEED_RIGHT", gasIn, null, vid));
            ctx.Add(BuildComplexAxisPair("INPUTCMD_UI_REPLAY_SPEED_LEFT",  brakeIn, null, vid));
        }
        if (m.TryGetValue("NAV_DOWN", out var navDownIn) && m.TryGetValue("NAV_UP", out var navUpIn))
            ctx.Add(BuildComplexButtonPair("INPUTCMD_UI_MAP_MOVE_UPDOWN", navDownIn, navUpIn, vid));

        // X and Y face buttons
        foreach (var cmd in new[] { "INPUTCMD_UI_X_PRESS", "INPUTCMD_UI_X_RELEASE", "INPUTCMD_UI_X_REPEAT" })
            AddIfExists(ctx, m, "BTN_X", cmd, vid);
        foreach (var cmd in new[] { "INPUTCMD_UI_Y_PRESS", "INPUTCMD_UI_Y_RELEASE", "INPUTCMD_UI_Y_REPEAT" })
            AddIfExists(ctx, m, "BTN_Y", cmd, vid);

        // Map / View button
        foreach (var cmd in new[] { "INPUTCMD_UI_VIEW_PRESS", "INPUTCMD_UI_VIEW_RELEASE", "INPUTCMD_UI_VIEW_REPEAT" })
            AddIfExists(ctx, m, "MAP", cmd, vid);

        return ctx;
    }

    // INPUTCONTEXT_RACING_UI
    private static XElement BuildRacingUiContext(Dictionary<string, MappedInput> m, string vid)
    {
        var ctx = Ctx("INPUTCONTEXT_RACING_UI");
        AddIfExists(ctx, m, "ANNA",      "INPUTCMD_ANNA_ACTIVATE",   vid);
        AddIfExists(ctx, m, "PHOTO",     "INPUTCMD_PHOTO_MODE_TOGGLE", vid);
        AddIfExists(ctx, m, "QUICKCHAT", "INPUTCMD_QUICKCHAT_ACTIVATE", vid);
        AddIfExists(ctx, m, "RADIO",     "INPUTCMD_RADIO_RIGHT",     vid);
        return ctx;
    }

    // INPUTCONTEXT_ANNA
    private static XElement BuildAnnaContext(Dictionary<string, MappedInput> m, string vid)
    {
        var ctx = Ctx("INPUTCONTEXT_ANNA");
        AddIfExists(ctx, m, "NAV_UP",    "INPUTCMD_ANNA_ITEM_1", vid);
        AddIfExists(ctx, m, "NAV_LEFT",  "INPUTCMD_ANNA_ITEM_2", vid);
        AddIfExists(ctx, m, "NAV_RIGHT", "INPUTCMD_ANNA_ITEM_3", vid);
        AddIfExists(ctx, m, "NAV_DOWN",  "INPUTCMD_ANNA_ITEM_4", vid);
        return ctx;
    }

    // INPUTCONTEXT_COPTER
    private static XElement BuildCopterContext(Dictionary<string, MappedInput> m, string vid)
    {
        var ctx = Ctx("INPUTCONTEXT_COPTER");
        AddIfExists(ctx, m, "PHOTO",     "INPUTCMD_PHOTO_MODE_TOGGLE",   vid);
        AddIfExists(ctx, m, "QUICKCHAT", "INPUTCMD_QUICKCHAT_ACTIVATE",  vid);
        AddIfExists(ctx, m, "RADIO",     "INPUTCMD_RADIO_RIGHT",         vid);
        return ctx;
    }

    // INPUTCONTEXT_RACING_CAMERA_ONLY
    private static XElement BuildRacingCameraOnlyContext(Dictionary<string, MappedInput> m, string vid)
    {
        var ctx = Ctx("INPUTCONTEXT_RACING_CAMERA_ONLY");
        AddIfExists(ctx, m, "PAUSE",     "INPUTCMD_PAUSE_GAME",          vid);
        AddIfExists(ctx, m, "CAMERA",    "INPUTCMD_SWITCH_CAMERA",       vid);
        AddIfExists(ctx, m, "HORN",      "INPUTCMD_HORN",                vid);
        AddIfExists(ctx, m, "CONFIRM",   "INPUTCMD_ACTIVATE",            vid);
        AddIfExists(ctx, m, "ANNA",      "INPUTCMD_ANNA_ACTIVATE",       vid);
        AddIfExists(ctx, m, "TELEMETRY", "INPUTCMD_TELEMETRY_TOGGLE",    vid);
        AddIfExists(ctx, m, "NAV_LEFT",  "INPUTCMD_TELEMETRY_PREV",      vid);
        AddIfExists(ctx, m, "NAV_RIGHT", "INPUTCMD_TELEMETRY_NEXT",      vid);
        return ctx;
    }

    // INPUTCONTEXT_FREECAM — uses axes for map-move
    private static XElement BuildFreecamContext(Dictionary<string, MappedInput> m, string vid)
    {
        var ctx = Ctx("INPUTCONTEXT_FREECAM");
        if (m.TryGetValue("BRAKE", out var brakeIn) && m.TryGetValue("GAS", out var gasIn))
            ctx.Add(BuildComplexAxisPair("INPUTCMD_UI_MAP_MOVE_LEFTRIGHT", brakeIn, gasIn, vid));
        return ctx;
    }

    // INPUTCONTEXT_HIDE_SEEK
    private static XElement BuildHideSeekContext(Dictionary<string, MappedInput> m, string vid)
    {
        var ctx = Ctx("INPUTCONTEXT_HIDE_SEEK");
        AddIfExists(ctx, m, "BTN_Y", "INPUTCMD_HIDESEEK_PING_OR_CHASEBREAK", vid);
        return ctx;
    }

    // INPUTCONTEXT_ELIMINATOR_UI
    private static XElement BuildEliminatorContext(Dictionary<string, MappedInput> m, string vid)
    {
        var ctx = Ctx("INPUTCONTEXT_ELIMINATOR_UI");
        AddIfExists(ctx, m, "NAV_LEFT",  "INPUTCMD_ELIMINATOR_UPGRADECHOICE_LEFT",  vid);
        AddIfExists(ctx, m, "NAV_DOWN",  "INPUTCMD_ELIMINATOR_UPGRADECHOICE_DOWN",  vid);
        AddIfExists(ctx, m, "NAV_RIGHT", "INPUTCMD_ELIMINATOR_UPGRADECHOICE_RIGHT", vid);
        return ctx;
    }

    // INPUTCONTEXT_CAR_MEETS
    private static XElement BuildCarMeetsContext(Dictionary<string, MappedInput> m, string vid)
    {
        var ctx = Ctx("INPUTCONTEXT_CAR_MEETS");
        AddIfExists(ctx, m, "NAV_LEFT", "INPUTCMD_QUICKCHAT_ACTIVATE",     vid);
        AddIfExists(ctx, m, "NAV_UP",   "INPUTCMD_CAR_MEETS_PHOTO",         vid);
        AddIfExists(ctx, m, "NAV_DOWN", "INPUTCMD_CAR_MEETS_CINEMATIC_CAM", vid);
        return ctx;
    }

    // DPad entries written to the UI context
    private static void AddDPad(XElement ctx, Dictionary<string, MappedInput> m, string vid)
    {
        AddIfExists(ctx, m, "NAV_UP",    "INPUTCMD_UI_DPAD_UP_PRESS",    vid);
        AddIfExists(ctx, m, "NAV_DOWN",  "INPUTCMD_UI_DPAD_DOWN_PRESS",  vid);
        AddIfExists(ctx, m, "NAV_LEFT",  "INPUTCMD_UI_DPAD_LEFT_PRESS",  vid);
        AddIfExists(ctx, m, "NAV_RIGHT", "INPUTCMD_UI_DPAD_RIGHT_PRESS", vid);
    }

    // Helpers
    private static XElement Ctx(string name) =>
        new("Context", new XAttribute("Version", "1"), new XAttribute("Context", name));

    private static void AddIfExists(XElement ctx, Dictionary<string, MappedInput> m, string key, string cmd, string vid,
        string? innerDz = null, string? outerDz = null)
    {
        if (!m.TryGetValue(key, out var input)) return;
        ctx.Add(BuildValueElement(input with { Key = cmd }, vid, innerDz, outerDz));
    }

    private static XElement BuildValueElement(MappedInput input, string vidPidStr,
        string? innerDz = null, string? outerDz = null)
    {
        var effectiveVid = string.IsNullOrEmpty(input.DeviceVidPid) ? vidPidStr : input.DeviceVidPid;
        var el = new XElement("Value",
            new XAttribute("Version", "1"),
            new XAttribute("Key", input.Key),
            new XAttribute("VidPid", effectiveVid),
            new XAttribute("InputType", input.InputType),
            new XAttribute("Index", input.Index));

        if (input.InputType == "Axis")
        {
            el.Add(new XAttribute("InvertAxis", input.InvertAxis ? "true" : "false"));
            el.Add(new XAttribute("InnerDeadzone", innerDz ?? "0.0"));
            el.Add(new XAttribute("OuterDeadzone", outerDz ?? "1.0"));
        }

        return el;
    }

    // Builds <Value Key="..." ><InputCmdLow .../><InputCmdHigh .../></Value> for two-axis map-move commands
    private static XElement BuildComplexAxisPair(string cmd, MappedInput low, MappedInput? high, string vid)
    {
        var el = new XElement("Value",
            new XAttribute("Version", "1"),
            new XAttribute("Key", cmd));

        var lowEl = new XElement("InputCmdLow",
            new XAttribute("VidPid", string.IsNullOrEmpty(low.DeviceVidPid) ? vid : low.DeviceVidPid),
            new XAttribute("InputType", low.InputType),
            new XAttribute("Index", low.Index),
            new XAttribute("InvertAxis", low.InvertAxis ? "true" : "false"),
            new XAttribute("InnerDeadzone", "0.05"),
            new XAttribute("OuterDeadzone", "0.95"));
        el.Add(lowEl);

        if (high != null)
        {
            var highEl = new XElement("InputCmdHigh",
                new XAttribute("VidPid", string.IsNullOrEmpty(high.DeviceVidPid) ? vid : high.DeviceVidPid),
                new XAttribute("InputType", high.InputType),
                new XAttribute("Index", high.Index),
                new XAttribute("InvertAxis", high.InvertAxis ? "true" : "false"),
                new XAttribute("InnerDeadzone", "0.05"),
                new XAttribute("OuterDeadzone", "0.95"));
            el.Add(highEl);
        }

        return el;
    }

    // Builds two-button map-move (up/down navigation)
    private static XElement BuildComplexButtonPair(string cmd, MappedInput low, MappedInput high, string vid)
    {
        var el = new XElement("Value",
            new XAttribute("Version", "1"),
            new XAttribute("Key", cmd));

        el.Add(new XElement("InputCmdLow",
            new XAttribute("VidPid", string.IsNullOrEmpty(low.DeviceVidPid) ? vid : low.DeviceVidPid),
            new XAttribute("InputType", low.InputType),
            new XAttribute("Index", low.Index)));
        el.Add(new XElement("InputCmdHigh",
            new XAttribute("VidPid", string.IsNullOrEmpty(high.DeviceVidPid) ? vid : high.DeviceVidPid),
            new XAttribute("InputType", high.InputType),
            new XAttribute("Index", high.Index)));

        return el;
    }
}
