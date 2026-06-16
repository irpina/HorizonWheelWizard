<div align="center">

<br />

# Horizon Wheel Wizard

**Guided wheel setup for Forza Horizon — detect, map, install.**

[![Release](https://img.shields.io/badge/release-v1.1-ff6600?style=flat-square)](https://github.com/irpina/HorizonWheelWizard/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows-blue?style=flat-square)](https://github.com/irpina/HorizonWheelWizard/releases/latest)
[![.NET](https://img.shields.io/badge/.NET-8-512bd4?style=flat-square)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

[**Download**](https://github.com/irpina/HorizonWheelWizard/releases/latest) · [**Website**](https://irpina.github.io/HorizonWheelWizard/) · [**Report a bug**](https://github.com/irpina/HorizonWheelWizard/issues)

<br />

</div>

---

## What it does

Horizon Wheel Wizard is a Windows wizard app that connects your sim racing wheel to Forza Horizon in four steps — no XML editing, no manual file copying.

```
Step 1  →  Detect your wheel (auto-scans connected controllers)
Step 2  →  Find Forza (auto-detects Steam + Xbox Game Pass installs)
Step 3  →  Map controls (press each input when prompted — 26 bindings)
Step 4  →  Generate & Install (backup → patch → install in one click)
```

After setup, a **Quick Remap** grid lets you rebind any single input without re-running the full wizard.

---

## Supported wheels

Moza · Fanatec · Simagic · Thrustmaster · Logitech G · SimuCube · Cammus · Asetek · and more — any HID device Windows recognises as a controller works.

---

## Requirements

| | |
|---|---|
| OS | Windows 10 (1803+) or Windows 11 |
| Runtime | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) |
| Browser engine | WebView2 — ships with Windows 11 and Edge; [download here](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) if missing |
| Game | Forza Horizon (Steam or Xbox Game Pass) |

---

## How it works

The wizard reads your wheel's VID/PID, writes it into the game's XML controller profile, packages it into the uncompressed ZIP format Forza expects, backs up the originals to `HST-BACKUP`, and installs the patched files into your game's `media` folder.

Advanced options (FFB tuning, device roles for pedals/shifter/handbrake, manual path overrides) are available in the **Advanced** panel after setup.

---

## Built on

> **Horizon Wheel Wizard is built on [Horizon SimTool](https://github.com/Dxniel02/Horizon-SimTool) by [Dxniel02](https://github.com/Dxniel02).**

The entire backend — device enumeration, XML/INI patching, zip generation, game file installation, backup/restore, and the core mapping engine — comes from Horizon SimTool. This project adds a WebView2 wizard UI on top of that foundation.

All credit for the core tool goes to Dxniel02.

---

## Community research

These posts made the original tool possible:

- **swagikuro** — [How to setup FFB on Forza Horizon WITHOUT Emuwheel](https://www.reddit.com/r/Simagic/comments/1toeiim/how_to_setup_ffb_on_forza_horizon_without/)
- **problemchild52** — [Moza R3 ESX Forza Horizon 6 Complete Mapping](https://www.reddit.com/r/ForzaHorizon6/comments/1tixvvw/moza_r3_esx_forza_horizon_6_complete_mapping_90/)
- **Cptkrush** — [Fanatec CSL DD issues on PC/Steam](https://www.reddit.com/r/ForzaHorizon/comments/1tf2dj0/for_folks_on_pc_steam_with_issues_getting_their/)
- **Forza Support** — [Wheel Input on Steam](https://support.forza.net/hc/en-us/articles/51642975681427-Forza-Horizon-6-on-Wheel-Wheel-Input-on-Steam)

---

## AI disclosure

Built with [Claude](https://claude.ai) (Anthropic, Claude Sonnet 4.6 via Claude Code). The original Horizon SimTool was built with OpenAI Codex.

Review the code before relying on it. This tool modifies game files — always verify your backups.

---

## Disclaimer

Not affiliated with Microsoft, Xbox, Playground Games, Turn 10 Studios, Forza, Steam, Valve, or any hardware manufacturer. Forza, Xbox, and all other trademarks belong to their respective owners. Use at your own risk.
