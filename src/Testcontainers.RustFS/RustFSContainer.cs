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
