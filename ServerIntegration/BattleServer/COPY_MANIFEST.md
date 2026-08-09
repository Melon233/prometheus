# Battle server copy manifest

Copy the following staged files from `D:\Unity Projects\Prometheus\ServerIntegration\BattleServer` to the same relative paths under `D:\Unity Projects\battle-server`:

- `.gitignore`
- `BattleServer.csproj`
- `NuGet.Config`
- `Program.cs`
- `README.md`
- `generate-config.bat`
- `generate-protobuf.bat`
- `run-server.bat`
- `run-smoke-test.bat`
- `Configuration/BattleServerConfiguration.cs`
- `Diagnostics/SmokeTestRunner.cs`
- `Networking/BattleClientSession.cs`
- `Networking/ReliableSnapshotOutbox.cs`
- `Networking/BattleServerHost.cs`

Do not copy staged `bin` or `obj` directories. After copying the text files, run `D:\Unity Projects\battle-server\generate-config.bat` to create `Generated/Config/**/*.cs` and `ConfigData/Luban/*.bytes` from the reviewed Prometheus workbook, then run `run-smoke-test.bat`.
