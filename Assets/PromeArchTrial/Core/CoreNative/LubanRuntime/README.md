# Luban cs-bin runtime

These sources are vendored unchanged for the pinned Luban `v4.10.2` `cs-bin` generator used by this project. They provide the binary reader and small support types referenced by Luban-generated C# table classes; they are infrastructure only and do not contain generated business DTOs.

Source repository: `https://github.com/focus-creative-games/luban_examples`. Source path: `Projects/Csharp_DotNet_bin/LubanLib`. The source snapshot was downloaded from the official repository's `main` archive on 2026-08-08 and copied without local code changes. The retained source archive SHA-256 is documented below so this otherwise moving branch snapshot remains identifiable.

- Source archive SHA-256: `FB698BC200AC9CEAC9251E5E4C192C7FE807A6B5EACEDF41FFC6A2295596AA28`
- `BeanBase.cs`: `2B3FB576F2D2CB3B6C74C2FFD8C75C4F035AB42D857547B1A9C0D892D5C45201`
- `ByteBuf.cs`: `C784561036714B73A23834969A3C3426CE32F0AA15690E47AD3AC4C56FE0F017`
- `ITypeId.cs`: `EFFD56BE2EC82D7650B23172AD396341CF7248F7CA34DFEDA362F88A6C1BB477`
- `StringUtil.cs`: `513ED1B536CAC41CD1BBB0CFF42466F4F289255422B2E6149BE67097951A791D`

The upstream code and this vendored copy are distributed under the MIT License; see `LICENSE.txt`. Any future Luban upgrade must regenerate code with the candidate tool, copy the matching official runtime, update the hashes, and run both client and server validation before replacing this snapshot.
