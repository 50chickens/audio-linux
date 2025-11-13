using Renci.SshNet;

namespace Asionyx.Tools.Deployment.Client.Library.Ssh
{
    // Copied into the client project so the console app is self-contained.
    public class SshBootstrapper
    {
        private readonly string? _host;
        private readonly int? _port;
        private readonly string? _user;
        private readonly string? _keyPath;
    private readonly string? _keyContent;
    private readonly bool _keyIsContent;

        public SshBootstrapper()
        {
        }

        public SshBootstrapper(string host, string user, string keyPath, int port = 22)
        {
            _host = host;
            _port = port;
            _user = user;
            _keyPath = keyPath;
        }

        // Backwards-compatible overloads used by tests and callers with alternate parameter orders.
        public SshBootstrapper(string host, int port, string user, string keyPath)
        {
            _host = host;
            _port = port;
            _user = user;
            _keyPath = keyPath;
        }

        public SshBootstrapper(string host, string user, string keyContent, bool keyIsContent, int port = 22)
        {
            _host = host;
            _port = port;
            _user = user;
            _keyContent = keyContent;
            _keyIsContent = keyIsContent;
        }

        public SshBootstrapper(string host, int port, string user, string keyContent, bool keyIsContent)
        {
            _host = host;
            _port = port;
            _user = user;
            _keyContent = keyContent;
            _keyIsContent = keyIsContent;
        }

        public void UploadFile(string host, int port, string user, string keyPath, string localFile, string remotePath)
        {
            if (!File.Exists(localFile)) throw new FileNotFoundException("Local file not found", localFile);
            if (string.IsNullOrEmpty(keyPath) && !_keyIsContent) throw new InvalidOperationException("No private key file found for authentication.");
            var pkey = LoadPrivateKeyFile(keyPath);
            // Use SFTP upload so we can provide progress feedback
            using var sftp = new SftpClient(host, port, user, pkey);
            sftp.Connect();
            using var fs = File.OpenRead(localFile);
            var total = fs.Length;
            ulong lastReported = 0;
            void ProgressCallback(ulong uploaded)
            {
                // Print progress on a single line
                lastReported = uploaded;
                try
                {
                    var percent = total > 0 ? (int)((uploaded * 100) / (ulong)total) : 0;
                    Console.Write($"\rUploading {Path.GetFileName(localFile)}: {uploaded}/{total} bytes ({percent}%)");
                }
                catch
                {
                    // ignore console errors
                }
            }

            sftp.UploadFile(fs, remotePath, ProgressCallback);
            Console.WriteLine();
            sftp.Disconnect();
        }

        public void UploadFile(string localFile, string remotePath)
        {
            if (string.IsNullOrEmpty(_host) || !_port.HasValue || string.IsNullOrEmpty(_user) || (string.IsNullOrEmpty(_keyPath) && !_keyIsContent))
            {
                throw new InvalidOperationException("SshBootstrapper not configured: provide host, port, user and keyPath (or in-memory key content) in constructor or call the overload with parameters.");
            }

            UploadFile(_host, _port.Value, _user, _keyPath, localFile, remotePath);
        }

        public void UploadDirectory(string host, int port, string user, string keyPath, string localDir, string remoteDir)
        {
            if (!Directory.Exists(localDir)) throw new DirectoryNotFoundException(localDir);

            if (string.IsNullOrEmpty(keyPath) && !_keyIsContent) throw new InvalidOperationException("No private key file found for authentication.");
            var pkey = LoadPrivateKeyFile(keyPath);
            using var sftp = new SftpClient(host, port, user, pkey);
            using var ssh = new SshClient(host, port, user, pkey);
            sftp.Connect();
            ssh.Connect();

            // Ensure base remote dir exists
            ssh.RunCommand($"mkdir -p {remoteDir}");

            foreach (var file in Directory.EnumerateFiles(localDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(localDir, file).Replace('\\', '/');
                var fileName = Path.GetFileName(file);
                // Never upload appsettings files from the publish bundle. The deployed service must be the
                // only actor that creates or updates appsettings.json on the remote host.
                if (fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Skipping upload of '{relative}' (appsettings files are managed by the service on the host)");
                    continue;
                }
                var remotePath = $"{remoteDir}/{relative}";
                var remoteDirPath = remotePath.Substring(0, remotePath.LastIndexOf('/'));
                ssh.RunCommand($"mkdir -p {remoteDirPath}");
                using var fs = File.OpenRead(file);
                var total = fs.Length;
                ulong lastReported = 0;
                void ProgressCallback(ulong uploaded)
                {
                    lastReported = uploaded;
                    try
                    {
                        var percent = total > 0 ? (int)((uploaded * 100) / (ulong)total) : 0;
                        Console.Write($"\rUploading {relative}: {uploaded}/{total} bytes ({percent}%)");
                    }
                    catch
                    {
                    }
                }

                sftp.UploadFile(fs, remotePath, ProgressCallback);
                Console.WriteLine();
            }

            sftp.Disconnect();
            ssh.Disconnect();
        }

        public void UploadDirectory(string localDir, string remoteDir)
        {
            if (string.IsNullOrEmpty(_host) || !_port.HasValue || string.IsNullOrEmpty(_user) || (string.IsNullOrEmpty(_keyPath) && !_keyIsContent))
            {
                throw new InvalidOperationException("SshBootstrapper not configured: provide host, port, user and keyPath (or in-memory key content) in constructor or call the overload with parameters.");
            }

            UploadDirectory(_host, _port.Value, _user, _keyPath, localDir, remoteDir);
        }

        public (int ExitCode, string Output, string Error) RunCommand(string host, int port, string user, string keyPath, string command)
        {
            var pkey = LoadPrivateKeyFile(keyPath);

            // Build a ConnectionInfo with the private key auth method so we can
            // attempt to configure algorithm lists (KEX / host key) to match
            // the real SSH server's advertised algorithms (see user's ssh -vvv output).
            // We try to add curve25519 and ssh-ed25519 algorithm implementations
            // reflectively if the loaded SSH.NET assembly exposes matching types.
            Renci.SshNet.ConnectionInfo conn;
            var keyAuth = new Renci.SshNet.PrivateKeyAuthenticationMethod(user, pkey);
            try
            {
                conn = new Renci.SshNet.ConnectionInfo(host, port, user, keyAuth);

                try
                {
                    var asm = typeof(Renci.SshNet.ConnectionInfo).Assembly;
                    Type? FindType(string token)
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return t;
                        }
                        return null;
                    }

                    // Add KEX algorithms if the ConnectionInfo exposes the dictionary
                    var kexProp = conn.GetType().GetProperty("KeyExchangeAlgorithms");
                    if (kexProp != null)
                    {
                        if (kexProp.GetValue(conn) is System.Collections.IDictionary kexDict)
                        {
                            var curveType = FindType("curve25519");
                            if (curveType != null && !kexDict.Contains("curve25519-sha256"))
                            {
                                kexDict["curve25519-sha256"] = curveType;
                            }
                            if (curveType != null && !kexDict.Contains("curve25519-sha256@libssh.org"))
                            {
                                kexDict["curve25519-sha256@libssh.org"] = curveType;
                            }
                            // Try to add ECDH (nistp) KEX entries if SSH.NET exposes an implementation
                            var ecdhType = FindType("ecdh");
                            if (ecdhType != null)
                            {
                                if (!kexDict.Contains("ecdh-sha2-nistp256")) kexDict["ecdh-sha2-nistp256"] = ecdhType;
                                if (!kexDict.Contains("ecdh-sha2-nistp384")) kexDict["ecdh-sha2-nistp384"] = ecdhType;
                                if (!kexDict.Contains("ecdh-sha2-nistp521")) kexDict["ecdh-sha2-nistp521"] = ecdhType;
                            }
                        }
                    }

                    // Add host key algorithm for ed25519 if present
                    var hostProp = conn.GetType().GetProperty("HostKeyAlgorithms");
                    if (hostProp != null)
                    {
                        if (hostProp.GetValue(conn) is System.Collections.IDictionary hostDict)
                        {
                            var edType = FindType("ed25519");
                            if (edType != null && !hostDict.Contains("ssh-ed25519"))
                            {
                                hostDict["ssh-ed25519"] = edType;
                            }
                        }
                    }
                }
                catch
                {
                    // Best-effort only — if reflection/augmentation fails, continue with defaults.
                }
            }
            catch
            {
                // Fall back to the simple constructor if anything unexpected happens
                conn = new Renci.SshNet.ConnectionInfo(host, port, user, keyAuth);
            }

            using var ssh = new Renci.SshNet.SshClient(conn);
            try
            {
                ssh.Connect();
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                // Augment the error with the full exception (stack trace) and any algorithm dictionaries
                try
                {
                    var sb = new System.Text.StringBuilder();

                    // Include full exception.ToString() so the stack trace is visible inline
                    sb.AppendLine("SSH connection failed: ");
                    sb.AppendLine(ex.ToString());
                    sb.AppendLine();

                    var t = conn.GetType();

                    // Inspect all properties on ConnectionInfo and print any IDictionary contents we find
                    foreach (var prop in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        try
                        {
                            var val = prop.GetValue(conn);
                            if (val is System.Collections.IDictionary dict)
                            {
                                sb.AppendLine(prop.Name + ":");
                                foreach (var key in dict.Keys)
                                {
                                    var v = dict[key];
                                    string vdesc = v?.GetType().Name ?? "null";
                                    sb.AppendLine("  " + key + " -> " + vdesc);
                                }
                                sb.AppendLine();
                            }
                        }
                        catch
                        {
                            // ignore per-property reflection errors — best-effort diagnostics only
                        }
                    }

                    // Also include a short summary of local connection info fields
                    try
                    {
                        sb.AppendLine($"Host: {conn.Host}");
                        sb.AppendLine($"Port: {conn.Port}");
                        sb.AppendLine($"Username: {conn.Username}");
                    }
                    catch
                    {
                    }

                    throw new InvalidOperationException(sb.ToString(), ex);
                }
                catch
                {
                    // If anything goes wrong while building diagnostics, fall back to original exception
                    throw;
                }
            }
            catch (Exception ex)
            {
                // Catch-all: provide the same rich diagnostics for any other exception raised during connect
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("SSH connection attempt threw an exception:");
                    sb.AppendLine(ex.ToString());
                    sb.AppendLine();

                    var t = conn.GetType();
                    foreach (var prop in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        try
                        {
                            var val = prop.GetValue(conn);
                            if (val is System.Collections.IDictionary dict)
                            {
                                sb.AppendLine(prop.Name + ":");
                                foreach (var key in dict.Keys)
                                {
                                    var v = dict[key];
                                    string vdesc = v?.GetType().Name ?? "null";
                                    sb.AppendLine("  " + key + " -> " + vdesc);
                                }
                                sb.AppendLine();
                            }
                        }
                        catch
                        {
                        }
                    }

                    try
                    {
                        sb.AppendLine($"Host: {conn.Host}");
                        sb.AppendLine($"Port: {conn.Port}");
                        sb.AppendLine($"Username: {conn.Username}");
                    }
                    catch
                    {
                    }

                    throw new InvalidOperationException(sb.ToString(), ex);
                }
                catch
                {
                    throw;
                }
            }
            var cmd = ssh.RunCommand(command);
            ssh.Disconnect();

            int exitStatus = -1;
            string result = string.Empty;
            string error = string.Empty;

            try
            {
                var cmdType = cmd.GetType();
                var exitProp = cmdType.GetProperty("ExitStatus");
                if (exitProp != null)
                {
                    var val = exitProp.GetValue(cmd);
                    if (val is int i) exitStatus = i;
                    else if (val != null) exitStatus = Convert.ToInt32(val);
                }

                var resProp = cmdType.GetProperty("Result");
                if (resProp != null) result = resProp.GetValue(cmd) as string ?? string.Empty;

                var errProp = cmdType.GetProperty("Error");
                if (errProp != null) error = errProp.GetValue(cmd) as string ?? string.Empty;
            }
            catch
            {
            }

            return (exitStatus, result, error);
        }

        public (int ExitCode, string Output, string Error) RunCommand(string command)
        {
            if (string.IsNullOrEmpty(_host) || !_port.HasValue || string.IsNullOrEmpty(_user) || (string.IsNullOrEmpty(_keyPath) && !_keyIsContent))
            {
                throw new InvalidOperationException("SshBootstrapper not configured: provide host, port, user and keyPath (or in-memory key content) in constructor or call the overload with parameters.");
            }

            return RunCommand(_host!, _port!.Value, _user!, _keyPath!, command);
        }

        private PrivateKeyFile LoadPrivateKeyFile(string keyPath)
        {
            // If key content was supplied directly (in-memory), use it instead of reading a file
            string text;
            if (_keyIsContent)
            {
                text = _keyContent ?? string.Empty;
            }
            else
            {
                if (string.IsNullOrEmpty(keyPath) || !File.Exists(keyPath)) throw new InvalidOperationException("No private key file found for authentication.");
                text = File.ReadAllText(keyPath, System.Text.Encoding.ASCII);
            }

            // Try to load as PEM from content or file.
            try
            {
                if (_keyIsContent)
                {
                    var bytes = System.Text.Encoding.ASCII.GetBytes(text);
                    return new PrivateKeyFile(new MemoryStream(bytes));
                }
                else
                {
                    using var s = File.OpenRead(keyPath);
                    return new PrivateKeyFile(s);
                }
            }
            catch
            {
                // If fallback fails and we didn't already try file mode, try file
                if (!_keyIsContent && File.Exists(keyPath))
                {
                    using var s = File.OpenRead(keyPath);
                    return new PrivateKeyFile(s);
                }
                throw new InvalidOperationException("Failed to load or convert private key material.");
            }
        }
    }
}
