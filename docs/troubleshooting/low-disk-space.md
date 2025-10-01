# Fixing "There is not enough space on the disk" build errors

The .NET MAUI workloads that target Android and Windows generate thousands of
intermediate files (images, AAB archives, APK zip entries, etc.) inside the
`bin/` and `obj/` directories. When the drive that hosts the repository runs out
of free space the Android asset packaging tools fail with a cascade of errors
similar to the following:

```
failed to write res/drawable-mdpi-v4/googleg_standard_color_18.png to archive: IO error
Could not write lines to file "obj\Debug\net9.0-android\designtime\build.props".
Unable to copy file "...\Microsoft.Windows.SDK.NET.dll". There is not enough space on the disk.
```

Follow the steps below to reclaim space and unblock your build.

## 1. Free several gigabytes quickly by deleting build outputs

1. Close Visual Studio, VS Code, Rider or any other IDE that might keep
   `bin/` / `obj/` folders locked.
2. Open a terminal at the solution root and run the cleanup script bundled with
   this repository:

   ```powershell
   pwsh ./scripts/clear-build-storage.ps1
   ```

   On macOS or Linux you can run the Bash equivalent:

   ```bash
   ./scripts/clear-build-storage.sh
   ```

   Both scripts remove every `bin/` and `obj/` directory below the repository
   and print the amount of reclaimed space. They are safe to run at any time
   because the build system will recreate the folders on the next build.

## 2. Optional: clear caches when additional space is needed

If the build still fails or the drive remains nearly full, clear the global
NuGet cache and unused workloads. **These steps take longer** but can recover a
few more gigabytes when the cache contains many packages.

```powershell
pwsh ./scripts/clear-build-storage.ps1 -IncludeNuGetCache -IncludeWorkloads
```

On macOS/Linux pass environment variables instead of switches:

```bash
INCLUDE_NUGET_CACHE=1 INCLUDE_WORKLOADS=1 ./scripts/clear-build-storage.sh
```

## 3. Verify free space before rebuilding

Check that the drive now has several gigabytes available. On Windows you can
use File Explorer or run `Get-PSDrive C` in PowerShell. On macOS/Linux use
`df -h`. Once the free space is confirmed, re-run your `dotnet build` or IDE
build command.

If space is still constrained, consider moving the repository to a larger disk
or pointing `DOTNET_WORKLOAD_CACHE_ROOT` to a drive with more capacity before
reinstalling workloads.
