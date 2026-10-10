# Quick Prompt 📌

**A lightweight, offline prompt manager for Windows.** Store reusable prompts, copy their full text with one click, pin your favorites, reorder them, and pin the window above other apps.

[فارسی — راهنمای فارسی](README.fa.md)

## Features
- Create, edit, search, and delete prompts.
- Click any title or preview to copy its full Unicode text to your clipboard.
- Pin important prompts above the others. Rearrange items with the up/down buttons.
- Use the window 📌 button to toggle always-on-top mode.
- Taskbar window and system-tray icon, with optional Windows auto-start.
- Offline operation, no account, cloud sync, analytics, or advertisements.
- Saves locally and uses built-in Windows .NET Framework libraries only.

## Download and use

1. Open [Releases](../../releases/latest) and get **QuickPrompt-v1.0.0-win.zip**.
2. Extract the ZIP file. Keep **QuickPrompt.exe** and **QuickPrompt.ico** in the same folder.
3. Run **QuickPrompt.exe** and choose **+ پرامپت جدید** (New Prompt).
4. Save a title and prompt body. Click a saved prompt to copy it, then paste it into another application.

The program also runs in the notification area near the clock. Right-click that icon to open, add a prompt, or completely exit the app. Closing the window minimizes it; fully exit through the tray menu.

You may create a Windows Startup shortcut to the executable. Optional command-line argument **--startup** opens it minimized.

## Privacy

**No personal prompts are included** in the repository or downloadable release. Your own prompts are stored only on your computer, at:

    %APPDATA%\QuickPrompt\prompts.json

The always-on-top preference is stored in:

    %APPDATA%\QuickPrompt\keep-on-top.txt

Quick Prompt does not send your prompts to a server. To preserve your collection when reinstalling Windows or migrating, make a copy of your prompts file.

## Building from source

Requires Windows with .NET Framework 4.x development compiler (or compatible Visual Studio build tools). Run the included **build.ps1** using PowerShell. It compiles the open-source C# Windows Forms program without NuGet dependencies. The executable is created under **dist/**.

The release is **unsigned**: Windows Defender SmartScreen or an enterprise security policy may warn or block execution. Do not disable system security. If your environment disallows unsigned apps, consider reviewing the code and using a compliant build or signing process.

The original app's prompt operations, pin/unpin, ordering, and persistent data behavior were tested on Windows. The sanitized public build compiled successfully, but independent runtime testing of that binary on Windows 10/11 is still needed: the build machine's application-control policy blocked its execution from the build folder.

## Contributing

Issues and pull requests are welcome. If reporting a bug, please describe your Windows version and reproduction steps **without attaching your personal prompts.json**.

## License

MIT — see [LICENSE](LICENSE).
