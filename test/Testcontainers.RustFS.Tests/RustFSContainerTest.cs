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
    public void EmptyAccessKeyIsRejected(string? accessKey)
    {
        var builder = new RustFSBuilder("rustfs/rustfs:1.0.0");

        Assert.Throws<ArgumentException>(() => builder.WithAccessKey(accessKey!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptySecretKeyIsRejected(string? secretKey)
    {
        var builder = new RustFSBuilder("rustfs/rustfs:1.0.0");

        Assert.Throws<ArgumentException>(() => builder.WithSecretKey(secretKey!));
    }
}

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
