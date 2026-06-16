using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Web.WebView2.Core;
using Windows.Gaming.Input;

namespace HorizonSimTool;

internal sealed class WebBridge
{
    private readonly WebUIForm _form;
    private string _mediaFolder = "";
    private string _inputZip = "";
    private string _wheelZip = "";
    private string _installFolder = "";
    private string _wheelbaseVidPid = "";
    private string _pedalsVidPid = "";
    private string _shifterVidPid = "";
    private string _handbrakeVidPid = "";
    private string _profileEntry = "";

    private string? _generatedInputZip;
    private string? _generatedWheelZip;

    private IReadOnlyList<DeviceInfo> _devices = Array.Empty<DeviceInfo>();
    private IReadOnlyList<DeviceInfo> _controllerDevices = Array.Empty<DeviceInfo>();
    private IReadOnlyList<ZipProfileInfo> _profiles = Array.Empty<ZipProfileInfo>();
    private IReadOnlyList<ZipWheelTuneInfo> _ffbTemplates = Array.Empty<ZipWheelTuneInfo>();
    private XDocument? _currentXml;
    private ZipProfileInfo? _selectedProfile;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WebBridge(WebUIForm form)
    {
        _form = form;
    }

    public void HandleMessage(CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonObject? msg;
        try
        {
            msg = JsonNode.Parse(e.WebMessageAsJson)?.AsObject();
        }
        catch
        {
            return;
        }

        var id = msg?["id"]?.GetValue<int>() ?? 0;
        var action = msg?["action"]?.GetValue<string>() ?? "";
        var payload = msg?["payload"]?.AsObject();

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await DispatchAsync(action, payload);
                SendReply(id, ok: true, data: result);
            }
            catch (Exception ex)
            {
                SendReply(id, ok: false, error: ex.Message);
            }
        });
    }

    private async Task<object?> DispatchAsync(string action, JsonObject? payload)
    {
        return action switch
        {
            "getInitialState"    => GetInitialState(),
            "refreshDevices"     => await Task.Run(RefreshDevices),
            "setRoles"           => SetRoles(payload),
            "findInstall"        => await Task.Run(FindInstall),
            "browseFolder"       => await BrowseFolder(payload),
            "browseFile"         => await BrowseFile(payload),
            "loadProfiles"       => await Task.Run(() => LoadProfiles(payload)),
            "selectProfile"      => SelectProfile(payload),
            "launchWizard"       => await LaunchWizardAsync(),
            "generateFiles"      => await Task.Run(() => GenerateFiles(payload)),
            "installFiles"       => await Task.Run(() => InstallFiles(payload)),
            "createBackup"       => await Task.Run(() => CreateBackup(payload)),
            "openBackupFolder"   => await _form.InvokeAsync<object?>(() => { OpenFolderShell(GameFileInstaller.GetBackupFolder(_mediaFolder)); return null; }),
            "openOutputFolder"   => await _form.InvokeAsync<object?>(() => { OpenFolderShell(Program.DefaultOutputFolder()); return null; }),
            "restoreBackups"     => await Task.Run(() => RestoreBackups(payload)),
            "verifyZips"         => await Task.Run(() => VerifyZips(payload)),
            "checkSilenceAccess" => await Task.Run(CheckSilenceAccess),
            "applySilence"       => null,
            "stopSilence"        => null,
            _ => throw new InvalidOperationException($"Unknown action: {action}")
        };
    }

    private object GetInitialState()
    {
        return new
        {
            outputFolder  = Program.DefaultOutputFolder(),
            mediaFolder   = _mediaFolder,
            installFolder = _installFolder,
            inputZip      = _inputZip,
            wheelZip      = _wheelZip,
        };
    }

    private object RefreshDevices()
    {
        _devices = DeviceEnumerator.GetPresentVidPidDevices();
        _controllerDevices = DeviceEnumerator.GetPresentControllerDevices();
        Log($"Loaded {_devices.Count} devices, {_controllerDevices.Count} controller devices.");
        return new
        {
            devices = MapDevices(_devices),
            controllerDevices = MapDevices(_controllerDevices)
        };
    }

    private static IReadOnlyList<object> MapDevices(IReadOnlyList<DeviceInfo> devs)
    {
        return devs.Select(d => (object)new
        {
            name = d.Name,
            vid = d.VidPid.Vid,
            pid = d.VidPid.Pid,
            xmlValue = d.VidPid.ToXmlString(),
            className = d.ClassName,
            isController = IsControllerClass(d),
            usesWindowsName = d.UsesWindowsSettingsName
        }).ToList();
    }

    private static bool IsControllerClass(DeviceInfo d)
    {
        var h = string.Join(" ", d.ClassName, d.HardwareId, d.CompatibleIds);
        return h.Contains("HID_DEVICE_SYSTEM_GAME", StringComparison.OrdinalIgnoreCase)
            || h.Contains("HID_DEVICE_SYSTEM_JOYSTICK", StringComparison.OrdinalIgnoreCase)
            || h.Contains("IG_", StringComparison.OrdinalIgnoreCase)
            || d.ClassName.Equals("HIDClass", StringComparison.OrdinalIgnoreCase);
    }

    private object? SetRoles(JsonObject? p)
    {
        _wheelbaseVidPid = p?["wheelbase"]?.GetValue<string>() ?? "";
        _pedalsVidPid    = p?["pedals"]?.GetValue<string>()    ?? "";
        _shifterVidPid   = p?["shifter"]?.GetValue<string>()   ?? "";
        _handbrakeVidPid = p?["handbrake"]?.GetValue<string>() ?? "";
        return null;
    }

    private object FindInstall()
    {
        var folder = ForzaInstallFinder.FindMediaFolders().FirstOrDefault();
        if (folder is null) return new { found = false };

        _mediaFolder   = folder;
        _installFolder = folder;
        _inputZip      = Path.Combine(folder, "inputmappingprofiles.zip");
        _wheelZip      = Path.Combine(folder, "wheeltunablesettingspc.zip");
        if (!File.Exists(_inputZip)) _inputZip = "";
        if (!File.Exists(_wheelZip)) _wheelZip = "";
        Log("Found game media folder: " + folder);
        return new { found = true, mediaFolder = _mediaFolder, installFolder = _installFolder, inputZip = _inputZip, wheelZip = _wheelZip };
    }

    private async Task<object?> BrowseFolder(JsonObject? p)
    {
        var title = p?["title"]?.GetValue<string>() ?? "Select folder";
        string? path = null;
        await _form.InvokeAsync(() =>
        {
            using var dlg = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true };
            path = dlg.ShowDialog(_form) == DialogResult.OK ? dlg.SelectedPath : null;
        });
        return new { path = path ?? "" };
    }

    private async Task<object?> BrowseFile(JsonObject? p)
    {
        var title  = p?["title"]?.GetValue<string>()  ?? "Select file";
        var filter = p?["filter"]?.GetValue<string>() ?? "ZIP files|*.zip";
        string? path = null;
        await _form.InvokeAsync(() =>
        {
            using var dlg = new OpenFileDialog { Title = title, Filter = filter + "|All files (*.*)|*.*" };
            path = dlg.ShowDialog(_form) == DialogResult.OK ? dlg.FileName : null;
        });
        return new { path = path ?? "" };
    }

    private object LoadProfiles(JsonObject? p)
    {
        var inputZip = p?["inputZip"]?.GetValue<string>() ?? _inputZip;
        var wheelZip = p?["wheelZip"]?.GetValue<string>() ?? _wheelZip;

        if (!File.Exists(inputZip)) throw new FileNotFoundException("Input ZIP not found: " + inputZip);

        _inputZip = inputZip;
        if (!string.IsNullOrWhiteSpace(wheelZip)) _wheelZip = wheelZip;

        _profiles = ZipProfileReader.ReadProfiles(inputZip);
        _ffbTemplates = File.Exists(wheelZip) ? ZipWheelTuneReader.ReadEntries(wheelZip) : Array.Empty<ZipWheelTuneInfo>();

        Log($"Loaded {_profiles.Count} profiles and {_ffbTemplates.Count} FFB templates.");
        return new
        {
            profiles = _profiles.Select(pr => new { entryName = pr.EntryName, displayName = pr.UserFacingName ?? pr.EntryName }).ToList(),
            ffbTemplates = _ffbTemplates.Select(t => new { entryName = t.EntryName, displayName = t.EntryName }).ToList()
        };
    }

    private object? SelectProfile(JsonObject? p)
    {
        var entry = p?["entryName"]?.GetValue<string>() ?? "";
        _profileEntry = entry;
        if (string.IsNullOrWhiteSpace(entry) || !File.Exists(_inputZip)) return null;

        _selectedProfile = _profiles.FirstOrDefault(pr => pr.EntryName == entry);
        if (_selectedProfile is not null)
        {
            _currentXml = ZipProfileReader.ReadXml(_inputZip, entry);
            Log("Loaded profile: " + entry);
        }
        return null;
    }

    private async Task<object?> LaunchWizardAsync()
    {
        if (string.IsNullOrWhiteSpace(_wheelbaseVidPid) || !VidPid.TryParse(_wheelbaseVidPid, out var vp))
            throw new InvalidOperationException("Assign a wheelbase device first in the Devices tab.");

        // Find the matching RawGameController by comparing VID/PID strings
        RawGameController? controller = null;
        DeviceInfo? matchedDevice = null;
        foreach (var rc in RawGameController.RawGameControllers)
        {
            var rcVid = rc.HardwareVendorId.ToString("X4");
            var rcPid = rc.HardwareProductId.ToString("X4");
            var rcVp = new VidPid(rcVid, rcPid);
            if (rcVp.Compact.Equals(vp.Compact, StringComparison.OrdinalIgnoreCase))
            {
                controller = rc;
                matchedDevice = _controllerDevices.FirstOrDefault(d => d.VidPid == vp)
                             ?? _devices.FirstOrDefault(d => d.VidPid == vp);
                break;
            }
        }
        controller ??= RawGameController.RawGameControllers.FirstOrDefault();
        if (controller is null) throw new InvalidOperationException("No controller detected. Connect your wheel and refresh devices.");
        matchedDevice ??= _controllerDevices.FirstOrDefault() ?? _devices.FirstOrDefault();

        var deviceName = matchedDevice?.Name ?? _wheelbaseVidPid;

        WheelMapResult? result = null;
        await _form.InvokeAsync(() =>
        {
            using var wizard = new WheelMapWizard(vp, deviceName, controller);
            if (wizard.ShowDialog(_form) == DialogResult.OK)
                result = wizard.Result;
        });

        if (result is null)
        {
            SendEvent("wizardDone", new { success = false });
            return null;
        }

        var summary = $"{result.Inputs.Count} inputs mapped";
        SendEvent("wizardDone", new { success = true, summary });
        Log("Wizard complete: " + summary);
        return null;
    }

    private object GenerateFiles(JsonObject? p)
    {
        var inputZip     = p?["inputZip"]?.GetValue<string>()     ?? _inputZip;
        var wheelZip     = p?["wheelZip"]?.GetValue<string>()     ?? _wheelZip;
        var profileEntry = p?["profileEntry"]?.GetValue<string>() ?? _profileEntry;
        var ffbEntry     = p?["ffbEntry"]?.GetValue<string>()     ?? "";
        var wheelbase    = p?["wheelbase"]?.GetValue<string>()    ?? _wheelbaseVidPid;
        var patchInPlace = p?["patchInPlace"]?.GetValue<bool>()   ?? true;
        var applyFfb     = p?["applyFfb"]?.GetValue<bool>()       ?? false;

        if (!File.Exists(inputZip)) throw new FileNotFoundException("Input ZIP not found: " + inputZip);
        if (!VidPid.TryParse(wheelbase, out var primaryVidPid))
            throw new InvalidOperationException("No wheelbase device assigned.");
        if (string.IsNullOrWhiteSpace(profileEntry))
            throw new InvalidOperationException("No XML profile selected.");

        var outputFolder = Program.DefaultOutputFolder();
        Directory.CreateDirectory(outputFolder);

        var xml = _currentXml ?? ZipProfileReader.ReadXml(inputZip, profileEntry);
        var profile = _selectedProfile ?? _profiles.FirstOrDefault(pr => pr.EntryName == profileEntry)
            ?? throw new InvalidOperationException("Profile not loaded. Use Load Files first.");

        // Replace all existing VID/PIDs with the wheelbase
        var replacements = XmlProfileEditor.GetVidPidValues(xml)
            .ToDictionary(v => v, _ => primaryVidPid);
        XmlProfileEditor.ApplyVidPidReplacements(xml, replacements);
        XmlProfileEditor.SetPrimaryAndFfb(XmlProfileEditor.GetProfileElement(xml), primaryVidPid, primaryVidPid);

        var destProfileName = patchInPlace ? profile.EntryName : "DefaultRawGameControllerMappingProfileCodexCustom.xml";
        if (!patchInPlace) XmlProfileEditor.AssignNewProfileId(XmlProfileEditor.GetProfileElement(xml));

        var patchedXml = XmlProfileEditor.ToUtf8Xml(xml);
        var outInputZip = Path.Combine(outputFolder, "inputmappingprofiles.zip");
        ZipWriter.WriteInputMappingZip(inputZip, outInputZip, profile.EntryName, destProfileName, patchedXml);

        string? outWheelZip = null;
        if (applyFfb && !string.IsNullOrWhiteSpace(ffbEntry) && File.Exists(wheelZip))
        {
            var templateIni = ZipWheelTuneReader.ReadText(wheelZip, ffbEntry);
            var patchedIni  = IniEditor.SetVendorProduct(templateIni, primaryVidPid);
            var destIniName = $"ControllerFFB-0X{primaryVidPid.Compact}.ini";
            outWheelZip = Path.Combine(outputFolder, "wheeltunablesettingspc.zip");
            ZipWriter.WriteWheelTuneZip(wheelZip, outWheelZip, ffbEntry, destIniName, patchedIni, Encoding.UTF8);
        }

        _generatedInputZip = outInputZip;
        _generatedWheelZip = outWheelZip;
        Log($"Generated files in: {outputFolder}");
        return new { generatedInputZip = outInputZip, generatedWheelZip = outWheelZip ?? "", outputFolder };
    }

    private object? InstallFiles(JsonObject? p)
    {
        var mediaFolder    = p?["mediaFolder"]?.GetValue<string>()      ?? _installFolder.IfEmpty(_mediaFolder);
        var generatedInput = p?["generatedInputZip"]?.GetValue<string>() ?? _generatedInputZip ?? "";
        var generatedWheel = p?["generatedWheelZip"]?.GetValue<string>() ?? _generatedWheelZip ?? "";

        if (!Directory.Exists(mediaFolder))
            throw new InvalidOperationException("Game media folder not found: " + mediaFolder);
        if (!File.Exists(generatedInput))
            throw new FileNotFoundException("Generated input ZIP not found. Generate files first.");

        GameFileInstaller.BackupAndInstall(mediaFolder, generatedInput,
            string.IsNullOrWhiteSpace(generatedWheel) ? null : generatedWheel);
        Log("Installed files to: " + mediaFolder);
        return null;
    }

    private object? CreateBackup(JsonObject? p)
    {
        var folder = p?["mediaFolder"]?.GetValue<string>() ?? _mediaFolder;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Media folder not found: " + folder);
        var backupFolder = GameFileInstaller.CreateOrOverwriteBackup(folder);
        Log("Backup created: " + backupFolder);
        return null;
    }

    private object? RestoreBackups(JsonObject? p)
    {
        var folder = p?["mediaFolder"]?.GetValue<string>() ?? _mediaFolder;
        GameFileInstaller.RestoreBackup(folder);
        Log("Backups restored.");
        return null;
    }

    private object? VerifyZips(JsonObject? p)
    {
        var inputZip = p?["generatedInputZip"]?.GetValue<string>() ?? _generatedInputZip ?? "";
        var wheelZip = p?["generatedWheelZip"]?.GetValue<string>() ?? _generatedWheelZip ?? "";

        if (!File.Exists(inputZip)) throw new FileNotFoundException("Generated input ZIP not found.");
        var r = ZipVerifier.VerifyStoreOnlyTopLevel(inputZip);
        if (!r.Passed) throw new InvalidOperationException("Input zip has nested/compressed entries.");
        Log($"Input zip verified: {r.EntryCount} entries.");
        if (!string.IsNullOrWhiteSpace(wheelZip) && File.Exists(wheelZip))
        {
            var wr = ZipVerifier.VerifyStoreOnlyTopLevel(wheelZip);
            if (!wr.Passed) throw new InvalidOperationException("Wheel zip has nested/compressed entries.");
            Log($"Wheel zip verified: {wr.EntryCount} entries.");
        }
        return null;
    }

    private object? CheckSilenceAccess()
    {
        var elevated = DeviceSilencer.IsRunningElevated();
        Log(elevated ? "Running as Administrator — silence available." : "Not elevated — silence requires admin.");
        SendEvent("silenceAccess", new { ok = elevated });
        return null;
    }

    private static void OpenFolderShell(string path)
    {
        if (Directory.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", path);
    }

    private void Log(string msg)
    {
        _form.BeginInvoke(() => SendEvent("log", msg));
    }

    private void SendReply(int id, bool ok, object? data = null, string? error = null)
    {
        var json = JsonSerializer.Serialize(new { id, ok, data, error }, JsonOpts);
        _form.BeginInvoke(() => _form.PostMessage(json));
    }

    private void SendEvent(string eventName, object data)
    {
        var json = JsonSerializer.Serialize(new { @event = eventName, data }, JsonOpts);
        _form.BeginInvoke(() => _form.PostMessage(json));
    }
}

file static class StringExtensions
{
    public static string IfEmpty(this string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
