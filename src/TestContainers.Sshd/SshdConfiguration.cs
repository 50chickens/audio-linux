namespace Testcontainers.Sshd;

/// <inheritdoc cref="ContainerConfiguration" />
[PublicAPI]
public sealed class SshdConfiguration : ContainerConfiguration
{
    public SshdConfiguration(string username = null, string password = null)
    {
        Username = username;
        Password = password;
    }

    public SshdConfiguration(IResourceConfiguration<Docker.DotNet.Models.CreateContainerParameters> resourceConfiguration)
        : base(resourceConfiguration)
    {
    }

    public SshdConfiguration(IContainerConfiguration resourceConfiguration)
        : base(resourceConfiguration)
    {
    }

    public SshdConfiguration(SshdConfiguration resourceConfiguration)
        : this(new SshdConfiguration(), resourceConfiguration)
    {
    }

    public SshdConfiguration(SshdConfiguration oldValue, SshdConfiguration newValue)
        : base(oldValue, newValue)
    {
        Username = BuildConfiguration.Combine(oldValue.Username, newValue.Username);
        Password = BuildConfiguration.Combine(oldValue.Password, newValue.Password);
    }

    public string Username { get; }

    public string Password { get; }
}
