# PIM Tray

A tiny Windows tray app that lets you activate one or more Microsoft Entra ID **Privileged Identity Management (PIM)** roles with a single click - reason and duration included - without ever opening a browser.

Built for IT pros and admins who activate PIM roles many times a day and are tired of the portal click-marathon.

> Author: [Thomas Marcussen](https://thomasmarcussen.com) - Microsoft MVP, Technology Architect
> Blog: <https://blog.thomasmarcussen.com>

---

## Why

The Entra portal works, but activating a role takes 5 to 8 clicks, plus a tab switch, plus typing a reason every single time. PIM Tray collapses that into:

1. Left-click the tray icon
2. Tick the role(s) you want
3. Type one reason, pick one duration
4. **Activate**

That's it - one dialog, one round-trip, one balloon notification when each role is live (or pending approval).

---

## Features

- **System-tray native** Win32 app. Sits quietly in the notification area; opens on left-click, full menu on right-click.
- **Real interactive sign-in** via MSAL - handles MFA, Conditional Access and the rest of Entra's auth surface the proper way. No password is ever typed into the app.
- **Multi-account / multi-tenant**: connect as many tenants as you like (e.g. Prod + Sandbox), each with its own app registration and sign-in state. Switch between them from the tray or the main window without signing out.
- **One-click cross-tenant activation**: if the same role (by name) is eligible in two or more connected tenants, PIM Tray offers a single "Activate in Prod + Sandbox" entry - one reason, one duration, activated in every matching tenant.
- **Auto-discovery of eligible roles**: after sign-in the app calls Microsoft Graph and lists every role the user is eligible for, including scope (Directory, or a named Administrative Unit).
- **Multi-select batch activation**: tick several roles - from one tenant or several - fill in one reason + one duration, activate them all in one go. Partial-success is reported clearly so you can retry only the failed ones.
- **Reason + duration enforced**: the activation form requires a justification (good hygiene + matches most PIM policies). Duration is a configurable dropdown - default 1/2/4/8 hours, override in config.
- **Token cache persisted** per tenant to `%LOCALAPPDATA%\PIMTray\msal_cache_<connection-id>.bin` (DPAPI-encrypted), so you only sign in once per account.
- **Per-user config** stored at `%APPDATA%\PIMTray\appsettings.json`. Created on first run if missing; older single-account config files are upgraded automatically the first time PIM Tray runs.
- **Code-signed** EV certificate (DigiCert). The MSI installer is signed too.
- **MSI installer** with Start menu shortcut, ARP entry, major-upgrade support.

---

## Install

**Option A - MSI (recommended)**

1. Download `PIMTray.msi` from the [Releases](../../releases) page.
2. Double-click. Per-machine install to `C:\Program Files\PIM Tray\`.
3. Start menu -> **PIM Tray -> PIM Tray**.

**Option B - Build from source**

```powershell
git clone https://github.com/<you>/PIMTray.git
cd PIMTray
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The exe lands in `bin\Release\net9.0-windows\win-x64\publish\PIMTray.exe`.

**Requirements**

- Windows 10 / 11 (x64)
- .NET 9 Windows Desktop Runtime (target machine)
- An Entra ID app registration with the right delegated Graph permissions (see [Configuration](#configuration))
- The signed-in user has at least one **eligible** PIM role

---

## Configuration

On first run the app creates `%APPDATA%\PIMTray\appsettings.json` with a single "Default" account:

```json
{
  "Connections": [
    {
      "Id": "a1b2c3d4e5f6...",
      "Name": "Default",
      "TenantId": "common",
      "ClientId": "14d82eec-204b-4c2f-b7e8-296a70dab67e",
      "RedirectUri": "http://localhost"
    }
  ],
  "Pim": {
    "DefaultDurationHours": 1,
    "DurationOptionsHours": [ 1, 2, 4, 8 ]
  }
}
```

| Field                        | Description                                                                                                |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `Connections[].Id`           | Stable identifier generated automatically - don't edit; it keys the token cache file for that account.     |
| `Connections[].Name`         | Display name shown in menus, e.g. `Prod` or `Sandbox`.                                                      |
| `Connections[].TenantId`     | `common` for multi-tenant, or your tenant GUID for single-tenant.                                           |
| `Connections[].ClientId`     | Default is the public **Microsoft Graph PowerShell** app (`14d82eec-...`). Replace with your own app reg.   |
| `Connections[].RedirectUri`  | Must match the redirect URI configured on the app registration. `http://localhost` is the standard choice.  |
| `Pim.DefaultDurationHours`   | Pre-selected duration in the Activate dialog.                                                               |
| `Pim.DurationOptionsHours`   | The list of durations shown in the dropdown.                                                                |

Config files from earlier single-account versions of PIM Tray (a top-level `AzureAd` object) are upgraded automatically the first time the new version loads them - no manual migration needed.

### Multiple accounts / tenants (e.g. Prod + Sandbox)

Use **tray icon -> Accounts -> Manage accounts...** (or **Accounts -> Manage accounts...** in the main window's menu bar) to add, edit, or remove tenant connections without touching the JSON file directly. Each connection is a separate app registration + tenant pairing with its own sign-in state and its own encrypted token cache, so signing out of one never affects another.

Once you're signed in to two or more accounts, any eligible role whose **name matches exactly** across tenants (e.g. "Global Administrator" eligible in both Prod and Sandbox) gets a combined menu entry - "Global Administrator — Activate in Prod + Sandbox" - so one click, one reason, one duration activates it in every matching tenant. Roles that only exist in one tenant, or whose names differ, still show up individually and can be multi-selected together in the main window regardless of tenant.

### Required Graph permissions (delegated)

Grant these on **every** app registration you configure as a connection:

- `RoleEligibilitySchedule.Read.Directory`
- `RoleAssignmentSchedule.ReadWrite.Directory`
- `User.Read`

### Bring-your-own app registration (recommended for production)

1. Entra admin center -> **App registrations -> New registration**. Single-tenant is fine.
2. **Authentication -> Add a platform -> Mobile and desktop applications**, redirect URI `http://localhost`. Allow public client flows = **Yes**.
3. **API permissions** -> Microsoft Graph -> add the three delegated scopes above. **Grant admin consent**.
4. In PIM Tray, use **Accounts -> Manage accounts... -> Add...** and paste in the **Application (client) ID** and **Directory (tenant) ID**. Repeat per tenant (e.g. once for Prod, once for Sandbox).

After admin consent, end users never see a consent prompt.

---

## How it works

```
+----------------+    interactive   +-----------------------+
| PIMTray.exe    | ---------------> | Microsoft Identity    |
| (WinForms tray)| <----- token --- | (MSAL public client)  |
+----------------+                  +-----------------------+
        | (one ConnectionSession per configured tenant)
        | https://graph.microsoft.com/v1.0/roleManagement/directory/
        v
+-----------------------------------------------------------+
| GET  roleEligibilitySchedules?$filter=principalId eq ...  |
| POST roleAssignmentScheduleRequests {action: selfActivate}|
+-----------------------------------------------------------+
```

- **AuthService** (`Auth/AuthService.cs`) wraps MSAL's `PublicClientApplication` for a single tenant connection. Uses Win32 broker-less interactive sign-in (default browser) with the right scopes. Cache is DPAPI-encrypted on disk, keyed per connection, so subsequent launches go silent per account.
- **PimService** (`Pim/PimService.cs`) talks raw Graph REST for a single tenant. Two endpoints: list eligibilities, create activation requests.
- **ConnectionSession** (`Connections/ConnectionSession.cs`) pairs one `AuthService` + `PimService` + `HttpClient` per configured tenant, and guards against overlapping sign-in/refresh calls on the same account.
- **RoleGrouping** (`Pim/RoleGrouping.cs`) combines eligible roles from every signed-in connection and produces the "activate in multiple tenants" grouped menu entries.
- **TrayApplicationContext** + **MainForm** drive the UI and own the list of `ConnectionSession`s. Both the tray right-click menu and the main window stay in sync via events.

No background polling, no telemetry, no callbacks.

---

## Project layout

```
PIMTray/
├── Auth/                    MSAL wrapper (single tenant)
├── Connections/             ConnectionSession - one signed-in tenant's Auth+Pim+HttpClient
├── Pim/                     Graph PIM client + cross-tenant role grouping
├── UI/
│   ├── TrayApplicationContext.cs    NotifyIcon + context menu, owns all ConnectionSessions
│   ├── MainForm.cs                  Main window, role list, MenuStrip
│   ├── ActivateRoleForm.cs          Reason + duration dialog (single + multi + cross-tenant)
│   ├── ManageAccountsForm.cs        Add/edit/remove tenant connections
│   ├── ConnectionEditForm.cs        Single connection's Name/TenantId/ClientId/RedirectUri
│   └── AboutForm.cs                 About dialog
├── Resources/
│   ├── tray.ico             App icon
│   ├── PIMTray.rc           Win32 resource (version info + manifest + icon)
│   └── PIMTray.res          Compiled resource (gitignored - rebuild with rc.exe)
├── installer/
│   └── PIMTray.wxs          WiX 5 installer definition
├── Tests/                   xUnit tests for AppConfig, PimService, RoleGrouping
├── AppConfig.cs             appsettings.json loader (writes defaults, migrates legacy format)
├── AppIcon.cs               Loads tray.ico from embedded resource
├── Program.cs               Entry point + unhandled-exception handler
├── PIMTray.csproj
└── app.manifest             High-DPI / supportedOS manifest (embedded via .res)
```

---

## Build the installer

Requires [WiX Toolset v5](https://wixtoolset.org/) as a global .NET tool (one-time setup):

```powershell
dotnet tool install --global wix --version 5.0.2
```

Every release, from the repo root:

```powershell
# 1. Publish the exe the installer packages (installer/PIMTray.wxs points at this path).
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

# 2. Build the MSI. WiX resolves the .wxs's relative Source/Icon paths (..\Resources\...,
#    ..\bin\...) against the current directory, so run this from installer/, not the repo root.
cd installer
wix build -arch x64 -out PIMTray.msi PIMTray.wxs
cd ..
```

`installer\PIMTray.msi` is the file to attach to a GitHub Release. Bump `Version` in `installer/PIMTray.wxs` (and `PIMTray.csproj`) before each release - WiX's `MajorUpgrade` element uses it to let the MSI cleanly upgrade an existing install.

This repo doesn't sign its MSI (no code-signing certificate), so Windows SmartScreen may warn on first run - expected for an internally-shared tool. If you do have an EV cert:

```powershell
signtool sign /sha1 <thumbprint> /fd SHA256 `
  /tr http://timestamp.digicert.com /td SHA256 `
  installer\PIMTray.msi
```

---

## Roadmap / ideas

- "Open settings file" tray entry
- Group eligibilities (PIM for Groups)
- Azure Resource PIM (subscription / resource group / resource scopes)
- Approval-pending status polling
- Auto-start at logon checkbox in About / Settings
- Optional dark mode

PRs welcome.

---

## License

MIT - see [LICENSE](LICENSE).

---

## Credits

Built by [Thomas Marcussen](https://thomasmarcussen.com) - Microsoft MVP, Technology Architect.
Contact: <Thomas@ThomasMarcussen.com>
