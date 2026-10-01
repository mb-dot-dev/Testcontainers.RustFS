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
