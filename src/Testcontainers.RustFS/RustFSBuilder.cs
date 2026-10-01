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
    /// <exception cref="ArgumentException"><paramref name="accessKey" /> is null or empty.</exception>
    public RustFSBuilder WithAccessKey(string accessKey)
    {
        if (string.IsNullOrEmpty(accessKey))
        {
            throw new ArgumentException("The RustFS access key must not be null or empty.", nameof(accessKey));
        }

        return Merge(DockerResourceConfiguration, new RustFSConfiguration(accessKey: accessKey))
            .WithEnvironment("RUSTFS_ACCESS_KEY", accessKey);
    }

    /// <summary>Sets the RustFS secret key.</summary>
    /// <param name="secretKey">The secret key.</param>
    /// <returns>A configured instance of <see cref="RustFSBuilder" />.</returns>
    /// <exception cref="ArgumentException"><paramref name="secretKey" /> is null or empty.</exception>
    public RustFSBuilder WithSecretKey(string secretKey)
    {
        if (string.IsNullOrEmpty(secretKey))
        {
            throw new ArgumentException("The RustFS secret key must not be null or empty.", nameof(secretKey));
        }

        return Merge(DockerResourceConfiguration, new RustFSConfiguration(secretKey: secretKey))
            .WithEnvironment("RUSTFS_SECRET_KEY", secretKey);
    }

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
