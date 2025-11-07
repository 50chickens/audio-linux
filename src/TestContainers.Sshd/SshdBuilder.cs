namespace Testcontainers.Sshd;

/// <inheritdoc cref="ContainerBuilder{TBuilderEntity, TContainerEntity, TConfigurationEntity}" />
[PublicAPI]
public sealed class SshdBuilder : ContainerBuilder<SshdBuilder, SshdContainer, SshdConfiguration>
{
    public const string SshdImage = "linuxserver/openssh-server:latest";

    public const ushort SshdPort = 2222; // linuxserver image uses 2222 inside

    public SshdBuilder()
        : this(new SshdConfiguration())
    {
        DockerResourceConfiguration = Init().DockerResourceConfiguration;
    }

    private SshdBuilder(SshdConfiguration resourceConfiguration)
        : base(resourceConfiguration)
    {
        DockerResourceConfiguration = resourceConfiguration;
    }

    protected override SshdConfiguration DockerResourceConfiguration { get; }

    public override SshdContainer Build()
    {
        Validate();

        return new SshdContainer(DockerResourceConfiguration);
    }

    protected override SshdBuilder Init()
    {
        return base.Init()
            .WithImage(SshdImage)
            .WithPortBinding(SshdPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilInternalTcpPortIsAvailable(SshdPort)
                .UntilExternalTcpPortIsAvailable(SshdPort));
    }

    protected override SshdBuilder Clone(IResourceConfiguration<Docker.DotNet.Models.CreateContainerParameters> resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new SshdConfiguration(resourceConfiguration));
    }

    protected override SshdBuilder Clone(IContainerConfiguration resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new SshdConfiguration(resourceConfiguration));
    }

    protected override SshdBuilder Merge(SshdConfiguration oldValue, SshdConfiguration newValue)
    {
        return new SshdBuilder(new SshdConfiguration(oldValue, newValue));
    }

    public SshdBuilder WithUsername(string username)
    {
        var merged = Merge(DockerResourceConfiguration, new SshdConfiguration(username: username));

        // The linuxserver/openssh-server image already contains the 'root' user in /etc/passwd.
        // Setting USER_NAME=root causes the image init to try to create the user and fail.
        // Only export USER_NAME when the requested username is not 'root'.
        if (!string.Equals(username, "root", StringComparison.Ordinal))
        {
            merged = merged.WithEnvironment("USER_NAME", username);
        }

        return merged;
    }

// Removed WithPassword: password-based flow is not required when using key-only authentication.

    // WithSSHPrivateKeyFileFromEnv removed: prefer explicit in-memory WithPrivateKey or WithPrivateKeyFileCopied flows.

    /// <summary>
    /// Configure the container to accept the supplied private key for authentication by deriving
    /// the public key and installing it inside the container. The private key is not written to
    /// the host; it is intended only for client-side use.
    /// </summary>
    public SshdBuilder WithPrivateKey(string privateKeyPem, string containerPrivateKeyPath = "/root/.ssh/id_rsa", string containerPublicKeyPath = "/root/.ssh/authorized_keys")
    {
        _ = Guard.Argument(privateKeyPem, nameof(privateKeyPem)).NotNull().NotEmpty();

        // Derive public key using BouncyCastle
        string publicKey;
        try
        {
            publicKey = DeriveOpenSshPublicKeyFromPrivatePem(privateKeyPem);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to derive public key from private PEM.", ex);
        }

        var builder = Merge(DockerResourceConfiguration, new SshdConfiguration())
            // Provide PUBLIC_KEY so the image can install the key during initialization,
            // and also keep a startup callback as a fallback.
            .WithEnvironment("PUBLIC_KEY", publicKey)
            .WithStartupCallback(async (container, ct) =>
            {
                // Ensure target directory exists and write authorized_keys
                await container.CopyAsync(Encoding.ASCII.GetBytes(publicKey + "\n"), containerPublicKeyPath, fileMode: Unix.FileMode644, ct: ct).ConfigureAwait(false);

                var cmd = new[] { "/bin/sh", "-c", $"mkdir -p $(dirname {containerPublicKeyPath}) || true && chmod 644 {containerPublicKeyPath} || true && (chown --reference=/home/$USER_NAME {containerPublicKeyPath} || chown --reference=/root {containerPublicKeyPath}) || true" };
                await container.ExecAsync(cmd, ct).ConfigureAwait(false);
            });

        return builder;
    }

    // WithPrivateKeyFile (bind-mount) removed to avoid bind-mounting private keys into containers.

    /// <summary>
    /// Copy a private key file from the host into the container (no bind-mount).
    /// The host file is read by the test process and the contents are written into the
    /// container at <paramref name="containerPrivateKeyPath"/>. The public key is derived
    /// from the private key content and installed into the container's authorized_keys.
    /// This avoids bind-mounting secrets into the container filesystem.
    /// </summary>
    public SshdBuilder WithPrivateKeyFileCopied(string hostPrivateKeyPath, string containerPrivateKeyPath = "/root/.ssh/id_rsa", string containerPublicKeyPath = "/root/.ssh/authorized_keys")
    {
        _ = Guard.Argument(hostPrivateKeyPath, nameof(hostPrivateKeyPath)).NotNull().NotEmpty();

        if (!File.Exists(hostPrivateKeyPath))
        {
            throw new InvalidOperationException($"Host private key file '{hostPrivateKeyPath}' does not exist.");
        }

        string privateKeyPem;
        try
        {
            // Read as ASCII to match typical PEM encoding
            privateKeyPem = File.ReadAllText(hostPrivateKeyPath, Encoding.ASCII);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read private key file '{hostPrivateKeyPath}'.", ex);
        }

        string publicKey;
        try
        {
            publicKey = DeriveOpenSshPublicKeyFromPrivatePem(privateKeyPem);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to derive public key from private PEM.", ex);
        }

        var builder = Merge(DockerResourceConfiguration, new SshdConfiguration())
            // Provide PUBLIC_KEY so the image can install the key during initialization,
            // and also keep a startup callback as a fallback that copies both private and public keys into the container.
            .WithEnvironment("PUBLIC_KEY", publicKey)
            .WithStartupCallback(async (container, ct) =>
            {
                // Copy private key content into container
                await container.CopyAsync(Encoding.ASCII.GetBytes(privateKeyPem + "\n"), containerPrivateKeyPath, fileMode: DotNet.Testcontainers.Configurations.UnixFileModes.UserRead | DotNet.Testcontainers.Configurations.UnixFileModes.UserWrite, ct: ct).ConfigureAwait(false);

                // Ensure authorized_keys exists and contains the public key
                await container.CopyAsync(Encoding.ASCII.GetBytes(publicKey + "\n"), containerPublicKeyPath, fileMode: Unix.FileMode644, ct: ct).ConfigureAwait(false);

                // Set permissions and ownership: attempt to chown to user home reference, fallback to root
                var cmd = new[] { "/bin/sh", "-c", $"mkdir -p $(dirname {containerPrivateKeyPath}) $(dirname {containerPublicKeyPath}) || true && chmod 600 {containerPrivateKeyPath} || true && chmod 644 {containerPublicKeyPath} || true && (chown --reference=/home/$USER_NAME {containerPrivateKeyPath} {containerPublicKeyPath} || chown --reference=/root {containerPrivateKeyPath} {containerPublicKeyPath}) || true" };
                await container.ExecAsync(cmd, ct).ConfigureAwait(false);
            });

        return builder;
    }

    private static string DeriveOpenSshPublicKeyFromPrivatePem(string privateKeyPem)
    {
        var reader = new Org.BouncyCastle.OpenSsl.PemReader(new StringReader(privateKeyPem));
        var keyObject = reader.ReadObject();

        Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters rsaPub = null;

        if (keyObject is Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair pair)
        {
            rsaPub = (Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters)pair.Public;
        }
        else if (keyObject is Org.BouncyCastle.Crypto.Parameters.RsaPrivateCrtKeyParameters priv)
        {
            rsaPub = new Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters(false, priv.Modulus, priv.PublicExponent);
        }
        else if (keyObject is Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters k)
        {
            rsaPub = k;
        }
        else
        {
            throw new InvalidOperationException("Unsupported PEM key format for RSA key extraction.");
        }

        var e = rsaPub.Exponent.ToByteArrayUnsigned();
        var n = rsaPub.Modulus.ToByteArrayUnsigned();

        byte[] sshRsa;
        using (var ms = new MemoryStream())
        {
            void WriteUInt32(uint v)
            {
                var b = new byte[4];
                b[0] = (byte)((v >> 24) & 0xff);
                b[1] = (byte)((v >> 16) & 0xff);
                b[2] = (byte)((v >> 8) & 0xff);
                b[3] = (byte)(v & 0xff);
                ms.Write(b, 0, 4);
            }
            void WriteString(string s)
            {
                var data = Encoding.ASCII.GetBytes(s);
                WriteUInt32((uint)data.Length);
                ms.Write(data, 0, data.Length);
            }
            void WriteMpint(byte[] arr)
            {
                if (arr == null || arr.Length == 0) { WriteUInt32(0); return; }
                if ((arr[0] & 0x80) != 0)
                {
                    var withZero = new byte[arr.Length + 1];
                    withZero[0] = 0x00;
                    Buffer.BlockCopy(arr, 0, withZero, 1, arr.Length);
                    WriteUInt32((uint)withZero.Length);
                    ms.Write(withZero, 0, withZero.Length);
                }
                else
                {
                    WriteUInt32((uint)arr.Length);
                    ms.Write(arr, 0, arr.Length);
                }
            }

            WriteString("ssh-rsa");
            WriteMpint(e);
            WriteMpint(n);
            sshRsa = ms.ToArray();
        }

        var pub64 = Convert.ToBase64String(sshRsa);
        return $"ssh-rsa {pub64} generated-key";
    }
}
