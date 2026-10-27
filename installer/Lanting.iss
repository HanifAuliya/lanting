; Installer Lanting (Inno Setup 6). Dibuat otomatis oleh GitHub Actions; lihat .github/workflows.
; Build lokal: dotnet publish dulu ke out\Lanting, lalu: iscc installer\Lanting.iss /DAppVersion=0.1.0

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\Lanting"
#endif

[Setup]
AppId={{8C4D0E4B-6A3F-4C55-9B8A-2F5E7C1D9A11}
AppName=Lanting
AppVersion={#AppVersion}
AppVerName=Lanting {#AppVersion}
AppPublisher=Hanif Auliya
AppPublisherURL=https://github.com/HanifAuliya/lanting
AppSupportURL=https://github.com/HanifAuliya/lanting/issues
; Dipasang per pengguna: tidak perlu hak admin, sama seperti Spotify atau VS Code versi user.
PrivilegesRequired=lowest
DefaultDirName={autopf}\Lanting
DisableProgramGroupPage=yes
OutputDir=..\out\installer
OutputBaseFilename=Lanting-Setup-{#AppVersion}
SetupIconFile=..\src\Lanting.App\Assets\lanting.ico
UninstallDisplayIcon={app}\Lanting.exe
UninstallDisplayName=Lanting
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
; Tutup Lanting yang sedang berjalan sebelum file ditimpa (nama mutex sama dengan di App.xaml.cs).
AppMutex=Local\Lanting.SingleInstance
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Jalankan Lanting setiap kali Windows menyala"
Name: "desktopicon"; Description: "Buat ikon di desktop"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Lanting"; Filename: "{app}\Lanting.exe"
Name: "{autodesktop}\Lanting"; Filename: "{app}\Lanting.exe"; Tasks: desktopicon

[Registry]
; Kunci yang sama dengan "Mulai bersama Windows" di Pengaturan (Shell\AutoStart.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Lanting"; ValueData: """{app}\Lanting.exe"""; Tasks: autostart

[Run]
Filename: "{app}\Lanting.exe"; Description: "Jalankan Lanting sekarang"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im Lanting.exe /f"; Flags: runhidden; RunOnceId: "StopLanting"

[UninstallDelete]
; Log dan pengaturan.
Type: filesandordirs; Name: "{localappdata}\Lanting"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // "Mulai bersama Windows" bisa juga dinyalakan dari Pengaturan, jadi selalu dihapus saat uninstall.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Lanting');
end;
