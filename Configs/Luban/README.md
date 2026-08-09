# PromeArchTrial Luban configuration

This directory defines the character configuration schema for the predicted Unity client and authoritative C# server. It uses the pinned Luban `v4.10.2` executable in `Tools/Luban/v4.10.2`; no global Luban or .NET tool installation is consulted.

## Authoring model

`../Xlsx/CharacterConfig.xlsx` is the only data workbook. Its sheets are `BattleRule`, `CharacterProperty`, `Locomotion`, `Dodge`, `Action`, `ActionSet`, `Character`, `AnimationClip`, and `CharacterPresentation`. Schema and table registration live in `Defines/gameplay.xml`, so the workbook does not need Luban's `__tables__`, `__beans__`, or `__enums__` sheets.

Shared gameplay fields explicitly use group `c,s`. Unity-only Spine and prefab fields explicitly use group `c`. Every relationship is declared in the XML type with `#ref=...`; collection relationships place `#ref` on the list element type, so Luban validates every ID during generation.

`Character` composes normal tables through refs and is selected by its own character ID. There is no aggregate `rootId` configuration object or root-ID handshake contract.

## Commands

- Run `validate.bat` to verify the pinned tool, parse the XML schema, load both group projections, and fail on invalid data or refs. Before the workbook exists, it intentionally returns exit code `20` after the schema-only pass instead of claiming data validation succeeded.
- Run `generate-client.bat` to write client C# readers under `Assets/PromeArchTrial/Generated/LubanClient` and binary tables under `Assets/StreamingAssets/PromeArchTrial/Config`. Keeping generated client code outside `Game/GameNative` prevents the server project's shared-source glob from compiling client-only `Tables` and presentation fields.
- Run `generate-server.bat` to write server C# readers under `Generated/Config` and binary tables under `ConfigData/Luban` in the sibling `battle-server` checkout. Set `PROMETHEUS_BATTLE_SERVER_ROOT` when the server is stored elsewhere.

Generated client and server readers are reproducible build outputs. The Unity client projection is kept under `Assets/PromeArchTrial/Generated/LubanClient` so Unity can compile and review the exact generated API, while the server projection remains local to the server checkout; both projections must be regenerated after schema changes. Binary table data is not hand-edited. The `cs-bin` runtime required by generated readers is vendored separately at `Assets/PromeArchTrial/Core/CoreNative/LubanRuntime` from the official Luban examples repository.

## Deterministic units

Gameplay timing is authored in integer simulation ticks. Distances, speeds, acceleration, and gravity use milli-units, and multipliers and probabilities use permille. Runtime Setup code converts those values to the project's fixed-point representation once, avoiding client/server drift from independently integrated floating-point configuration values.

The Luban adapter converts `BattleRule.land_recovery_ticks` into the runtime `Land` action, uses `attack_buffer_ticks` for the rollback-safe one-slot light-attack buffer, and converts each `Dodge` row into distinct runtime `DodgeForward` and `DodgeBackward` actions. The ordered `ActionSet.normal_attack_ids` entries map to runtime `Attack1` through `Attack4`; each `moving_attack_ids` row must carry identical gameplay values and exists only to select a different client animation for the same combo index. The scalar refs select regular skill, held special or heavy attack, and ultimate rows. Every `Action` phase, motion interval, displacement, hit range, resource cost, and confirmed-hit gain maps directly into `CharacterActionRuntimeConfig` after milli-unit fixed-point conversion. The Yefa baseline uses `combo_reset_ticks=60`, `special_hold_ticks=15`, and `attack_buffer_ticks=6` at 30 Hz.
