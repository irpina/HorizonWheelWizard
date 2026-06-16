using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace HorizonSimTool;

internal static class Program
{
    private const string InputZipName = "inputmappingprofiles.zip";
    private const string WheelZipName = "wheeltunablesettingspc.zip";

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(command) || command == "gui")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                return RunGuiSingleInstance();
            }

            TrySetConsoleOutputEncoding();
            return command switch
            {
                "wizard" => RunWizard(),
                "list-devices" or "devices" => RunListDevices(),
                "list-controllers" or "controllers" => RunListControllers(),
                "list-profiles" or "profiles" => RunListProfiles(args.Skip(1).FirstOrDefault()),
                "find-install" or "find" => RunFindInstall(),
                "help" or "--help" or "-h" => ShowHelp(),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("HorizonSimTool");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  wizard         Interactive mapping and zip generation flow (default)");
        Console.WriteLine("  list-devices   List connected USB/HID devices with VID/PID");
        Console.WriteLine("  controllers    List controller devices eligible for Silence mode");
        Console.WriteLine("  list-profiles  List raw controller XML profiles from an inputmappingprofiles.zip");
        Console.WriteLine("  find-install   Find likely Forza Horizon 6 media folders");
        Console.WriteLine("  help           Show this help");
        Console.WriteLine();
        Console.WriteLine("Generated zips are written with Store/no compression and top-level entries only.");
        return 0;
    }

    private static int RunGuiSingleInstance()
    {
        using var singleInstance = new Mutex(initiallyOwned: true, "Local\\HorizonSimTool.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Horizon SimTool is already running.\n\nUse the existing window or tray icon.",
                "Horizon SimTool",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        Application.Run(new WebUIForm());
        GC.KeepAlive(singleInstance);
        return 0;
    }

    public static string DefaultOutputFolder() => Path.Combine(AppContext.BaseDirectory, "output");

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine("Run HorizonSimTool help for usage.");
        return 1;
    }

    private static void TrySetConsoleOutputEncoding()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // A WinExe launched without a console can reject console encoding changes.
        }
    }

    private static int RunListDevices()
    {
        var devices = DeviceEnumerator.GetPresentVidPidDevices();
        PrintDevices(devices);
        return 0;
    }

    private static int RunListControllers()
    {
        var devices = DeviceEnumerator.GetPresentControllerDevices();
        Console.WriteLine("Controller devices eligible for Silence mode:");
        PrintDevices(devices);
        return 0;
    }

    private static int RunFindInstall()
    {
        var candidates = ForzaInstallFinder.FindMediaFolders();
        if (candidates.Count == 0)
        {
            Console.WriteLine("No Forza Horizon 6 media folder was found automatically.");
            return 1;
        }

        Console.WriteLine("Found media folders:");
        foreach (var path in candidates)
        {
            Console.WriteLine("  " + path);
        }

        return 0;
    }

    private static int RunListProfiles(string? zipPath)
    {
        var path = zipPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var mediaFolder = ForzaInstallFinder.FindMediaFolders().FirstOrDefault();
            if (mediaFolder is null)
            {
                Console.Error.WriteLine("No media folder was found. Pass a path to inputmappingprofiles.zip.");
                return 1;
            }

            path = Path.Combine(mediaFolder, InputZipName);
        }

        path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim('"')));
        var profiles = ZipProfileReader.ReadProfiles(path);
        Console.WriteLine("Raw game controller XML profiles:");
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            var primary = profile.PrimaryDeviceVidPid is null ? "" : " | " + profile.PrimaryDeviceVidPid.Value.ToXmlString();
            var userFacingName = profile.UserFacingName is null ? "" : " | " + profile.UserFacingName;
            Console.WriteLine($"  [{i + 1,3}] {profile.EntryName}{primary}{userFacingName}");
        }

        return profiles.Count == 0 ? 1 : 0;
    }

    private static int RunWizard()
    {
        Console.WriteLine("HorizonSimTool mapping wizard");
        Console.WriteLine();

        var devices = DeviceEnumerator.GetPresentVidPidDevices();
        PrintDevices(devices);

        var mediaFolder = ChooseMediaFolder();
        var inputZipPath = PromptPath(
            "Input mapping zip",
            mediaFolder is null ? null : Path.Combine(mediaFolder, InputZipName),
            mustExist: true);
        var wheelZipPath = PromptPath(
            "Wheel tune zip",
            mediaFolder is null ? null : Path.Combine(mediaFolder, WheelZipName),
            mustExist: true);

        var profiles = ZipProfileReader.ReadProfiles(inputZipPath);
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("No RawGameController XML profiles were found in " + inputZipPath);
        }

        var selectedProfile = ChooseProfile(profiles);
        var document = ZipProfileReader.ReadXml(inputZipPath, selectedProfile.EntryName);
        var profileElement = XmlProfileEditor.GetProfileElement(document);
        var discoveredVidPids = XmlProfileEditor.GetVidPidValues(document);

        Console.WriteLine();
        Console.WriteLine("VID/PID values currently present in the selected XML:");
        foreach (var value in discoveredVidPids)
        {
            Console.WriteLine("  " + value.ToXmlString());
        }

        var currentPrimary = XmlProfileEditor.TryGetProfileVidPid(profileElement, "PrimaryDeviceVidPid");
        var currentFfb = XmlProfileEditor.TryGetProfileVidPid(profileElement, "FFBDeviceVidPid")
            ?? XmlProfileEditor.TryGetProfileVidPid(profileElement, "FFBVidPid")
            ?? currentPrimary;

        var primary = PromptVidPid(devices, "Select the wheelbase/primary device", currentPrimary);
        var ffb = PromptVidPid(devices, "Select the force feedback device", currentFfb ?? primary);

        var replacements = PromptReplacements(devices, discoveredVidPids, currentPrimary, currentFfb, primary, ffb);
        XmlProfileEditor.ApplyVidPidReplacements(document, replacements);
        XmlProfileEditor.SetPrimaryAndFfb(profileElement, primary, ffb);
        ConfigureDInputOverrides(document, profileElement);

        var destinationProfileName = selectedProfile.EntryName;
        if (!PromptYesNo("Patch the selected XML profile in place?", defaultYes: true))
        {
            var defaultName = "DefaultRawGameControllerMappingProfileCodexCustom.xml";
            destinationProfileName = PromptFileName("New XML profile filename", defaultName, ".xml");
            XmlProfileEditor.AssignNewProfileId(profileElement);
        }

        var patchedXml = XmlProfileEditor.ToUtf8Xml(document);

        var ffbEntries = ZipWheelTuneReader.ReadEntries(wheelZipPath);
        if (ffbEntries.Count == 0)
        {
            throw new InvalidOperationException("No ControllerFFB INI files were found in " + wheelZipPath);
        }

        var ffbTemplate = ChooseFfbTemplate(ffbEntries, currentFfb ?? ffb);
        var templateIni = ZipWheelTuneReader.ReadText(wheelZipPath, ffbTemplate.EntryName);
        var patchedIni = IniEditor.SetVendorProduct(templateIni, ffb);
        var destinationIniName = $"ControllerFFB-0X{ffb.Compact}.ini";

        var outputFolder = PromptPath("Output folder", Path.Combine(Environment.CurrentDirectory, "output"), mustExist: false);
        Directory.CreateDirectory(outputFolder);

        var xmlPreviewPath = Path.Combine(outputFolder, Path.GetFileName(destinationProfileName));
        var iniPreviewPath = Path.Combine(outputFolder, destinationIniName);
        File.WriteAllBytes(xmlPreviewPath, patchedXml);
        File.WriteAllText(iniPreviewPath, patchedIni, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var outputInputZip = Path.Combine(outputFolder, InputZipName);
        var outputWheelZip = Path.Combine(outputFolder, WheelZipName);

        ZipWriter.WriteInputMappingZip(inputZipPath, outputInputZip, selectedProfile.EntryName, destinationProfileName, patchedXml);
        ZipWriter.WriteWheelTuneZip(wheelZipPath, outputWheelZip, ffbTemplate.EntryName, destinationIniName, patchedIni, Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine("Generated:");
        Console.WriteLine("  " + xmlPreviewPath);
        Console.WriteLine("  " + iniPreviewPath);
        Console.WriteLine("  " + outputInputZip);
        Console.WriteLine("  " + outputWheelZip);

        if (mediaFolder is not null && PromptYesNo("Back up the original zips and install the generated zips now?", defaultYes: false))
        {
            BackupAndInstall(mediaFolder, outputInputZip, outputWheelZip, outputFolder);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Install skipped. You can copy the generated zips into the game's media folder later.");
        }

        return 0;
    }

    private static string? ChooseMediaFolder()
    {
        var candidates = ForzaInstallFinder.FindMediaFolders();
        Console.WriteLine();

        if (candidates.Count == 0)
        {
            Console.WriteLine("No game media folder was found automatically.");
            return PromptYesNo("Enter the game media folder manually?", defaultYes: true)
                ? PromptPath("Forza Horizon 6 media folder", null, mustExist: true)
                : null;
        }

        Console.WriteLine("Likely game media folders:");
        for (var i = 0; i < candidates.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {candidates[i]}");
        }

        while (true)
        {
            var answer = Prompt("Use which media folder? Enter a number, M for manual, or S to skip", "1");
            if (answer.Equals("s", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (answer.Equals("m", StringComparison.OrdinalIgnoreCase))
            {
                return PromptPath("Forza Horizon 6 media folder", null, mustExist: true);
            }

            if (int.TryParse(answer, out var index) && index >= 1 && index <= candidates.Count)
            {
                return candidates[index - 1];
            }

            Console.WriteLine("Please enter a listed number, M, or S.");
        }
    }

    private static void BackupAndInstall(string mediaFolder, string outputInputZip, string outputWheelZip, string outputFolder)
    {
        var backupFolder = GameFileInstaller.BackupAndInstall(mediaFolder, outputInputZip, outputWheelZip);

        Console.WriteLine();
        Console.WriteLine("Installed generated zips to:");
        Console.WriteLine("  " + mediaFolder);
        Console.WriteLine("Original zips were backed up to:");
        Console.WriteLine("  " + backupFolder);
    }

    private static void PrintDevices(IReadOnlyList<DeviceInfo> devices)
    {
        Console.WriteLine("Connected devices with VID/PID:");
        if (devices.Count == 0)
        {
            Console.WriteLine("  No present USB/HID VID/PID devices were detected.");
            return;
        }

        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            Console.WriteLine($"  [{i + 1,2}] VID={device.VidPid.Vid} PID={device.VidPid.Pid}  {device.VidPid.ToXmlString(),-12} {device.Name}");
            if (!string.IsNullOrWhiteSpace(device.ClassName) || !string.IsNullOrWhiteSpace(device.Manufacturer))
            {
                Console.WriteLine($"       {JoinNonEmpty(" | ", device.ClassName, device.Manufacturer)}");
            }
        }
    }

    private static ZipProfileInfo ChooseProfile(IReadOnlyList<ZipProfileInfo> profiles)
    {
        Console.WriteLine();
        Console.WriteLine("Raw game controller XML profiles:");
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            var display = profile.UserFacingName is null ? "" : " | " + profile.UserFacingName;
            var primary = profile.PrimaryDeviceVidPid is null ? "" : " | " + profile.PrimaryDeviceVidPid.Value.ToXmlString();
            Console.WriteLine($"  [{i + 1,2}] {profile.EntryName}{display}{primary}");
        }

        var defaultIndex = 1;
        for (var i = 0; i < profiles.Count; i++)
        {
            var primary = profiles[i].PrimaryDeviceVidPid;
            if (primary.HasValue && !primary.Value.Compact.Equals("FFFFFFFF", StringComparison.OrdinalIgnoreCase))
            {
                defaultIndex = i + 1;
                break;
            }
        }

        while (true)
        {
            var answer = Prompt("Select the XML profile to patch", defaultIndex.ToString());
            if (int.TryParse(answer, out var index) && index >= 1 && index <= profiles.Count)
            {
                return profiles[index - 1];
            }

            Console.WriteLine("Please enter one of the listed numbers.");
        }
    }

    private static IReadOnlyDictionary<VidPid, VidPid> PromptReplacements(
        IReadOnlyList<DeviceInfo> devices,
        IReadOnlyList<VidPid> existingValues,
        VidPid? currentPrimary,
        VidPid? currentFfb,
        VidPid newPrimary,
        VidPid newFfb)
    {
        var replacements = new Dictionary<VidPid, VidPid>();
        Console.WriteLine();
        Console.WriteLine("Map XML VID/PID values to your connected devices.");
        Console.WriteLine("Use P for primary, F for FFB, K to keep the current value, a device number, or a manual VID/PID.");

        foreach (var oldValue in existingValues)
        {
            var defaultValue =
                currentPrimary == oldValue ? "P" :
                currentFfb == oldValue ? "F" :
                "K";

            while (true)
            {
                var answer = Prompt($"Map {oldValue.ToXmlString()}", defaultValue);
                if (answer.Equals("k", StringComparison.OrdinalIgnoreCase))
                {
                    replacements[oldValue] = oldValue;
                    break;
                }

                if (answer.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    replacements[oldValue] = newPrimary;
                    break;
                }

                if (answer.Equals("f", StringComparison.OrdinalIgnoreCase))
                {
                    replacements[oldValue] = newFfb;
                    break;
                }

                if (int.TryParse(answer, out var index) && index >= 1 && index <= devices.Count)
                {
                    replacements[oldValue] = devices[index - 1].VidPid;
                    break;
                }

                if (VidPid.TryParse(answer, out var manual))
                {
                    replacements[oldValue] = manual;
                    break;
                }

                Console.WriteLine("Enter P, F, K, a listed device number, or a VID/PID such as 3670:0501.");
            }
        }

        return replacements;
    }

    private static void ConfigureDInputOverrides(XDocument document, XElement profileElement)
    {
        Console.WriteLine();
        if (PromptYesNo("Set DInputFFBInvertAxis on the profile?", defaultYes: false))
        {
            var value = PromptBool("DInputFFBInvertAxis");
            profileElement.SetAttributeValue("DInputFFBInvertAxis", value ? "true" : "false");
        }

        if (!PromptYesNo("Add or update per-command DInputIndex/DInputInvertAxis overrides?", defaultYes: false))
        {
            return;
        }

        var values = XmlProfileEditor.GetInputValues(document)
            .OrderBy(v => v.Key)
            .ThenBy(v => v.VidPid?.ToXmlString())
            .ToList();

        Console.WriteLine();
        Console.WriteLine("Mapped inputs:");
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            Console.WriteLine($"  [{i + 1,3}] {value.Key,-32} {value.InputType,-8} Index={value.Index,-3} Invert={value.InvertAxis,-5} {value.VidPid?.ToXmlString()}");
        }

        while (true)
        {
            var answer = Prompt("Input number to edit, or blank to finish", "");
            if (string.IsNullOrWhiteSpace(answer))
            {
                return;
            }

            if (!int.TryParse(answer, out var index) || index < 1 || index > values.Count)
            {
                Console.WriteLine("Please enter one of the listed input numbers.");
                continue;
            }

            var selected = values[index - 1];
            var dInputIndex = Prompt("DInputIndex file value (blank leaves unchanged)", "");
            if (!string.IsNullOrWhiteSpace(dInputIndex))
            {
                if (!int.TryParse(dInputIndex, out _))
                {
                    Console.WriteLine("DInputIndex must be a number.");
                    continue;
                }

                selected.Element.SetAttributeValue("DInputIndex", dInputIndex.Trim());
            }

            var invert = Prompt("DInputInvertAxis true/false (blank leaves unchanged)", "");
            if (!string.IsNullOrWhiteSpace(invert))
            {
                if (!TryParseBool(invert, out var invertValue))
                {
                    Console.WriteLine("Enter true or false.");
                    continue;
                }

                selected.Element.SetAttributeValue("DInputInvertAxis", invertValue ? "true" : "false");
            }
        }
    }

    private static ZipWheelTuneInfo ChooseFfbTemplate(IReadOnlyList<ZipWheelTuneInfo> entries, VidPid defaultVidPid)
    {
        Console.WriteLine();
        var defaultEntry = entries.FirstOrDefault(e => e.EntryName.Contains(defaultVidPid.Compact, StringComparison.OrdinalIgnoreCase))
            ?? entries.First();

        Console.WriteLine("Wheel tune INI templates:");
        for (var i = 0; i < entries.Count; i++)
        {
            var marker = entries[i].EntryName == defaultEntry.EntryName ? "*" : " ";
            Console.WriteLine($" {marker}[{i + 1,3}] {entries[i].EntryName}");
        }

        while (true)
        {
            var defaultIndex = 1;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].EntryName.Equals(defaultEntry.EntryName, StringComparison.OrdinalIgnoreCase))
                {
                    defaultIndex = i + 1;
                    break;
                }
            }

            var answer = Prompt("Select the FFB INI template", defaultIndex.ToString());
            if (int.TryParse(answer, out var index) && index >= 1 && index <= entries.Count)
            {
                return entries[index - 1];
            }

            Console.WriteLine("Please enter one of the listed numbers.");
        }
    }

    private static VidPid PromptVidPid(IReadOnlyList<DeviceInfo> devices, string label, VidPid? defaultValue)
    {
        Console.WriteLine();
        Console.WriteLine(label + ".");
        Console.WriteLine("Enter a listed device number or a VID/PID such as 3670:0501, 0x36700501, or VID_3670&PID_0501.");

        while (true)
        {
            var answer = Prompt(label, defaultValue?.ToXmlString());
            if (int.TryParse(answer, out var index) && index >= 1 && index <= devices.Count)
            {
                return devices[index - 1].VidPid;
            }

            if (VidPid.TryParse(answer, out var vidPid))
            {
                return vidPid;
            }

            Console.WriteLine("Please enter a listed device number or a valid VID/PID.");
        }
    }

    private static string PromptPath(string label, string? defaultValue, bool mustExist)
    {
        while (true)
        {
            var value = Prompt(label, defaultValue).Trim('"');
            if (string.IsNullOrWhiteSpace(value))
            {
                Console.WriteLine("A path is required.");
                continue;
            }

            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
            if (!mustExist || File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                return fullPath;
            }

            Console.WriteLine("That path does not exist: " + fullPath);
        }
    }

    private static string PromptFileName(string label, string defaultValue, string requiredExtension)
    {
        while (true)
        {
            var value = Prompt(label, defaultValue).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                Console.WriteLine("A filename is required.");
                continue;
            }

            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Console.WriteLine("That filename contains invalid characters.");
                continue;
            }

            if (!value.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                value += requiredExtension;
            }

            return value;
        }
    }

    private static string Prompt(string label, string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
        {
            Console.Write(label + ": ");
        }
        else
        {
            Console.Write($"{label} [{defaultValue}]: ");
        }

        var answer = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(answer) && !string.IsNullOrWhiteSpace(defaultValue))
        {
            return defaultValue;
        }

        return answer ?? "";
    }

    private static bool PromptYesNo(string label, bool defaultYes)
    {
        while (true)
        {
            var suffix = defaultYes ? "Y/n" : "y/N";
            var answer = Prompt($"{label} ({suffix})", defaultYes ? "Y" : "N");
            if (string.IsNullOrWhiteSpace(answer))
            {
                return defaultYes;
            }

            if (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (answer.Equals("n", StringComparison.OrdinalIgnoreCase) || answer.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Console.WriteLine("Please answer yes or no.");
        }
    }

    private static bool PromptBool(string label)
    {
        while (true)
        {
            var answer = Prompt(label + " true/false", null);
            if (TryParseBool(answer, out var value))
            {
                return value;
            }

            Console.WriteLine("Please enter true or false.");
        }
    }

    private static bool TryParseBool(string value, out bool result)
    {
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("t", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("y", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("f", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("n", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static string JoinNonEmpty(string separator, params string?[] values)
    {
        return string.Join(separator, values.Where(v => !string.IsNullOrWhiteSpace(v)));
    }
}

internal readonly record struct VidPid(string Vid, string Pid)
{
    private static readonly Regex VidPidRegex = new(
        @"VID[_=:\- ]?([0-9a-fA-F]{4}).*PID[_=:\- ]?([0-9a-fA-F]{4})|0x([0-9a-fA-F]{4})([0-9a-fA-F]{4})|^([0-9a-fA-F]{4})[:;\- ]([0-9a-fA-F]{4})$|^([0-9a-fA-F]{8})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Vid { get; } = NormalizeHalf(Vid);
    public string Pid { get; } = NormalizeHalf(Pid);
    public string Compact => Vid + Pid;

    public static bool TryParse(string? value, out VidPid vidPid)
    {
        vidPid = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VidPidRegex.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        string vid;
        string pid;
        if (match.Groups[1].Success)
        {
            vid = match.Groups[1].Value;
            pid = match.Groups[2].Value;
        }
        else if (match.Groups[3].Success)
        {
            vid = match.Groups[3].Value;
            pid = match.Groups[4].Value;
        }
        else if (match.Groups[5].Success)
        {
            vid = match.Groups[5].Value;
            pid = match.Groups[6].Value;
        }
        else
        {
            var compact = match.Groups[7].Value;
            vid = compact[..4];
            pid = compact[4..];
        }

        vidPid = new VidPid(vid, pid);
        return true;
    }

    public string ToXmlString() => "0x" + Compact;
    public override string ToString() => ToXmlString();

    private static string NormalizeHalf(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 4 || value.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new ArgumentException("VID and PID values must be four hex characters.");
        }

        return value.ToUpperInvariant();
    }
}

internal sealed record DeviceInfo(
    string Name,
    VidPid VidPid,
    string ClassName,
    string Manufacturer,
    string InstanceId,
    string HardwareId,
    string CompatibleIds,
    bool UsesWindowsSettingsName);

internal enum DeviceSilencerStatus
{
    Enabled,
    Disabled,
    Unknown
}

internal static class DeviceEnumerator
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const uint SpdrpDeviceDesc = 0x00000000;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint SpdrpCompatibleIds = 0x00000002;
    private const uint SpdrpClass = 0x00000007;
    private const uint SpdrpMfg = 0x0000000B;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private static readonly IntPtr InvalidHandleValue = new(-1);
    private static readonly DevPropKey DevpkeyDeviceBusReportedDeviceDesc = new(
        new Guid("540B947E-8B40-45BC-A8A2-6A0B894CBDA2"),
        4);

    public static IReadOnlyList<DeviceInfo> GetPresentVidPidDevices()
    {
        return EnumeratePresentVidPidDevices()
            .GroupBy(d => d.VidPid)
            .Select(g => g.OrderByDescending(DeviceNameScore).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.VidPid.Compact, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<DeviceInfo> GetPresentControllerDevices()
    {
        return EnumeratePresentVidPidDevices()
            .Where(IsControllerDevice)
            .GroupBy(d => d.VidPid)
            .Select(g => g.OrderByDescending(DeviceNameScore).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.VidPid.Compact, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<DeviceInfo> EnumeratePresentVidPidDevices()
    {
        var devices = new List<DeviceInfo>();
        var deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == InvalidHandleValue)
        {
            return devices;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new SpDevinfoData
                {
                    CbSize = Marshal.SizeOf<SpDevinfoData>()
                };

                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref data))
                {
                    break;
                }

                var instanceId = GetDeviceInstanceId(deviceInfoSet, data);
                var hardwareId = GetDeviceProperty(deviceInfoSet, data, SpdrpHardwareId);
                var parseSource = instanceId + "\0" + hardwareId;
                if (!VidPid.TryParse(parseSource, out var vidPid))
                {
                    continue;
                }

                var friendlyName = GetDeviceProperty(deviceInfoSet, data, SpdrpFriendlyName);
                var description = GetDeviceProperty(deviceInfoSet, data, SpdrpDeviceDesc);
                var compatibleIds = GetDeviceProperty(deviceInfoSet, data, SpdrpCompatibleIds);
                var windowsSettingsName = GetDeviceProperty(deviceInfoSet, data, DevpkeyDeviceBusReportedDeviceDesc);
                var usesWindowsSettingsName = !string.IsNullOrWhiteSpace(windowsSettingsName);
                var name = FirstNonEmpty(windowsSettingsName, friendlyName, description, instanceId, vidPid.ToXmlString());
                var className = GetDeviceProperty(deviceInfoSet, data, SpdrpClass);
                var manufacturer = GetDeviceProperty(deviceInfoSet, data, SpdrpMfg);

                devices.Add(new DeviceInfo(name, vidPid, className, manufacturer, instanceId, hardwareId, compatibleIds, usesWindowsSettingsName));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return devices;
    }

    private static readonly string[] KnownSimBrands =
    [
        "moza", "simagic", "simsonn", "fanatec", "simucube", "thrustmaster",
        "logitech g", "bemaster", "cammus", "accuforce", "leo bodnar",
        "heusinkveld", "ricmotech", "cube controls", "asetek", "simline",
        "vrs", "augury", "podium", "csl", "csp", "csr",
    ];

    private static bool IsControllerDevice(DeviceInfo device)
    {
        var haystack = string.Join(" ", device.Name, device.ClassName, device.Manufacturer, device.InstanceId, device.HardwareId, device.CompatibleIds);

        // Always include known sim hardware brands regardless of class
        var isKnownSimBrand = KnownSimBrands.Any(b => haystack.Contains(b, StringComparison.OrdinalIgnoreCase));

        if (!isKnownSimBrand
            && (device.ClassName.Equals("USB", StringComparison.OrdinalIgnoreCase)
                || haystack.Contains("USB Composite Device", StringComparison.OrdinalIgnoreCase)
                || haystack.Contains("USB Host Controller", StringComparison.OrdinalIgnoreCase)
                || haystack.Contains("HID-compliant system controller", StringComparison.OrdinalIgnoreCase)
                || haystack.Contains("Logitech Gaming HID Device", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var hasControllerSignal =
            haystack.Contains("HID_DEVICE_SYSTEM_GAME", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("HID_DEVICE_SYSTEM_JOYSTICK", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("HID_DEVICE_UP:0001_U:0004", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("HID_DEVICE_UP:0001_U:0005", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("HID_DEVICE_UP:0001_U:0008", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("IG_", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("XnaComposite", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("XboxComposite", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("game controller", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("gamepad", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("joystick", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("wireless controller", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("xbox", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("dualshock", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("dualsense", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("playstation", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("wheel", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("wheelbase", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("pedal", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("shifter", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("handbrake", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("hand brake", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("simagic", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("simsonn", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("fanatec", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("moza", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("simucube", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("thrustmaster", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("logitech g", StringComparison.OrdinalIgnoreCase);

        if (!hasControllerSignal)
        {
            return false;
        }

        var explicitNonController =
            haystack.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("mouse", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("touchpad", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("webcam", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("camera", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("microphone", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("headset", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("audio", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("aura", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("rgb", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("lighting", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("led controller", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("root hub", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("usb hub", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("stream deck", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("monitor", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("display", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("printer", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("scanner", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("network", StringComparison.OrdinalIgnoreCase);

        if (explicitNonController
            && !haystack.Contains("game controller", StringComparison.OrdinalIgnoreCase)
            && !haystack.Contains("HID_DEVICE_SYSTEM_GAME", StringComparison.OrdinalIgnoreCase)
            && !haystack.Contains("HID_DEVICE_SYSTEM_JOYSTICK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
    }

    private static string NormalizeDeviceName(string name)
    {
        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    private static int DeviceNameScore(DeviceInfo device)
    {
        var name = NormalizeDeviceName(device.Name);
        var score = 0;

        if (device.UsesWindowsSettingsName)
        {
            score += 500;
        }

        if (!name.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("USB Input Device", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("USB Composite Device", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (name.Contains("game controller", StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        if (device.ClassName.Contains("HIDClass", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        if (!string.IsNullOrWhiteSpace(device.Manufacturer)
            && !device.Manufacturer.Contains("standard", StringComparison.OrdinalIgnoreCase)
            && !device.Manufacturer.Contains("microsoft", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static string GetDeviceProperty(IntPtr deviceInfoSet, SpDevinfoData data, uint property)
    {
        var buffer = new byte[8192];
        if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref data, property, out _, buffer, buffer.Length, out var requiredSize))
        {
            return "";
        }

        var size = Math.Min(requiredSize, buffer.Length);
        return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    private static string GetDeviceProperty(IntPtr deviceInfoSet, SpDevinfoData data, DevPropKey propertyKey)
    {
        var key = propertyKey;
        var buffer = new byte[8192];
        if (!SetupDiGetDeviceProperty(deviceInfoSet, ref data, ref key, out _, buffer, buffer.Length, out _, 0))
        {
            return "";
        }

        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    private static string GetDeviceInstanceId(IntPtr deviceInfoSet, SpDevinfoData data)
    {
        var builder = new StringBuilder(1024);
        return SetupDiGetDeviceInstanceId(deviceInfoSet, ref data, builder, builder.Capacity, out _)
            ? builder.ToString()
            : "";
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[] propertyBuffer,
        int propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        StringBuilder deviceInstanceId,
        int deviceInstanceIdSize,
        out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        ref DevPropKey propertyKey,
        out uint propertyType,
        byte[] propertyBuffer,
        int propertyBufferSize,
        out int requiredSize,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int CbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public DevPropKey(Guid fmtid, uint pid)
        {
            Fmtid = fmtid;
            Pid = pid;
        }

        public Guid Fmtid;
        public uint Pid;
    }
}

internal static class DeviceSilencer
{
    private const int CrSuccess = 0x00000000;
    private const int CmLocateDevnodeNormal = 0x00000000;
    private const int CmLocateDevnodePhantom = 0x00000001;
    private const uint CmProbDisabled = 0x00000016;

    public static bool IsRunningElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static IReadOnlyList<string> LoadSilencedDeviceIds()
    {
        var path = StateFilePath();
        return File.Exists(path)
            ? File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<string>();
    }

    public static void SaveSilencedDeviceIds(IEnumerable<string> instanceIds)
    {
        var path = StateFilePath();
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllLines(path, instanceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static void ClearSilencedDeviceIds()
    {
        var path = StateFilePath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static DeviceSilencerStatus GetDeviceStatus(string instanceId)
    {
        try
        {
            var devInst = LocateDevNode(instanceId);
            var result = CM_Get_DevNode_Status(out _, out var problemNumber, devInst, 0);
            if (result != CrSuccess)
            {
                return DeviceSilencerStatus.Unknown;
            }

            return problemNumber == CmProbDisabled
                ? DeviceSilencerStatus.Disabled
                : DeviceSilencerStatus.Enabled;
        }
        catch
        {
            return DeviceSilencerStatus.Unknown;
        }
    }

    public static void DisableDevice(string instanceId)
    {
        var devInst = LocateDevNode(instanceId);
        var result = CM_Disable_DevNode(devInst, 0);
        if (result != CrSuccess)
        {
            throw new InvalidOperationException($"Could not disable device {instanceId}. Configuration Manager returned 0x{result:X4}.");
        }
    }

    public static void EnableDevice(string instanceId)
    {
        var devInst = LocateDevNode(instanceId);
        var result = CM_Enable_DevNode(devInst, 0);
        if (result != CrSuccess)
        {
            throw new InvalidOperationException($"Could not restore device {instanceId}. Configuration Manager returned 0x{result:X4}.");
        }
    }

    private static int LocateDevNode(string instanceId)
    {
        var result = CM_Locate_DevNode(out var devInst, instanceId, CmLocateDevnodeNormal);
        if (result == CrSuccess)
        {
            return devInst;
        }

        result = CM_Locate_DevNode(out devInst, instanceId, CmLocateDevnodePhantom);
        if (result == CrSuccess)
        {
            return devInst;
        }

        throw new InvalidOperationException($"Could not find device {instanceId}. Configuration Manager returned 0x{result:X4}.");
    }

    private static string StateFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HorizonSimTool",
            "silenced-devices.txt");
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out int pdnDevInst, string pDeviceID, int ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Disable_DevNode(int dnDevInst, int ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Enable_DevNode(int dnDevInst, int ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber, int dnDevInst, int ulFlags);
}

internal static class ForzaInstallFinder
{
    public static IReadOnlyList<string> FindMediaFolders()
    {
        var candidates = new List<string>
        {
            // Steam defaults
            @"C:\Program Files (x86)\Steam\steamapps\common\ForzaHorizon6\media",
            @"C:\Program Files\Steam\steamapps\common\ForzaHorizon6\media",
        };

        // Xbox Game Pass / Xbox app — scan all fixed drives
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            var root = drive.RootDirectory.FullName;
            candidates.Add(Path.Combine(root, "XboxGames", "Forza Horizon 6", "Content", "media"));
            candidates.Add(Path.Combine(root, "XboxGames", "Forza Horizon 5", "Content", "media"));
            candidates.Add(Path.Combine(root, "XboxGames", "ForzaHorizon6", "Content", "media"));
        }

        // Steam registry paths
        foreach (var steamPath in FindSteamPaths())
        {
            candidates.Add(Path.Combine(steamPath, "steamapps", "common", "ForzaHorizon6", "media"));
            foreach (var library in ReadSteamLibraries(steamPath))
            {
                candidates.Add(Path.Combine(library, "steamapps", "common", "ForzaHorizon6", "media"));
            }
        }

        return candidates
            .Select(SafeFullPath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsForzaMediaFolder)
            .ToList();
    }

    private static IEnumerable<string> FindSteamPaths()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var steamKey = baseKey.OpenSubKey(@"Software\Valve\Steam");
                foreach (var valueName in new[] { "SteamPath", "InstallPath" })
                {
                    if (steamKey?.GetValue(valueName) is string path && Directory.Exists(path))
                    {
                        yield return path.Replace('/', '\\');
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ReadSteamLibraries(string steamPath)
    {
        var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
        {
            yield break;
        }

        var text = File.ReadAllText(libraryFile);
        foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
        {
            var path = match.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static string? SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsForzaMediaFolder(string path)
    {
        return Directory.Exists(path)
            && File.Exists(Path.Combine(path, "inputmappingprofiles.zip"));
    }
}

internal sealed record ZipProfileInfo(
    string EntryName,
    string? UserFacingName,
    VidPid? PrimaryDeviceVidPid);

internal static class ZipProfileReader
{
    public static IReadOnlyList<ZipProfileInfo> ReadProfiles(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var profiles = new List<ZipProfileInfo>();
        foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var stream = entry.Open();
                var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                var profile = XmlProfileEditor.TryGetProfileElement(document);
                if (profile is null)
                {
                    continue;
                }

                var userFacingName = profile.Attribute("UserFacingName")?.Value;
                var primary = XmlProfileEditor.TryGetProfileVidPid(profile, "PrimaryDeviceVidPid");
                profiles.Add(new ZipProfileInfo(entry.FullName, userFacingName, primary));
            }
            catch
            {
                // Some XML files are small placeholders. Ignore files that are not parseable profiles.
            }
        }

        return profiles
            .OrderBy(p => p.EntryName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static XDocument ReadXml(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException("Zip entry not found: " + entryName);
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }
}

internal sealed record XmlInputValue(
    XElement Element,
    string Key,
    string InputType,
    string Index,
    string InvertAxis,
    VidPid? VidPid);

internal static class XmlProfileEditor
{
    public static XElement GetProfileElement(XDocument document)
    {
        return TryGetProfileElement(document)
            ?? throw new InvalidOperationException("The selected XML does not contain a RawGameControllerInputMappingProfile element.");
    }

    public static XElement? TryGetProfileElement(XDocument document)
    {
        return document
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName.Equals("RawGameControllerInputMappingProfile", StringComparison.OrdinalIgnoreCase));
    }

    public static VidPid? TryGetProfileVidPid(XElement profileElement, string attributeName)
    {
        var value = profileElement.Attribute(attributeName)?.Value;
        return VidPid.TryParse(value, out var vidPid) ? vidPid : null;
    }

    public static IReadOnlyList<VidPid> GetVidPidValues(XDocument document)
    {
        return document
            .Descendants()
            .SelectMany(e => e.Attributes())
            .Where(IsVidPidAttribute)
            .Select(a => VidPid.TryParse(a.Value, out var vidPid) ? (VidPid?)vidPid : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .Distinct()
            .OrderBy(v => v.Compact, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<XmlInputValue> GetInputValues(XDocument document)
    {
        return document
            .Descendants()
            .Where(e => e.Name.LocalName.Equals("Value", StringComparison.OrdinalIgnoreCase))
            .Select(e =>
            {
                var vidPid = VidPid.TryParse(e.Attribute("VidPid")?.Value, out var parsed) ? parsed : (VidPid?)null;
                return new XmlInputValue(
                    e,
                    e.Attribute("Key")?.Value ?? "",
                    e.Attribute("InputType")?.Value ?? "",
                    e.Attribute("Index")?.Value ?? "",
                    e.Attribute("InvertAxis")?.Value ?? "",
                    vidPid);
            })
            .Where(v => !string.IsNullOrWhiteSpace(v.Key))
            .ToList();
    }

    public static void ApplyVidPidReplacements(XDocument document, IReadOnlyDictionary<VidPid, VidPid> replacements)
    {
        foreach (var attribute in document.Descendants().SelectMany(e => e.Attributes()).Where(IsVidPidAttribute))
        {
            if (VidPid.TryParse(attribute.Value, out var oldValue) && replacements.TryGetValue(oldValue, out var newValue))
            {
                attribute.Value = newValue.ToXmlString();
            }
        }
    }

    public static void SetPrimaryAndFfb(XElement profileElement, VidPid primary, VidPid ffb)
    {
        profileElement.SetAttributeValue("PrimaryDeviceVidPid", primary.ToXmlString());

        var hasFfbDevice = profileElement.Attribute("FFBDeviceVidPid") is not null;
        var hasFfb = profileElement.Attribute("FFBVidPid") is not null;
        if (hasFfbDevice)
        {
            profileElement.SetAttributeValue("FFBDeviceVidPid", ffb.ToXmlString());
        }

        if (hasFfb)
        {
            profileElement.SetAttributeValue("FFBVidPid", ffb.ToXmlString());
        }

        if (!hasFfbDevice && !hasFfb)
        {
            profileElement.SetAttributeValue("FFBDeviceVidPid", ffb.ToXmlString());
        }

        if (profileElement.Attribute("FFBMotorIndex") is null)
        {
            profileElement.SetAttributeValue("FFBMotorIndex", "0");
        }
    }

    public static void AssignNewProfileId(XElement profileElement)
    {
        profileElement.SetAttributeValue("Id", Guid.NewGuid().ToString());
    }

    public static byte[] ToUtf8Xml(XDocument document)
    {
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = document.Declaration is null
        };

        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    private static bool IsVidPidAttribute(XAttribute attribute)
    {
        var name = attribute.Name.LocalName;
        return name.Equals("VidPid", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("VidPid", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record ZipWheelTuneInfo(string EntryName);

internal static class ZipWheelTuneReader
{
    public static IReadOnlyList<ZipWheelTuneInfo> ReadEntries(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries
            .Where(e => e.FullName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            .Select(e => new ZipWheelTuneInfo(e.FullName))
            .OrderBy(e => e.EntryName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ReadText(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException("Zip entry not found: " + entryName);
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

internal static class IniEditor
{
    private static readonly Regex VendorProductRegex = new(
        @"^\s*VendorProduct\s+0x[0-9a-fA-F]{8}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    public static string SetVendorProduct(string iniText, VidPid vidPid)
    {
        var value = "VendorProduct " + vidPid.ToXmlString();
        if (VendorProductRegex.IsMatch(iniText))
        {
            return VendorProductRegex.Replace(iniText, value);
        }

        var newline = iniText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return iniText.TrimEnd() + newline + value + newline;
    }
}

internal static class ZipWriter
{
    public static void WriteInputMappingZip(
        string sourceZipPath,
        string destinationZipPath,
        string selectedEntryName,
        string patchedEntryName,
        byte[] patchedXml)
    {
        WriteArchive(sourceZipPath, destinationZipPath, entry =>
        {
            if (entry.FullName.Equals(selectedEntryName, StringComparison.OrdinalIgnoreCase)
                && patchedEntryName.Equals(selectedEntryName, StringComparison.OrdinalIgnoreCase))
            {
                return new ReplacementEntry(TopLevelEntryName(patchedEntryName), patchedXml);
            }

            return null;
        },
        extraEntries: patchedEntryName.Equals(selectedEntryName, StringComparison.OrdinalIgnoreCase)
            ? Array.Empty<ReplacementEntry>()
            : new[] { new ReplacementEntry(TopLevelEntryName(patchedEntryName), patchedXml) });
    }

    public static void WriteWheelTuneZip(
        string sourceZipPath,
        string destinationZipPath,
        string templateEntryName,
        string patchedEntryName,
        string patchedIni,
        Encoding encoding)
    {
        var bytes = encoding.GetBytes(patchedIni);
        WriteArchive(sourceZipPath, destinationZipPath, entry =>
        {
            var topLevelName = TopLevelEntryName(entry.FullName);
            if (entry.FullName.Equals(templateEntryName, StringComparison.OrdinalIgnoreCase)
                && !topLevelName.Equals(patchedEntryName, StringComparison.OrdinalIgnoreCase))
            {
                return ReplacementEntry.Skip();
            }

            if (topLevelName.Equals(patchedEntryName, StringComparison.OrdinalIgnoreCase))
            {
                return new ReplacementEntry(patchedEntryName, bytes);
            }

            return null;
        },
        extraEntries: new[] { new ReplacementEntry(patchedEntryName, bytes) });
    }

    private static void WriteArchive(
        string sourceZipPath,
        string destinationZipPath,
        Func<ZipArchiveEntry, ReplacementEntry?> replacementFactory,
        IReadOnlyList<ReplacementEntry> extraEntries)
    {
        var temporaryPath = destinationZipPath + ".tmp";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        var writtenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var source = ZipFile.OpenRead(sourceZipPath))
        using (var destination = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
        {
            foreach (var sourceEntry in source.Entries)
            {
                if (sourceEntry.FullName.EndsWith("/", StringComparison.Ordinal) || sourceEntry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    continue;
                }

                var replacement = replacementFactory(sourceEntry);
                if (replacement?.IsSkip == true)
                {
                    continue;
                }

                var entryName = replacement?.EntryName ?? TopLevelEntryName(sourceEntry.FullName);
                if (extraEntries.Any(e => e.EntryName.Equals(entryName, StringComparison.OrdinalIgnoreCase))
                    && replacement is null)
                {
                    continue;
                }

                if (!writtenNames.Add(entryName))
                {
                    throw new InvalidOperationException("The zip would contain duplicate top-level entry: " + entryName);
                }

                var destinationEntry = destination.CreateEntry(entryName, CompressionLevel.NoCompression);
                using var destinationStream = destinationEntry.Open();
                if (replacement is not null)
                {
                    destinationStream.Write(replacement.Bytes);
                }
                else
                {
                    using var sourceStream = sourceEntry.Open();
                    sourceStream.CopyTo(destinationStream);
                }
            }

            foreach (var extraEntry in extraEntries)
            {
                if (!writtenNames.Add(extraEntry.EntryName))
                {
                    continue;
                }

                var destinationEntry = destination.CreateEntry(extraEntry.EntryName, CompressionLevel.NoCompression);
                using var destinationStream = destinationEntry.Open();
                destinationStream.Write(extraEntry.Bytes);
            }
        }

        if (File.Exists(destinationZipPath))
        {
            File.Delete(destinationZipPath);
        }

        File.Move(temporaryPath, destinationZipPath);
    }

    private static string TopLevelEntryName(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').Trim('/');
        var fileName = normalized.Contains('/') ? Path.GetFileName(normalized) : normalized;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Invalid zip entry name: " + entryName);
        }

        return fileName;
    }

    private sealed record ReplacementEntry(string EntryName, byte[] Bytes, bool IsSkip = false)
    {
        public static ReplacementEntry Skip() => new("", Array.Empty<byte>(), IsSkip: true);
    }
}

internal static class GameFileInstaller
{
    private const string InputZipName = "inputmappingprofiles.zip";
    private const string WheelZipName = "wheeltunablesettingspc.zip";
    private const string BackupFolderName = "HST-BACKUP";

    public static string GetBackupFolder(string mediaFolder)
    {
        if (string.IsNullOrWhiteSpace(mediaFolder))
        {
            return "";
        }

        return Path.Combine(Path.GetFullPath(Environment.ExpandEnvironmentVariables(mediaFolder.Trim())), BackupFolderName);
    }

    public static string GetBackupInputZipPath(string mediaFolder) => Path.Combine(GetBackupFolder(mediaFolder), InputZipName);

    public static string GetBackupWheelZipPath(string mediaFolder) => Path.Combine(GetBackupFolder(mediaFolder), WheelZipName);

    public static bool BackupExists(string mediaFolder)
    {
        return File.Exists(GetBackupInputZipPath(mediaFolder))
            && File.Exists(GetBackupWheelZipPath(mediaFolder));
    }

    public static string EnsureBackup(string mediaFolder)
    {
        var inputTarget = Path.Combine(mediaFolder, InputZipName);
        var wheelTarget = Path.Combine(mediaFolder, WheelZipName);
        ValidateGameZipTargets(inputTarget, wheelTarget);

        var backupFolder = GetBackupFolder(mediaFolder);
        Directory.CreateDirectory(backupFolder);
        CopyIfMissing(inputTarget, GetBackupInputZipPath(mediaFolder));
        CopyIfMissing(wheelTarget, GetBackupWheelZipPath(mediaFolder));

        return backupFolder;
    }

    public static string CreateOrOverwriteBackup(string mediaFolder)
    {
        var inputTarget = Path.Combine(mediaFolder, InputZipName);
        var wheelTarget = Path.Combine(mediaFolder, WheelZipName);
        ValidateGameZipTargets(inputTarget, wheelTarget);

        var backupFolder = GetBackupFolder(mediaFolder);
        Directory.CreateDirectory(backupFolder);
        File.Copy(inputTarget, GetBackupInputZipPath(mediaFolder), overwrite: true);
        File.Copy(wheelTarget, GetBackupWheelZipPath(mediaFolder), overwrite: true);

        return backupFolder;
    }

    private static void ValidateGameZipTargets(string inputTarget, string wheelTarget)
    {
        if (!File.Exists(inputTarget))
        {
            throw new InvalidOperationException("The media folder is missing " + InputZipName + ".");
        }

        if (!File.Exists(wheelTarget))
        {
            throw new InvalidOperationException("The media folder is missing " + WheelZipName + ".");
        }
    }

    public static string BackupAndInstall(string mediaFolder, string outputInputZip, string? outputWheelZip)
    {
        var backupFolder = EnsureBackup(mediaFolder);
        var inputTarget = Path.Combine(mediaFolder, InputZipName);
        CopyUnlessSamePath(outputInputZip, inputTarget);
        if (!string.IsNullOrEmpty(outputWheelZip))
        {
            var wheelTarget = Path.Combine(mediaFolder, WheelZipName);
            CopyUnlessSamePath(outputWheelZip, wheelTarget);
        }
        return backupFolder;
    }

    public static string RestoreBackup(string mediaFolder)
    {
        var backupFolder = GetBackupFolder(mediaFolder);
        var backupInput = GetBackupInputZipPath(mediaFolder);
        var backupWheel = GetBackupWheelZipPath(mediaFolder);
        if (!File.Exists(backupInput) || !File.Exists(backupWheel))
        {
            throw new InvalidOperationException("HST-BACKUP does not contain both required game zips.");
        }

        File.Copy(backupInput, Path.Combine(mediaFolder, InputZipName), overwrite: true);
        File.Copy(backupWheel, Path.Combine(mediaFolder, WheelZipName), overwrite: true);
        return backupFolder;
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Copy(source, destination, overwrite: false);
        }
    }

    private static void CopyUnlessSamePath(string source, string destination)
    {
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, destination, overwrite: true);
        }
    }
}

internal static class ZipVerifier
{
    public static ZipVerificationResult VerifyStoreOnlyTopLevel(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var badEntries = archive.Entries
            .Where(entry =>
                entry.FullName.Contains("/", StringComparison.Ordinal)
                || entry.FullName.Contains("\\", StringComparison.Ordinal)
                || entry.CompressedLength != entry.Length)
            .Select(entry => entry.FullName)
            .ToList();

        return new ZipVerificationResult(archive.Entries.Count, badEntries);
    }
}

internal sealed record ZipVerificationResult(int EntryCount, IReadOnlyList<string> BadEntries)
{
    public bool Passed => BadEntries.Count == 0;
}
