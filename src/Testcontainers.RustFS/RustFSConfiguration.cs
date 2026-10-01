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
