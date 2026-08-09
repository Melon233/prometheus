# PromeArchTrial authoritative battle server

This .NET 8 process runs one global `AuthoritativeBattleWorld` at exactly 30 Hz. Every session owns only transport state and an identity; character state, command queues, combat resolution, and Tick events live in the single server-authoritative world.

## Generate and validate configuration

The server never reads Unity `ScriptableObject` assets and does not use a `rootId`. Character `1001` is selected directly from the server projection of `Configs/Xlsx/CharacterConfig.xlsx`.

Run `generate-config.bat` after the workbook or Luban schema changes. It uses the pinned Luban tool from the sibling Prometheus checkout and writes:

- C# readers to `Generated/Config`.
- Server-group cs-bin tables to `ConfigData/Luban`.
- Only `c,s` gameplay fields; client-only prefab and Spine fields are not emitted.

`Tables` automatically executes `ResolveRef()`. `BattleServerConfiguration.Load` then calls the shared `CharacterLubanConfigAdapter.Compile(tables, 1001)`, and startup fails before listening if a table is missing, a ref is invalid, or the compiled runtime constraints are inconsistent.

## Build and run

```bat
generate-config.bat
dotnet build BattleServer.csproj
run-smoke-test.bat
run-server.bat
```

Use `--config-dir <absolute-or-relative-directory>` to load an explicit server cs-bin directory. Without that option the built process reads `ConfigData/Luban` copied beside the executable.

## Protocol and world ownership

- Protocol v5 validates the protocol number and immutable runtime-config hash during `ClientHello`.
- Dynamic registration adds the entity at the world's current Tick and `ServerWelcome` carries `playerId`, `entityId`, Character `1001`, that exact Tick, and the complete initial rollback state.
- After `ServerWelcome`, the client predicts and uploads four neutral lead commands, then keeps its local predicted timeline at least four Ticks ahead so ordinary latency does not make every next-Tick input late.
- `ClientInput` is converted to a pure `CharacterCommand` and submitted directly into the world's bounded Tick queue; late and duplicate retransmissions are idempotent diagnostics, while malformed or excessively future commands remain protocol errors.
- Only `BattleServerHost` advances the global world through one `PeriodicTimer` loop.
- Each 30 Hz result sends the player's complete state, reconciliation Tick, and every event involving that entity.
- A single writer per session prevents TCP-frame interleaving. Pong control frames are prioritized over the replaceable latest state, while unsent events are bounded, deduplicated by `worldTick + ordinal`, merged into later snapshots, and removed only after a successful TCP write.
- Disconnect removes the entity and all unconsumed commands from the global world.

## Smoke-test coverage

`run-smoke-test.bat` fails on any unmet invariant and covers:

- Protocol v5 round trips for handshake, complete input prediction state, complete authoritative state, events, Ping, and Pong.
- Server Luban tables, resolved scalar/list refs, and repeated adapter content hash.
- Authoritative restore plus replay of every unacknowledged predicted command.
- Walk, run, sprint, jump, forward/back dodge, four-hit normal combo, held heavy attack, skill, and ultimate.
- The shared world behavior probe and a second two-entity hit scenario driven by the Luban-compiled Character configuration.
- Reliable event accumulation across state replacement, including duplicate publication, a concurrent newer snapshot, and write-success acknowledgement.
- A real localhost server connection with a delay longer than one Tick, four-Tick input lead, idempotent late/duplicate retransmission, immediate Pong, Welcome Tick alignment, full snapshots, and measured snapshot cadence close to 30 Hz.

Generated configuration code and data are build artifacts and remain ignored by Git; regenerate them from the reviewed workbook and schema instead of editing them manually.
