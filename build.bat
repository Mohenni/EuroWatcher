@echo off
rem Builds the Windows version of EuroWatcher from the files in this folder.
rem Needs nothing extra: it uses the C# compiler that is part of every Windows 10/11 installation.
rem Result:  bin\EuroWatcher.exe       (the program, with the page and the draw data inside)
rem          EuroWatcherSetup.exe      (the installer, which contains the program)

setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo The .NET Framework 4 compiler was not found at %CSC%
    exit /b 1
)
if not exist bin mkdir bin

echo Building bin\EuroWatcher.exe ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 ^
    /out:bin\EuroWatcher.exe ^
    /win32icon:assets\EuroWatcher.ico /win32manifest:desktop\App\app.manifest ^
    /resource:index.html,web/index.html ^
    /resource:styles.css,web/styles.css ^
    /resource:app.js,web/app.js ^
    /resource:i18n.js,web/i18n.js ^
    /resource:data\draws.js,web/draws.js ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
    desktop\App\*.cs desktop\Shared\*.cs
if errorlevel 1 exit /b 1

echo Building EuroWatcherSetup.exe ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 ^
    /out:EuroWatcherSetup.exe ^
    /win32icon:assets\EuroWatcher.ico /win32manifest:desktop\Setup\setup.manifest ^
    /resource:assets\EuroWatcher.ico,EuroWatcher.ico ^
    /resource:bin\EuroWatcher.exe,EuroWatcher.Payload.exe ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll ^
    desktop\Setup\*.cs desktop\Shared\*.cs
if errorlevel 1 exit /b 1

echo Done.
endlocal
