# Building

## Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

## Single-file self-contained executable
Produces one standalone Windows x64 `.exe` that runs **without** the .NET runtime
installed on the target machine:

```sh
dotnet publish "Leauge Auto Accept/Leauge Auto Accept.csproj" -p:PublishProfile=win-x64-singlefile
```

Output: `Leauge Auto Accept/bin/Release/net9.0/win-x64/publish/Leauge Auto Accept.exe`

## Regular build (framework-dependent, for development)
```sh
dotnet build "Leauge Auto Accept/Leauge Auto Accept.csproj" -c Debug
```
