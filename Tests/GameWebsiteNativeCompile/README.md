# Native-reference compile without opening Unity

From the Unity project root:

```powershell
dotnet build Tests/GameWebsiteNativeCompile/GameWebsiteNativeCompile.csproj --artifacts-path Temp/GameWebsiteNativeCheck --verbosity quiet
dotnet Temp/GameWebsiteNativeCheck/bin/GameWebsiteNativeCompile/debug/GameWebsiteNativeCompile.dll
```

Reuses the installed Editor's saved compiler response files and adds newly created
runtime/Editor source files. All generated assemblies go under ignored `Temp`,
never Unity's live assemblies. When an Android response file exists, that target
is compiled too. This does not import or save scenes, create an APK, install on a
device, or establish real provider/device acceptance.
