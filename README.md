# Horizon Wheel Wizard

A guided Windows app for setting up your sim racing wheel in Forza Horizon. Walks you through detecting your wheel, finding your game install, mapping all 26 inputs, and installing the patched files — in one wizard flow.

## Based On

**Horizon Wheel Wizard is built on top of [Horizon SimTool](https://github.com/Dxniel02/Horizon-SimTool) by [Dxniel02](https://github.com/Dxniel02).**

Horizon SimTool provided the full backend engine: device enumeration, XML/INI patching, zip generation, game file installation, backup/restore, and the original WinForms UI. This project adds a WebView2-embedded Tailwind CSS wizard UI on top of that foundation.

All credit for the core tool goes to Dxniel02 and the original Horizon SimTool project.

---

## What's New in Horizon Wheel Wizard

### Guided Setup Wizard
On first launch, the app walks you through four steps:

1. **Detect Wheel** — auto-scans connected controllers, pick your wheelbase from the list
2. **Find Game** — auto-detects Forza Horizon installs (Steam + Xbox Game Pass); browse fallback if needed
3. **Map Controls** — launches the input capture wizard, press each input when prompted (26 bindings)
4. **Backup & Install** — one click creates a backup, generates patched ZIPs, and installs to the game folder

### Quick Remap
After setup, a Quick Remap grid shows all 26 mapped bindings. Click any cell to remap that input without re-running the full wizard.

### Modern UI
Dark motorsports theme built with Tailwind CSS, served via Microsoft WebView2 embedded inside the WinForms app — no browser, no server, no port.

---

## Requirements

- Windows 10 (1803+) or Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- WebView2 Runtime (pre-installed on Windows 11 / Microsoft Edge; [download here](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) if missing)

---

## How It Works

The wizard writes your wheelbase VID/PID into the game's XML controller profile, packages it into the correct uncompressed ZIP format the game expects, creates a `HST-BACKUP` of the originals, and installs the patched files into your game's `media` folder.

Advanced options (FFB tuning, device roles for pedals/shifter/handbrake, manual path overrides) are available in the sidebar after setup.

---

## Credits and Acknowledgements

**Original tool: [Horizon SimTool](https://github.com/Dxniel02/Horizon-SimTool) by [Dxniel02](https://github.com/Dxniel02)**
— Device enumeration, XML/INI patching, zip generation, game file installation, backup/restore, and the core mapping engine all come from this project. Thank you.

Community research that helped make the original tool possible:

- Reddit user `swagikuro` and contributors for:
  [How to setup FFB on Forza Horizon WITHOUT Emuwheel](https://www.reddit.com/r/Simagic/comments/1toeiim/how_to_setup_ffb_on_forza_horizon_without/)

- Reddit user `problemchild52` for the Moza R3 ESX mapping guide:
  [Moza R3 ESX Forza Horizon 6 Complete Mapping](https://www.reddit.com/r/ForzaHorizon6/comments/1tixvvw/moza_r3_esx_forza_horizon_6_complete_mapping_90/)

- Reddit user `Cptkrush` for Fanatec CSL DD troubleshooting:
  [For folks on PC (Steam) with issues getting their Fanatec CSL DD to work](https://www.reddit.com/r/ForzaHorizon/comments/1tf2dj0/for_folks_on_pc_steam_with_issues_getting_their/)

- Forza Support Team for official wheel input documentation:
  [Forza Horizon 6 on Wheel: Wheel Input on Steam](https://support.forza.net/hc/en-us/articles/51642975681427-Forza-Horizon-6-on-Wheel-Wheel-Input-on-Steam)

---

## AI Disclosure

This application was built with AI assistance using [Claude](https://claude.ai) by Anthropic (Claude Sonnet 4.6 via Claude Code). The original Horizon SimTool was built with OpenAI Codex.

Review the code before relying on it. This tool modifies game files and can disable controller devices system-wide.

---

## Disclaimer

Use at your own risk. Back up your game files before using. Horizon Wheel Wizard includes backup and restore features, but you are responsible for verifying your own backups.

This project is not affiliated with, endorsed by, or supported by Microsoft, Xbox, Playground Games, Turn 10 Studios, Forza, Steam, Valve, or any hardware manufacturer.

Forza, Xbox, Microsoft, Steam, and all other names and trademarks belong to their respective owners.

This software is provided as-is with no warranty of any kind.
