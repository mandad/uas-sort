. 'C:\Users\damia\AppData\Local\Temp\uas-sort-spike-winui\env.ps1'
& "$T\dotnet-install.ps1" -Version 11.0.100-rc.1.26425.128 -InstallDir "$T\dotnet" -NoPath -Architecture x64
& "$T\dotnet\dotnet.exe" --info
