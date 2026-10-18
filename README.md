# UnityPackageImporter

An experimental [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod for importing Unity packages (`.unitypackage`) directly into [Resonite](https://resonite.com/).

---

## 📖 Documentation

Full documentation, installation guides, and feature compatibility details are available on the documentation site:

👉 **[https://artbyo3.github.io/ResoniteUnityPackagesImporter/](https://artbyo3.github.io/ResoniteUnityPackagesImporter/)**

---

## 🛠️ Building from Source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Resonite installed with [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader)

### Commands
```powershell
# Build Release mod DLL
dotnet build UnityPackageImporter/UnityPackageImporter.csproj -c Release

# Run regression test suite
dotnet run --project Tests/Importer.RegressionTests.csproj -c Release
```

If Resonite is not installed in the default Steam location, set the `GAMEPATH` environment variable (e.g. `$env:GAMEPATH = "C:\Path\To\Resonite\"` or `export GAMEPATH="/path/to/Resonite/"`) or specify `-p:GamePath="<path-to-resonite>"`.

---

## 📄 License & Credits

Maintained by **Artbyo3**.
Built from the original foundation by [dfgHiatus](https://github.com/dfgHiatus/ResoniteUnityPackagesImporter) and its contributors.

Licensed under the [MIT License](LICENSE).
