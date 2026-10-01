# Testcontainers.RustFS (.NET) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `MbUtils.Testcontainers.RustFS`, an unofficial Testcontainers .NET module that runs a RustFS S3-compatible server for integration tests.

**Architecture:** Upstream-style trio: `RustFSBuilder : ContainerBuilder<...>`, `RustFSContainer : DockerContainer`, `RustFSConfiguration : ContainerConfiguration`. Credentials and the console flag live in the immutable configuration and are merged by the builder. Readiness is an HTTP wait on `/health`, plus a second wait on the console health endpoint when the console is enabled.

**Tech Stack:** .NET 10 (`net10.0` only), `Testcontainers` 4.15.0, xunit v3, `AWSSDK.S3` (tests only), GitHub Actions, NuGet trusted publishing.

**Spec:** `docs/superpowers/specs/2026-10-01-testcontainers-rustfs-dotnet-design.md`

**Execution setup:** work in a worktree per the user's convention, not inside the repo:
`git worktree add -b feat/initial-module ../Testcontainers.RustFS.worktrees/feat-initial-module design-spec`
(run from `/Users/bence/repos/github.com/mb-dot-dev/Testcontainers.RustFS`). Untracked local config does not follow into a worktree; there is none here.

## Global Constraints

- NuGet package ID `MbUtils.Testcontainers.RustFS`; assembly name and root namespace `Testcontainers.RustFS`.
- Target framework `net10.0` only. Sole runtime dependency: `Testcontainers` (version via central package management).
- Folders are `src/` and `test/` (singular).
- Default image `rustfs/rustfs:1.0.0`; default credentials `rustfsadmin`/`rustfsadmin`; container ports 9000 (S3) and 9001 (console) are constants.
- Env vars: `RUSTFS_ACCESS_KEY`, `RUSTFS_SECRET_KEY`, `RUSTFS_ADDRESS=:9000`; console adds `RUSTFS_CONSOLE_ENABLE=true`, `RUSTFS_CONSOLE_ADDRESS=:9001`. No command override.
- Readiness: `GET /health` on 9000 (200); with console also `GET /rustfs/console/health` on 9001 (200).
- No `AWSSDK.S3` (or any S3 client) dependency in the library; tests only.
- No symbol package, no SourceLink: do not set `IncludeSymbols` or `SymbolPackageFormat`; push with `--no-symbols`.
- Console is opt-in; `GetConsoleAddress()` throws `InvalidOperationException` when disabled, checked before any port lookup.
- Tests run against real Docker, no mocks. Bucket names must be 3-63 characters.
- GitHub Actions are pinned to commit SHAs with a version comment.
- Unofficial: the package description and README say so.

## Spec amendments made while planning

These follow from inspecting upstream; the user should confirm them when reviewing this plan.

1. **`WithConsole()` takes no parameter** (spec said `WithConsole(bool enabled = true)`). The builder is additive: it appends a port binding, env vars and a wait strategy, so `WithConsole(false)` after `WithConsole()` could not undo them. A parameterless method is honest about that.
2. **Argument validation uses plain `ArgumentException`**, not upstream's `Guard.Argument(...)`, which is internal to the Testcontainers assembly.
3. **No `ConnectionStringProvider`.** Upstream modules now register one (`ConnectionMode.Host/Container`). It is not in the spec, so it is left out (YAGNI) and can be added later without breaking the API.
4. **Image constructors:** `RustFSBuilder()` (default image), `RustFSBuilder(string image)` and `RustFSBuilder(IImage image)`. Upstream marks its parameterless constructor `[Obsolete]`; this module keeps it non-obsolete because the spec's pinned-default design is deliberate.

## Review Focus

Failure modes the spec implies but a happy-path test would miss, most likely first. Each has a test in the task named in brackets.

1. A wrong secret key must be rejected by the server, proving custom credentials actually reach RustFS and are not silently ignored. [Task 1]
2. A secret key containing `/`, `+` and `=` (like the AWS example keys) must authenticate. [Task 1]
3. `WithConsole()` called after `WithAccessKey`/`WithSecretKey` must keep the custom credentials: the builder's merge must not drop earlier configuration. [Task 2]
4. Empty or null access/secret key must fail at `Build()` with `ArgumentException`, not as an opaque container failure. [Task 1]
5. `GetConsoleAddress()` on a never-started, console-disabled container must throw `InvalidOperationException`, not a port-lookup error. [Task 2]
6. The string-image constructor must work (`new RustFSBuilder("rustfs/rustfs:1.0.0")`), since that is upstream's recommended entry point. [Task 1]

---

### Task 1: Solution scaffold and S3 container (credentials, connection string)

**Files:**
- Create: `Testcontainers.RustFS.slnx`, `Directory.Build.props`, `Directory.Packages.props`
- Create: `src/Testcontainers.RustFS/Testcontainers.RustFS.csproj`
- Create: `src/Testcontainers.RustFS/RustFSConfiguration.cs`, `RustFSBuilder.cs`, `RustFSContainer.cs`
- Create: `test/Testcontainers.RustFS.Tests/Testcontainers.RustFS.Tests.csproj`
- Create: `test/Testcontainers.RustFS.Tests/RustFSContainerTest.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (Task 2 extends these, so names and shapes are fixed):
  - `RustFSConfiguration(string? accessKey = null, string? secretKey = null, bool? consoleEnabled = null)` with `string? AccessKey`, `string? SecretKey`, `bool? ConsoleEnabled`, plus the five standard copy/merge constructors.
  - `RustFSBuilder` constants `RustFSImage`, `RustFSPort` (`ushort` 9000), `RustFSConsolePort` (`ushort` 9001), `DefaultAccessKey`, `DefaultSecretKey`; constructors `()`, `(string)`, `(IImage)`; methods `WithAccessKey(string)`, `WithSecretKey(string)`, `Build()`.
  - `RustFSContainer(RustFSConfiguration)` with `string AccessKey`, `string SecretKey`, `string GetConnectionString()`.

- [ ] **Step 1: Create the solution and shared MSBuild files**

```bash
dotnet new sln --format slnx -n Testcontainers.RustFS
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Testcontainers" Version="4.15.0" />
    <PackageVersion Include="AWSSDK.S3" Version="4.0.102.2" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
    <PackageVersion Include="coverlet.collector" Version="10.0.1" />
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the library and test project files, add to the solution**

`src/Testcontainers.RustFS/Testcontainers.RustFS.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>Testcontainers.RustFS</AssemblyName>
    <RootNamespace>Testcontainers.RustFS</RootNamespace>
    <PackageId>MbUtils.Testcontainers.RustFS</PackageId>
    <Authors>Bence Molnár</Authors>
    <Description>Unofficial Testcontainers module for RustFS, an S3-compatible object store.</Description>
    <PackageTags>testcontainers;rustfs;s3;docker;testing</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageProjectUrl>https://github.com/mb-dot-dev/Testcontainers.RustFS</PackageProjectUrl>
    <RepositoryUrl>https://github.com/mb-dot-dev/Testcontainers.RustFS</RepositoryUrl>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Testcontainers" />
  </ItemGroup>
  <ItemGroup>
    <None Include="../../README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

`test/Testcontainers.RustFS.Tests/Testcontainers.RustFS.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsPublishable>false</IsPublishable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="AWSSDK.S3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Testcontainers.RustFS/Testcontainers.RustFS.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln Testcontainers.RustFS.slnx add src/Testcontainers.RustFS/Testcontainers.RustFS.csproj test/Testcontainers.RustFS.Tests/Testcontainers.RustFS.Tests.csproj
```

The library csproj packs `../../README.md`; create a placeholder `README.md` change in Task 3, the existing one-liner is enough until then.

- [ ] **Step 3: Write the failing tests**

`test/Testcontainers.RustFS.Tests/RustFSContainerTest.cs`:

```csharp
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace Testcontainers.RustFS;

public sealed class RustFSDefaultFixture : IAsyncLifetime
{
    public RustFSContainer Container { get; } = new RustFSBuilder("rustfs/rustfs:1.0.0").Build();

    public ValueTask InitializeAsync() => new(Container.StartAsync(TestContext.Current.CancellationToken));

    public ValueTask DisposeAsync() => Container.DisposeAsync();
}

public sealed class RustFSContainerTest(RustFSDefaultFixture fixture) : IClassFixture<RustFSDefaultFixture>
{
    private static AmazonS3Client CreateClient(string serviceUrl, string accessKey, string secretKey)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            AuthenticationRegion = "us-east-1",
            ForcePathStyle = true,
        };
        return new AmazonS3Client(accessKey, secretKey, config);
    }

    [Fact]
    public async Task PutObjectThenGetObjectRoundTrips()
    {
        var container = fixture.Container;
        using var client = CreateClient(container.GetConnectionString(), container.AccessKey, container.SecretKey);
        var bucket = $"bucket-{Guid.NewGuid():N}";
        var ct = TestContext.Current.CancellationToken;

        await client.PutBucketAsync(bucket, ct);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = "hello.txt",
            ContentBody = "Hello RustFS",
            UseChunkEncoding = false,
        }, ct);

        using var response = await client.GetObjectAsync(bucket, "hello.txt", ct);
        using var reader = new StreamReader(response.ResponseStream);
        Assert.Equal("Hello RustFS", await reader.ReadToEndAsync(ct));

        var list = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket }, ct);
        Assert.Contains(list.S3Objects, o => o.Key == "hello.txt");
    }

    [Fact]
    public void DefaultCredentialsAreRustfsadmin()
    {
        Assert.Equal(RustFSBuilder.DefaultAccessKey, fixture.Container.AccessKey);
        Assert.Equal("rustfsadmin", fixture.Container.AccessKey);
        Assert.Equal("rustfsadmin", fixture.Container.SecretKey);
    }

    [Fact]
    public void ConnectionStringIsHttpUrlWithMappedPort()
    {
        var uri = new Uri(fixture.Container.GetConnectionString());

        Assert.Equal(Uri.UriSchemeHttp, uri.Scheme);
        Assert.Equal(fixture.Container.GetMappedPublicPort(RustFSBuilder.RustFSPort), uri.Port);
    }
}

public sealed class RustFSCustomCredentialsTest : IAsyncLifetime
{
    // Contains '/', '+' and '=' to prove awkward secret characters survive env-var passing and signing.
    private const string AccessKey = "AKIAIOSFODNN7EXAMPLE";
    private const string SecretKey = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY=";

    private readonly RustFSContainer _container = new RustFSBuilder("rustfs/rustfs:1.0.0")
        .WithAccessKey(AccessKey)
        .WithSecretKey(SecretKey)
        .Build();

    public ValueTask InitializeAsync() => new(_container.StartAsync(TestContext.Current.CancellationToken));

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    [Fact]
    public async Task CustomCredentialsAuthenticate()
    {
        using var client = new AmazonS3Client(AccessKey, SecretKey, new AmazonS3Config
        {
            ServiceURL = _container.GetConnectionString(),
            AuthenticationRegion = "us-east-1",
            ForcePathStyle = true,
        });

        var buckets = await client.ListBucketsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, buckets.HttpStatusCode);
        Assert.Equal(AccessKey, _container.AccessKey);
        Assert.Equal(SecretKey, _container.SecretKey);
    }

    [Fact]
    public async Task WrongSecretKeyIsRejected()
    {
        using var client = new AmazonS3Client(AccessKey, "not-the-secret", new AmazonS3Config
        {
            ServiceURL = _container.GetConnectionString(),
            AuthenticationRegion = "us-east-1",
            ForcePathStyle = true,
        });

        await Assert.ThrowsAsync<AmazonS3Exception>(
            () => client.ListBucketsAsync(TestContext.Current.CancellationToken));
    }
}

public sealed class RustFSBuilderValidationTest
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyAccessKeyFailsAtBuild(string? accessKey)
    {
        var builder = new RustFSBuilder("rustfs/rustfs:1.0.0").WithAccessKey(accessKey!);

        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptySecretKeyFailsAtBuild(string? secretKey)
    {
        var builder = new RustFSBuilder("rustfs/rustfs:1.0.0").WithSecretKey(secretKey!);

        Assert.Throws<ArgumentException>(() => builder.Build());
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test test/Testcontainers.RustFS.Tests`
Expected: build FAIL, `RustFSBuilder`/`RustFSContainer` do not exist (CS0246).

- [ ] **Step 5: Implement the configuration**

`src/Testcontainers.RustFS/RustFSConfiguration.cs`:

```csharp
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;

namespace Testcontainers.RustFS;

/// <inheritdoc cref="ContainerConfiguration" />
public sealed class RustFSConfiguration : ContainerConfiguration
{
    /// <summary>Initializes a new instance of the <see cref="RustFSConfiguration" /> class.</summary>
    /// <param name="accessKey">The RustFS access key.</param>
    /// <param name="secretKey">The RustFS secret key.</param>
    /// <param name="consoleEnabled">Whether the web console is enabled.</param>
    public RustFSConfiguration(string? accessKey = null, string? secretKey = null, bool? consoleEnabled = null)
    {
        AccessKey = accessKey;
        SecretKey = secretKey;
        ConsoleEnabled = consoleEnabled;
    }

    /// <summary>Initializes a new instance of the <see cref="RustFSConfiguration" /> class.</summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    public RustFSConfiguration(IResourceConfiguration<CreateContainerParameters> resourceConfiguration)
        : base(resourceConfiguration)
    {
        // Passes the configuration upwards to the base implementations to create an updated immutable copy.
    }

    /// <summary>Initializes a new instance of the <see cref="RustFSConfiguration" /> class.</summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    public RustFSConfiguration(IContainerConfiguration resourceConfiguration)
        : base(resourceConfiguration)
    {
        // Passes the configuration upwards to the base implementations to create an updated immutable copy.
    }

    /// <summary>Initializes a new instance of the <see cref="RustFSConfiguration" /> class.</summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    public RustFSConfiguration(RustFSConfiguration resourceConfiguration)
        : this(new RustFSConfiguration(), resourceConfiguration)
    {
        // Passes the configuration upwards to the base implementations to create an updated immutable copy.
    }

    /// <summary>Initializes a new instance of the <see cref="RustFSConfiguration" /> class.</summary>
    /// <param name="oldValue">The old Docker resource configuration.</param>
    /// <param name="newValue">The new Docker resource configuration.</param>
    public RustFSConfiguration(RustFSConfiguration oldValue, RustFSConfiguration newValue)
        : base(oldValue, newValue)
    {
        AccessKey = newValue.AccessKey ?? oldValue.AccessKey;
        SecretKey = newValue.SecretKey ?? oldValue.SecretKey;
        ConsoleEnabled = newValue.ConsoleEnabled ?? oldValue.ConsoleEnabled;
    }

    /// <summary>Gets the RustFS access key.</summary>
    public string? AccessKey { get; }

    /// <summary>Gets the RustFS secret key.</summary>
    public string? SecretKey { get; }

    /// <summary>Gets a value indicating whether the web console is enabled.</summary>
    public bool? ConsoleEnabled { get; }
}
```

Note: `?? ` is used instead of `BuildConfiguration.Combine` because `Combine` is built for reference types and `ConsoleEnabled` is a `bool?`; the null-coalescing form is equivalent and uniform across the three fields. An empty-string key is a non-null value and so wins the merge, which is what lets `Validate()` reject it in Step 6.

- [ ] **Step 6: Implement the builder**

`src/Testcontainers.RustFS/RustFSBuilder.cs`:

```csharp
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Images;

namespace Testcontainers.RustFS;

/// <inheritdoc cref="ContainerBuilder{TBuilderEntity, TContainerEntity, TConfigurationEntity}" />
public sealed class RustFSBuilder : ContainerBuilder<RustFSBuilder, RustFSContainer, RustFSConfiguration>
{
    public const string RustFSImage = "rustfs/rustfs:1.0.0";

    public const ushort RustFSPort = 9000;

    public const ushort RustFSConsolePort = 9001;

    public const string DefaultAccessKey = "rustfsadmin";

    public const string DefaultSecretKey = "rustfsadmin";

    /// <summary>Initializes a new instance of the <see cref="RustFSBuilder" /> class using <see cref="RustFSImage" />.</summary>
    public RustFSBuilder()
        : this(RustFSImage)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RustFSBuilder" /> class.</summary>
    /// <param name="image">The full Docker image name, including repository and tag.</param>
    public RustFSBuilder(string image)
        : this(new DockerImage(image))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RustFSBuilder" /> class.</summary>
    /// <param name="image">An <see cref="IImage" /> specifying the Docker image to use.</param>
    public RustFSBuilder(IImage image)
        : this(new RustFSConfiguration())
    {
        DockerResourceConfiguration = Init().WithImage(image).DockerResourceConfiguration;
    }

    private RustFSBuilder(RustFSConfiguration resourceConfiguration)
        : base(resourceConfiguration)
    {
        DockerResourceConfiguration = resourceConfiguration;
    }

    /// <inheritdoc />
    protected override RustFSConfiguration DockerResourceConfiguration { get; }

    /// <summary>Sets the RustFS access key.</summary>
    /// <param name="accessKey">The access key.</param>
    /// <returns>A configured instance of <see cref="RustFSBuilder" />.</returns>
    public RustFSBuilder WithAccessKey(string accessKey)
    {
        return Merge(DockerResourceConfiguration, new RustFSConfiguration(accessKey: accessKey))
            .WithEnvironment("RUSTFS_ACCESS_KEY", accessKey);
    }

    /// <summary>Sets the RustFS secret key.</summary>
    /// <param name="secretKey">The secret key.</param>
    /// <returns>A configured instance of <see cref="RustFSBuilder" />.</returns>
    public RustFSBuilder WithSecretKey(string secretKey)
    {
        return Merge(DockerResourceConfiguration, new RustFSConfiguration(secretKey: secretKey))
            .WithEnvironment("RUSTFS_SECRET_KEY", secretKey);
    }

    /// <inheritdoc />
    public override RustFSContainer Build()
    {
        Validate();
        return new RustFSContainer(DockerResourceConfiguration);
    }

    /// <inheritdoc />
    protected override RustFSBuilder Init()
    {
        return base.Init()
            .WithPortBinding(RustFSPort, true)
            .WithEnvironment("RUSTFS_ADDRESS", $":{RustFSPort}")
            .WithAccessKey(DefaultAccessKey)
            .WithSecretKey(DefaultSecretKey)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
                request.ForPath("/health").ForPort(RustFSPort)));
    }

    /// <inheritdoc />
    protected override void Validate()
    {
        base.Validate();

        if (string.IsNullOrEmpty(DockerResourceConfiguration.AccessKey))
        {
            throw new ArgumentException("The RustFS access key must not be null or empty.", nameof(DockerResourceConfiguration.AccessKey));
        }

        if (string.IsNullOrEmpty(DockerResourceConfiguration.SecretKey))
        {
            throw new ArgumentException("The RustFS secret key must not be null or empty.", nameof(DockerResourceConfiguration.SecretKey));
        }
    }

    /// <inheritdoc />
    protected override RustFSBuilder Clone(IResourceConfiguration<CreateContainerParameters> resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new RustFSConfiguration(resourceConfiguration));
    }

    /// <inheritdoc />
    protected override RustFSBuilder Clone(IContainerConfiguration resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new RustFSConfiguration(resourceConfiguration));
    }

    /// <inheritdoc />
    protected override RustFSBuilder Merge(RustFSConfiguration oldValue, RustFSConfiguration newValue)
    {
        return new RustFSBuilder(new RustFSConfiguration(oldValue, newValue));
    }
}
```

If the build reports that `Docker.DotNet.Models` does not exist, Testcontainers 4.15 renamed the Docker client namespace: take the namespace from the `using` in upstream's `RedisBuilder.cs` at tag `4.15.0` (`gh api repos/testcontainers/testcontainers-dotnet/contents/src/Testcontainers.Redis/Usings.cs?ref=4.15.0 --jq .content | base64 -d`) and use that in both files.

- [ ] **Step 7: Implement the container**

`src/Testcontainers.RustFS/RustFSContainer.cs`:

```csharp
using DotNet.Testcontainers.Containers;

namespace Testcontainers.RustFS;

/// <inheritdoc cref="DockerContainer" />
public sealed class RustFSContainer : DockerContainer
{
    private readonly RustFSConfiguration _configuration;

    /// <summary>Initializes a new instance of the <see cref="RustFSContainer" /> class.</summary>
    /// <param name="configuration">The container configuration.</param>
    public RustFSContainer(RustFSConfiguration configuration)
        : base(configuration)
    {
        _configuration = configuration;
    }

    /// <summary>Gets the S3 access key.</summary>
    public string AccessKey => _configuration.AccessKey!;

    /// <summary>Gets the S3 secret key.</summary>
    public string SecretKey => _configuration.SecretKey!;

    /// <summary>Gets the S3 endpoint URL reachable from the host.</summary>
    /// <returns>The RustFS S3 endpoint URL.</returns>
    public string GetConnectionString()
    {
        return new UriBuilder(Uri.UriSchemeHttp, Hostname, GetMappedPublicPort(RustFSBuilder.RustFSPort)).ToString();
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test test/Testcontainers.RustFS.Tests`
Expected: PASS (all tests; the first run starts three containers, so allow about a minute). The `rustfs/rustfs:1.0.0` image is already present locally.

- [ ] **Step 9: Commit**

```bash
git add Testcontainers.RustFS.slnx Directory.Build.props Directory.Packages.props src test
git commit -m "feat: add RustFS container with credentials and connection string

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Opt-in web console

**Files:**
- Modify: `src/Testcontainers.RustFS/RustFSBuilder.cs`
- Modify: `src/Testcontainers.RustFS/RustFSContainer.cs`
- Modify: `test/Testcontainers.RustFS.Tests/RustFSContainerTest.cs`

**Interfaces:**
- Consumes: from Task 1, `RustFSConfiguration.ConsoleEnabled` (`bool?`), `RustFSBuilder.RustFSConsolePort`, `RustFSBuilder.WithAccessKey/WithSecretKey`, `Merge`.
- Produces: `RustFSBuilder WithConsole()`; `RustFSContainer.GetConsoleAddress()` returning `string`, throwing `InvalidOperationException` when the console is disabled.

- [ ] **Step 1: Write the failing tests**

Append to `test/Testcontainers.RustFS.Tests/RustFSContainerTest.cs`:

```csharp
public sealed class RustFSConsoleTest : IAsyncLifetime
{
    // Console is combined with custom credentials on purpose: WithConsole() must not drop earlier configuration.
    private readonly RustFSContainer _container = new RustFSBuilder("rustfs/rustfs:1.0.0")
        .WithAccessKey("console-access")
        .WithSecretKey("console-secret")
        .WithConsole()
        .Build();

    public ValueTask InitializeAsync() => new(_container.StartAsync(TestContext.Current.CancellationToken));

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    [Fact]
    public async Task ConsoleIsServedWhenEnabled()
    {
        using var http = new HttpClient();

        var response = await http.GetAsync(
            new Uri(new Uri(_container.GetConsoleAddress()), "rustfs/console/health"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CustomCredentialsSurviveWithConsole()
    {
        using var client = new AmazonS3Client("console-access", "console-secret", new AmazonS3Config
        {
            ServiceURL = _container.GetConnectionString(),
            AuthenticationRegion = "us-east-1",
            ForcePathStyle = true,
        });

        var buckets = await client.ListBucketsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, buckets.HttpStatusCode);
        Assert.Equal("console-access", _container.AccessKey);
    }
}

public sealed class RustFSConsoleDisabledTest
{
    [Fact]
    public void GetConsoleAddressThrowsWhenConsoleIsDisabled()
    {
        // Built but never started: the flag must be checked before any port lookup.
        var container = new RustFSBuilder("rustfs/rustfs:1.0.0").Build();

        var exception = Assert.Throws<InvalidOperationException>(() => container.GetConsoleAddress());

        Assert.Contains("WithConsole()", exception.Message);
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test test/Testcontainers.RustFS.Tests --filter "FullyQualifiedName~Console"`
Expected: build FAIL, `WithConsole` and `GetConsoleAddress` do not exist.

- [ ] **Step 3: Implement `WithConsole()`**

In `RustFSBuilder.cs`, add after `WithSecretKey`:

```csharp
    /// <summary>
    /// Enables the web console on <see cref="RustFSConsolePort" />. Opt-in, off by default.
    /// Additive: the console cannot be switched off again once enabled on a builder instance.
    /// </summary>
    /// <returns>A configured instance of <see cref="RustFSBuilder" />.</returns>
    public RustFSBuilder WithConsole()
    {
        return Merge(DockerResourceConfiguration, new RustFSConfiguration(consoleEnabled: true))
            .WithPortBinding(RustFSConsolePort, true)
            .WithEnvironment("RUSTFS_CONSOLE_ENABLE", "true")
            .WithEnvironment("RUSTFS_CONSOLE_ADDRESS", $":{RustFSConsolePort}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
                request.ForPath("/rustfs/console/health").ForPort(RustFSConsolePort)));
    }
```

`WithWaitStrategy` appends to the wait strategies already configured, so the S3 `/health` check from `Init()` stays and both must pass.

- [ ] **Step 4: Implement `GetConsoleAddress()`**

In `RustFSContainer.cs`, add after `GetConnectionString()`:

```csharp
    /// <summary>Gets the web console URL reachable from the host.</summary>
    /// <returns>The RustFS console URL.</returns>
    /// <exception cref="InvalidOperationException">The console was not enabled with <c>WithConsole()</c>.</exception>
    public string GetConsoleAddress()
    {
        if (_configuration.ConsoleEnabled != true)
        {
            throw new InvalidOperationException(
                "The RustFS console is disabled; enable it with new RustFSBuilder().WithConsole().");
        }

        return new UriBuilder(Uri.UriSchemeHttp, Hostname, GetMappedPublicPort(RustFSBuilder.RustFSConsolePort)).ToString();
    }
```

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test test/Testcontainers.RustFS.Tests`
Expected: PASS, all tests (four container starts in total: default fixture, custom credentials, console).

- [ ] **Step 6: Commit**

```bash
git add src test
git commit -m "feat: add opt-in web console

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: README, Sonar, Dependabot and workflows

**Files:**
- Modify: `README.md`
- Create: `sonar-project.properties`
- Create: `.github/dependabot.yml`
- Create: `.github/workflows/main.yaml`
- Create: `.github/workflows/pack_and_publish.yml`

**Interfaces:**
- Consumes: the public API from Tasks 1-2 (documented in the README).
- Produces: nothing code depends on.

- [ ] **Step 1: Write the README**

`README.md`:

````markdown
# Testcontainers.RustFS

Unofficial [Testcontainers for .NET](https://dotnet.testcontainers.org/) module for
[RustFS](https://github.com/rustfs/rustfs), an S3-compatible object store. Not affiliated with
or endorsed by the Testcontainers project.

## Install

```bash
dotnet add package MbUtils.Testcontainers.RustFS
```

The package contains no S3 client; use whichever you like (examples use `AWSSDK.S3`).

## Usage

```csharp
using Amazon.S3;
using Testcontainers.RustFS;

await using var rustfs = new RustFSBuilder().Build();
await rustfs.StartAsync();

using var client = new AmazonS3Client(rustfs.AccessKey, rustfs.SecretKey, new AmazonS3Config
{
    ServiceURL = rustfs.GetConnectionString(),
    AuthenticationRegion = "us-east-1",
    ForcePathStyle = true,
});

await client.PutBucketAsync("testbucket");
```

Bucket names must be 3-63 characters.

## Options

| Builder member | Purpose | Default |
|---|---|---|
| `new RustFSBuilder()` | Use the pinned default image | `rustfs/rustfs:1.0.0` |
| `new RustFSBuilder("repo:tag")` | Use another image | |
| `WithAccessKey(string)` | S3 access key | `rustfsadmin` |
| `WithSecretKey(string)` | S3 secret key | `rustfsadmin` |
| `WithConsole()` | Enable the web console (opt-in) | off |

| Container member | Purpose |
|---|---|
| `AccessKey`, `SecretKey` | The credentials in use |
| `GetConnectionString()` | S3 endpoint URL, `http://host:port/` |
| `GetConsoleAddress()` | Console URL; throws `InvalidOperationException` unless `WithConsole()` was used |

The default image is pinned rather than `latest` so a test run does not change underneath you;
override it with the image constructor.

## License

MIT
````

- [ ] **Step 2: Add Sonar and Dependabot config**

`sonar-project.properties`:

```properties
sonar.projectKey=mb-dot-dev_Testcontainers.RustFS
sonar.organization=mb-dot-dev

sonar.sources=src
sonar.tests=test
sonar.cs.opencover.reportsPaths=**/TestResults/**/coverage.opencover.xml
sonar.coverage.exclusions=test/**

# S5332 flags the http:// endpoint URLs this module builds. A RustFS test container serves
# plain HTTP on an ephemeral localhost port, so there is no TLS endpoint to address.
sonar.issue.ignore.multicriteria=e1
sonar.issue.ignore.multicriteria.e1.ruleKey=csharpsquid:S5332
sonar.issue.ignore.multicriteria.e1.resourceKey=src/**
```

`.github/dependabot.yml`:

```yaml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 10
    groups:
      nuget-dependencies:
        patterns:
          - "*"

  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 10
    groups:
      github-actions:
        patterns:
          - "*"
```

- [ ] **Step 3: Add the build-and-analyse workflow**

`.github/workflows/main.yaml` (the .NET Sonar scanner wraps the build, unlike the Python repo's generic scan action):

```yaml
name: Main
run-name: Build & test
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
  workflow_dispatch:

concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

jobs:
  sonarqube:
    name: Build, test & SonarQube
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7.0.0
        with:
          fetch-depth: 0  # Shallow clones should be disabled for a better relevancy of analysis

      - uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        with:
          dotnet-version: 10.0.x

      - name: Install SonarScanner
        run: dotnet tool install --global dotnet-sonarscanner

      - name: Begin analysis
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: >
          dotnet sonarscanner begin
          /k:"mb-dot-dev_Testcontainers.RustFS"
          /o:"mb-dot-dev"
          /d:sonar.token="$SONAR_TOKEN"
          /d:sonar.cs.opencover.reportsPaths="**/TestResults/**/coverage.opencover.xml"
          /d:sonar.qualitygate.wait=true

      - name: Build
        run: dotnet build -c Release

      - name: Test
        run: >
          dotnet test -c Release --no-build
          --collect:"XPlat Code Coverage"
          -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover

      - name: End analysis
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
```

- [ ] **Step 4: Add the pack-and-publish workflow**

`.github/workflows/pack_and_publish.yml`:

```yaml
name: Pack and publish

on:
  push:
    tags:
      - "v*"

jobs:
  pack_and_publish:
    runs-on: ubuntu-latest
    container:
      image: mcr.microsoft.com/dotnet/sdk:10.0
    permissions:
      contents: read
      id-token: write

    steps:
      - uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7.0.0

      - name: Pack
        run: |
          VERSION="${GITHUB_REF##*/}"
          VERSION="${VERSION#v}"
          dotnet pack src/Testcontainers.RustFS -c Release -o ./packages -p:PackageVersion="$VERSION"

      - name: NuGet login (OIDC -> temp API key)
        uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841 # v1.2.0
        id: login
        with:
          user: ${{ secrets.NUGET_USER }}

      - name: Push to NuGet
        run: >
          dotnet nuget push ./packages/*.nupkg
          -s https://api.nuget.org/v3/index.json
          --api-key ${{ steps.login.outputs.NUGET_API_KEY }}
          --no-symbols
```

- [ ] **Step 5: Verify pack output and workflow syntax**

Run: `dotnet pack src/Testcontainers.RustFS -c Release -o ./packages -p:PackageVersion=0.0.1-local && ls packages && unzip -l packages/*.nupkg | grep -E "README|\.dll" && rm -rf packages`
Expected: `MbUtils.Testcontainers.RustFS.0.0.1-local.nupkg` exists and contains `lib/net10.0/Testcontainers.RustFS.dll` and `README.md`; no `.snupkg`.

Run: `python3 -c "import yaml,sys; [yaml.safe_load(open(f)) for f in ['.github/dependabot.yml','.github/workflows/main.yaml','.github/workflows/pack_and_publish.yml']]; print('yaml ok')"`
Expected: `yaml ok`.

The workflows and Sonar analysis cannot be run locally; they are first exercised by CI and the first tag.

- [ ] **Step 6: Commit**

```bash
git add README.md sonar-project.properties .github
git commit -m "chore: add README, Sonar config, Dependabot and CI/release workflows

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

## Prerequisites outside the repo

Not part of any task; the user must do these before CI and the first release work:

- A SonarCloud project with key `mb-dot-dev_Testcontainers.RustFS` (organization `mb-dot-dev`) and a `SONAR_TOKEN` repository secret. If the project key differs, change it in `main.yaml` and `sonar-project.properties`.
- A `NUGET_USER` repository secret and a trusted-publishing policy on nuget.org for this repo and the `pack_and_publish.yml` workflow. The package ID `MbUtils.Testcontainers.RustFS` must be creatable by that nuget.org account.
