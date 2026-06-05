# Horizon SimTool

<img width="3840" height="2075" alt="HST" src="https://github.com/user-attachments/assets/142934aa-8b3f-4f90-8817-78a0851cad24" />

Horizon SimTool is a Windows GUI utility for creating and installing Forza Horizon wheel, force feedback, and controller mapping fixes.

It helps sim racing users map their real hardware devices, generate patched XML and INI files, package them into the correct game zip files, and optionally silence extra controller devices while the app is running.

## Credits and Acknowledgements

This my first public app release! 

Horizon SimTool was inspired by community research, guides, troubleshooting posts, and official documentation shared by the Forza Horizon and sim racing communities on reddit.

Special thanks and credit to the following sources for documenting fixes, mapping behavior, VID/PID usage, force feedback configuration, DirectInput override behavior, backup practices, and troubleshooting steps that helped inspire this tool:

* Reddit user `swagikuro` and contributors from the Simagic community for the guide:
  [How to setup FFB on Forza Horizon WITHOUT Emuwheel and not needing it to be Device 1](https://www.reddit.com/r/Simagic/comments/1toeiim/how_to_setup_ffb_on_forza_horizon_without/)

* Reddit user `problemchild52` for the Moza R3 ESX mapping guide:
  [Moza R3 ESX Forza Horizon 6 Complete Mapping (90%) Finally!](https://www.reddit.com/r/ForzaHorizon6/comments/1tixvvw/moza_r3_esx_forza_horizon_6_complete_mapping_90/)

* Reddit user `Cptkrush` for the Fanatec CSL DD troubleshooting and configuration post:
  [For folks on PC (Steam) with issues getting their Fanatec CSL DD to work](https://www.reddit.com/r/ForzaHorizon/comments/1tf2dj0/for_folks_on_pc_steam_with_issues_getting_their/)

* The Forza Support Team for the official wheel input documentation:
  [Forza Horizon 6 on Wheel: Wheel Input on Steam](https://support.forza.net/hc/en-us/articles/51642975681427-Forza-Horizon-6-on-Wheel-Wheel-Input-on-Steam)

This project is not a copy of those guides. It was created to turn the manual community workflow into a more user friendly Windows GUI tool that can help users generate, back up, install, and manage their own wheel mapping and force feedback configuration files.

All credit belongs to the original authors and communities for the research and information they shared. Their guides helped make this tool possible for the community. Thanks!

## AI Disclosure

This application was fully created using AI with OpenAI Codex.

The code and full application was created with AI assistance. Review the code before relying on it, especially because this tool can modify game files and can disable controller-class devices system-wide.


## What The App Does

Horizon SimTool can:

- List connected USB/HID devices by Windows-style device name.
- Show each device VID, PID, and combined XML value.
- Filter connected devices to controller-class devices only.
- Find a likely Forza Horizon game `media` folder.
- Load `inputmappingprofiles.zip` and `wheeltunablesettingspc.zip`.
- Use `HST-BACKUP` as the clean source when backups are available.
- Assign devices to wheelbase/FFB, pedals, shifter, and handbrake roles.
- Patch the selected XML controller profile.
- Generate a matching force feedback INI file for the selected wheelbase.
- Create the required uncompressed zip files.
- Install generated files into the selected game media folder.
- Create and restore backups from `HST-BACKUP`.
- Save, import, export, delete, and set default presets.
- Disable extra controller devices system-wide from the Silence tab.
- Restore devices disabled by the app.
- Minimize to the Windows tray if enabled.

## How Device Mapping Works

The main mapping flow is built around the wheelbase.

The selected wheelbase is treated as the primary wheel and force feedback device. Its VID/PID is written into the XML profile and used for the generated `ControllerFFB-0XVIDPID.ini` file.

Pedals, shifter, and handbrake devices can also be selected as role devices. These role selections are saved in presets and used by the Silence tab so the app knows which controller devices should stay visible. Advanced command mapping is available for users who need to manually assign specific XML command rows to pedals, shifters, or handbrakes.

For most users:

1. Select the games `media` folder. "Find" will automatically find the folder and zip files.
2. Load or find the source zip files.
3. Select an XML profile and FFB INI template.
4. Set the wheelbase/FFB device.
5. Set pedals, shifter, and handbrake if used.
6. Generate files.
7. Install generated files.

## Tabs

### 1. Mapping

The Mapping tab is where hardware and game files are selected.

Use it to:

- Browse for or find the game `media` folder.
- Select source zip files if needed.
- Select XML profiles and FFB INI templates.
- View connected devices.
- Filter devices to controller devices only.
- Assign wheelbase/FFB, pedals, shifter, and handbrake roles.
- Open Advanced settings for command VID/PID and DirectInput overrides.

Green status fields mean that section is configured.

### 2. Generate / Install

The Generate / Install tab handles output, backup, restore, and installation.

Use it to:

- Create a backup in `HST-BACKUP`.
- Open the backup folder.
- Restore original backup files.
- Generate patched files into the Horizon SimTool output folder.
- Verify generated zip files.
- Install generated files into the selected game media folder.

Generating files does not install them by itself. Use **Install generated files** when you are ready to copy them into the game folder.

### 3. Silence

The Silence tab can disable extra controller-class devices system-wide.

Use it to:

- Keep configured role devices visible.
- Hide/disable extra controller devices.
- Manually choose which controller devices stay visible.
- Check the current enabled/disabled status of devices.
- Stop Silence and restore devices disabled by the app.
- Enable minimize-to-tray behavior.

Only controller-class devices are shown. Keyboards, mice, webcams, and other unrelated devices are intentionally left out.

## Presets

Presets are saved in the application folder under `HST-PRESETS`.

Presets can save:

- Selected media folder and source file paths.
- XML profile and FFB INI template selections.
- Wheelbase/FFB, pedals, shifter, and handbrake roles.
- Advanced mapping options.
- Silence tab visible/hidden selections.
- Minimize-to-tray preference.

Use:

- **Save Preset** to save the current configuration.
- **Set Default** to load that preset automatically next time.
- **Delete Preset** to remove a saved preset.
- **Import** to bring in a preset file and load it.
- **Export** to share a preset file.

## Additional Note: Device Hiding / HidHide Alternative

Instead of using the **Silence** function, which disables all other controller devices on your system, you can also use **HidHide** as a solid alternative to hide specific devices from the game.

**HidHide:**
https://github.com/nefarius/HidHide

In my case, I personally have to disable or hide my **vJoy** device, which I use for other games. From my testing, I can only get **4 devices working reliably at one time** in-game. If I add a 5th device, such as an Xbox controller, things may still partially work, but I noticed that I lose the ability to adjust the **FFB settings** in the **Advanced Controls** section.

If you are familiar with tools like **vJoy** and **SimHub**, you can also map multiple devices together into a single vJoy device. This can help reduce the number of devices the game sees while still allowing you to combine inputs from different hardware.

Just make sure you keep your **wheelbase separate** and do not combine it into vJoy, so it does not interfere with force feedback.

**Recommended setup:**
**Wheelbase + vJoy = More Flexibility**


## Important Disclaimer

Use this application at your own risk.

Back up your game files before using this tool. Horizon SimTool includes backup and restore features, but you are responsible for verifying your own backups before installing generated files.

This application can modify files in your game installation folder. It can also disable controller-class devices system-wide while Silence mode is active. If you select the wrong device, use an incorrect game folder, overwrite files, restore the wrong backup, or run into a system-specific issue, your game configuration, controller behavior, Windows device state, or other local files may be affected.

I am not responsible for anything that happens from using this application. This includes, but is not limited to, broken game files, lost settings, disabled devices, game crashes, Windows issues, hardware behavior changes, data loss, account issues, bans, lost time, or any other damage or inconvenience.

This project is not affiliated with, endorsed by, sponsored by, or supported by Microsoft, Xbox, Playground Games, Turn 10 Studios, Forza, Steam, Valve, or any hardware manufacturer.

Forza, Xbox, Microsoft, Steam, and all other names, trademarks, and brands belong to their respective owners.

This software is provided as-is with no warranty of any kind.

