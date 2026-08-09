# Google.Protobuf runtime

`Google.Protobuf.dll` is the `netstandard2.0` runtime assembly from NuGet package `Google.Protobuf` version `3.35.1`.

- Project: https://github.com/protocolbuffers/protobuf
- Package: https://www.nuget.org/packages/Google.Protobuf/3.35.1
- License: BSD-3-Clause

The assembly is checked into the Unity project so Editor, Mono and IL2CPP builds use the same Protobuf runtime version without requiring a Unity-specific NuGet package manager. The C# server references the same exact package version from its project file.
