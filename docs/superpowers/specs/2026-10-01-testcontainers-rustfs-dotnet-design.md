# Testcontainers.RustFS (.NET) — Design

**Date:** 2026-10-01
**Status:** Draft, pending review

## Goal

Ship `MbUtils.Testcontainers.RustFS`, an unofficial Testcontainers .NET module that starts a
[RustFS](https://github.com/rustfs/rustfs) S3-compatible server in a Docker container for
integration tests. It mirrors the shape of upstream `Testcontainers.Minio`
(builder / container / configuration) and the behaviour of the existing Python module
`testcontainers-rustfs` (`/Users/bence/repos/github.com/mb-dot-dev/testcontainers-rustfs`).

Non-goals: multi-node clusters, TLS, bucket pre-creation, an S3 client dependency, a multi-target
matrix, symbol packages.

## Naming

| Thing | Value |
|---|---|
| NuGet package ID | `MbUtils.Testcontainers.RustFS` |
| Assembly name / root namespace | `Testcontainers.RustFS` |
| Repo | `Testcontainers.RustFS` |

The package ID differs from the namespace on purpose: nuget.org likely reserves the
`Testcontainers.*` ID prefix for the official org, so the package is published under `MbUtils.`.
Set `<PackageId>`, `<AssemblyName>` and `<RootNamespace>` explicitly in the csproj.

Known risk, accepted: types are named like upstream's (`Testcontainers.RustFS.RustFSBuilder`). If
upstream ships its own RustFS module, a project referencing both gets type clashes.

## Verified RustFS behaviour

Taken from the Python module's verified findings (image `rustfs/rustfs:1.0.0`). These should be
re-confirmed by the integration tests.

| Behaviour | Finding |
|---|---|
| S3 health | `GET :9000/health` → 200, no auth; a sound readiness gate |
| Console health | `GET :9001/rustfs/console/health` → 200; different port and path |
| Startup command | None needed; the entrypoint initialises `/data` and `/logs` |
| `RUSTFS_ADDRESS` | Optional, defaults to `:9000` |
| Unauthenticated `GET /` | 403, so unusable as a health check |
| Bucket names | Must be 3–63 characters |

## Public API

```csharp
namespace Testcontainers.RustFS;

public sealed class RustFSBuilder
    : ContainerBuilder<RustFSBuilder, RustFSContainer, RustFSConfiguration>
{
    public const string RustFSImage = "rustfs/rustfs:1.0.0";
    public const ushort RustFSPort = 9000;
    public const ushort RustFSConsolePort = 9001;
    public const string DefaultAccessKey = "rustfsadmin";
    public const string DefaultSecretKey = "rustfsadmin";

    public RustFSBuilder();                                  // uses RustFSImage
    public RustFSBuilder WithAccessKey(string accessKey);
    public RustFSBuilder WithSecretKey(string secretKey);
    public RustFSBuilder WithConsole(bool enabled = true);   // off by default
    public override RustFSContainer Build();
}

public sealed class RustFSContainer : DockerContainer
{
    public string AccessKey { get; }
    public string SecretKey { get; }
    public string GetConnectionString();   // http://{host}:{mappedPort}
    public string GetConsoleAddress();     // throws InvalidOperationException if console disabled
}
```

### Decisions

- **Mirrors upstream structure** so `new RustFSBuilder().Build()` feels familiar, including a
  `RustFSConfiguration : ContainerConfiguration` carrying access key, secret key and console flag.
- **`WithAccessKey`/`WithSecretKey`**, not Minio's `WithUsername`/`WithPassword`: RustFS and S3
  call them keys, as does the Python module.
- **Pinned image tag** `rustfs/rustfs:1.0.0`, not `latest`; consumers can override via the
  builder's image constructor/`WithImage`.
- **Default credentials** are RustFS's own `rustfsadmin`/`rustfsadmin`.
- **Console is opt-in**: fewer published ports and faster startup by default.
- **Container-side ports are constants**, not configurable. This departs from the Python module
  (which has `port`/`console_port`); Minio's builder does not expose them either. Easy to add later.
- **No S3 client dependency.** There is no `GetClient()`. Users build an `AmazonS3Client` from
  `GetConnectionString()`, `AccessKey` and `SecretKey`; the README shows how. `AWSSDK.S3` is a
  test-project dependency only.

### Container configuration

Always:

- expose 9000
- `RUSTFS_ACCESS_KEY` = access key
- `RUSTFS_SECRET_KEY` = secret key
- `RUSTFS_ADDRESS` = `:9000`
- no command override

When the console is enabled, additionally:

- expose 9001
- `RUSTFS_CONSOLE_ENABLE` = `true`
- `RUSTFS_CONSOLE_ADDRESS` = `:9001`

### Readiness

HTTP wait strategy: `GET /health` on port 9000 expecting 200. With the console enabled, add a
second check: `GET /rustfs/console/health` on port 9001 expecting 200. Both must pass, so
`GetConsoleAddress()` never returns a URL that is not yet serving.

## Repo layout

```
Testcontainers.RustFS.slnx
Directory.Build.props            # net10.0, nullable, implicit usings, warnings as errors
Directory.Packages.props         # central package management
src/Testcontainers.RustFS/
  RustFSBuilder.cs
  RustFSContainer.cs
  RustFSConfiguration.cs
  Testcontainers.RustFS.csproj
test/Testcontainers.RustFS.Tests/
  RustFSContainerTest.cs
  Testcontainers.RustFS.Tests.csproj
README.md  LICENSE  .gitignore  sonar-project.properties
.github/dependabot.yml
.github/workflows/main.yaml
.github/workflows/pack_and_publish.yml
```

Folders are `src/` and `test/` (singular), per the user's convention.

## Packaging

- Target framework: `net10.0` only.
- Sole runtime dependency: `Testcontainers` (a version floor, not a pin).
- Metadata: MIT license, README packed in, description states the package is unofficial.
- No symbol package (`IncludeSymbols`/`SymbolPackageFormat` unset) and no SourceLink.
- Version comes from the git tag at release time via `-p:PackageVersion`.

## Tests

xunit against real Docker, no mocks. Test project depends on `AWSSDK.S3`.

1. **Round-trip**: `CreateBucket`, `PutObject`, `GetObject`, `ListObjectsV2`, with a valid 3–63
   character bucket name.
2. **Custom credentials**: non-default access/secret key authenticate.
3. **Console enabled**: starts (proving the composite wait) and `GetConsoleAddress()` serves 200.
4. **Console disabled**: `GetConsoleAddress()` throws `InvalidOperationException`. The flag is
   checked before any port lookup, so the test builds the container with `Build()` and never
   starts it.
5. **Connection string shape**: `http://{host}:{port}` against a running container.

Tests 1 and 5 share a class fixture with the default container. Tests 2 and 3 each need their
own container. That is three container starts for the suite.

## Error handling

| Condition | Behaviour |
|---|---|
| `GetConsoleAddress()` with console disabled | `InvalidOperationException` telling the caller to use `WithConsole()` |
| Container fails to become healthy | Propagated from the wait strategy |
| Docker unavailable | Propagated from Testcontainers |

## CI and release

- **`dependabot.yml`**: copied from `molnarbence/MbUtils.Extensions`. Two ecosystems, `nuget` and
  `github-actions`, weekly, `directory: "/"`, 10 open PRs max, one grouped PR per ecosystem.
- **`main.yaml`**: `dotnet build` and `dotnet test` with coverage on `ubuntu-latest` (Docker
  present), then a SonarQube job in the same shape as the Python repo (SHA-pinned actions,
  quality-gate wait). `sonar-project.properties` points sources at `src/` and tests at `test/`.
- **`pack_and_publish.yml`**: modelled on `MbUtils.Extensions`'s workflow of the same name.
  - Trigger: tag push `v*`.
  - Runs in the `mcr.microsoft.com/dotnet/sdk:10.0` container; permissions `contents: read`,
    `id-token: write`.
  - Strips the `v` prefix from the tag, then
    `dotnet pack -c Release -o ./packages -p:PackageVersion=$VERSION`.
  - `NuGet/login` exchanges the OIDC token for a short-lived API key using `secrets.NUGET_USER`.
  - `dotnet nuget push ./packages/**/*.nupkg -s https://api.nuget.org/v3/index.json --api-key ...
    --no-symbols`.
  - Deviation from the reference: `checkout` and `NuGet/login` are pinned to commit SHAs (with a
    version comment), as in the Python repo. Dependabot keeps the pins current.

Prerequisites outside the repo: a SonarCloud project and `SONAR_TOKEN` secret; a `NUGET_USER`
secret and a trusted-publishing policy on nuget.org for this repo and workflow.

## README

Covers: the unofficial disclaimer, install (`dotnet add package MbUtils.Testcontainers.RustFS`),
a worked round-trip example using `AWSSDK.S3`, the console option, the builder options, and a note
that the pinned image is overridable.

## Deliverables

- `src/Testcontainers.RustFS/*`: builder, container, configuration, csproj
- `test/Testcontainers.RustFS.Tests/*`: five integration tests
- `Testcontainers.RustFS.slnx`, `Directory.Build.props`, `Directory.Packages.props`
- `.gitignore` (exists), `sonar-project.properties`
- `.github/dependabot.yml`, `.github/workflows/main.yaml`, `.github/workflows/pack_and_publish.yml`
- `README.md`
