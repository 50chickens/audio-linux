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
        private readonly bool _autoConvertKey;
    private readonly string? _keyContent;
    private readonly bool _keyIsContent;

        public SshBootstrapper()
        {
        }

        public SshBootstrapper(string host, string user, string keyPath, int port = 22, bool autoConvertKey = false)
        {
            _host = host;
            _port = port;
            _user = user;
            _keyPath = keyPath;
            _autoConvertKey = autoConvertKey;
        }

        public void UploadFile(string host, int port, string user, string keyPath, string localFile, string remotePath)
        {
            if (!File.Exists(localFile)) throw new FileNotFoundException("Local file not found", localFile);
            if (string.IsNullOrEmpty(keyPath) && !_keyIsContent) throw new InvalidOperationException("No private key file found for authentication.");
            var pkey = LoadPrivateKeyFile(keyPath);
            using var scp = new ScpClient(host, port, user, pkey);
            scp.Connect();
            scp.Upload(new FileInfo(localFile), remotePath);
            scp.Disconnect();
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
            using var scp = new ScpClient(host, port, user, pkey);
            using var ssh = new SshClient(host, port, user, pkey);
            scp.Connect();
            ssh.Connect();

            // Ensure base remote dir exists
            ssh.RunCommand($"mkdir -p {remoteDir}");

            foreach (var file in Directory.EnumerateFiles(localDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(localDir, file).Replace('\\', '/');
                var remotePath = $"{remoteDir}/{relative}";
                var remoteDirPath = remotePath.Substring(0, remotePath.LastIndexOf('/'));
                ssh.RunCommand($"mkdir -p {remoteDirPath}");
                using var fs = File.OpenRead(file);
                scp.Upload(fs, remotePath);
            }

            scp.Disconnect();
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

            // If auto-convert is disabled, try to load directly as PEM
            if (!_autoConvertKey)
            {
                // If content was provided, create stream from it, otherwise open file
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

            // Auto-convert: detect OpenSSH or Putty PPK format
            if (_autoConvertKey && text.Contains("-----BEGIN OPENSSH PRIVATE KEY-----"))
            {
                var pem = ConvertOpenSshToPem(text);
                var bytes = System.Text.Encoding.ASCII.GetBytes(pem + "\n");
                var ms = new MemoryStream(bytes);
                return new PrivateKeyFile(ms);
            }

            // Fallback: try to load as PEM from content (or convert OpenSSH to PEM)
            try
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(text);
                return new PrivateKeyFile(new MemoryStream(bytes));
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

        private static string ConvertOpenSshToPem(string opensshText)
        {
            // Extract base64 payload from the OpenSSH private key
            const string header = "-----BEGIN OPENSSH PRIVATE KEY-----";
            const string footer = "-----END OPENSSH PRIVATE KEY-----";
            var start = opensshText.IndexOf(header, StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("Not an OpenSSH private key.");
            start += header.Length;
            var end = opensshText.IndexOf(footer, start, StringComparison.Ordinal);
            if (end < 0) throw new InvalidOperationException("Not an OpenSSH private key.");
            var b64 = opensshText.Substring(start, end - start);
            b64 = b64.Replace("\n", string.Empty).Replace("\r", string.Empty).Trim();
            var data = Convert.FromBase64String(b64);

            int idx = 0;
            static uint ReadUInt32(byte[] buf, ref int i)
            {
                if (i + 4 > buf.Length) throw new InvalidOperationException("Unexpected EOF");
                uint v = (uint)((buf[i] << 24) | (buf[i + 1] << 16) | (buf[i + 2] << 8) | buf[i + 3]);
                i += 4;
                return v;
            }
            static byte[] ReadBytes(byte[] buf, ref int i, int len)
            {
                if (i + len > buf.Length) throw new InvalidOperationException("Unexpected EOF");
                var r = new byte[len];
                Buffer.BlockCopy(buf, i, r, 0, len);
                i += len;
                return r;
            }

            // read magic string (null-terminated)
            int z = Array.IndexOf<byte>(data, 0, idx);
            if (z < 0) throw new InvalidOperationException("Invalid OpenSSH key header");
            var magic = System.Text.Encoding.ASCII.GetString(data, idx, z - idx);
            idx = z + 1;
            if (magic != "openssh-key-v1") throw new InvalidOperationException("Unsupported OpenSSH key format.");

            // read ciphername, kdfname, kdfoptions (all length-prefixed strings)
            uint cipherNameLen = ReadUInt32(data, ref idx);
            var cipherName = System.Text.Encoding.ASCII.GetString(ReadBytes(data, ref idx, (int)cipherNameLen));
            uint kdfNameLen = ReadUInt32(data, ref idx);
            var kdfName = System.Text.Encoding.ASCII.GetString(ReadBytes(data, ref idx, (int)kdfNameLen));
            uint kdfOptionsLen = ReadUInt32(data, ref idx);
            var kdfOptions = ReadBytes(data, ref idx, (int)kdfOptionsLen);

            if (!string.Equals(cipherName, "none", StringComparison.Ordinal) || !string.Equals(kdfName, "none", StringComparison.Ordinal))
            {
                throw new NotSupportedException("Encrypted OpenSSH private keys are not supported by auto-convert.");
            }

            // number of public keys
            var nkeys = ReadUInt32(data, ref idx);
            for (uint i = 0; i < nkeys; ++i)
            {
                var l = (int)ReadUInt32(data, ref idx);
                _ = ReadBytes(data, ref idx, l); // skip pubkeyblob
            }

            var privLen = (int)ReadUInt32(data, ref idx);
            var privBlob = ReadBytes(data, ref idx, privLen);

            int pidx = 0;
            var check1 = ReadUInt32(privBlob, ref pidx);
            var check2 = ReadUInt32(privBlob, ref pidx);

            // read key type
            var ktlen = (int)ReadUInt32(privBlob, ref pidx);
            var keyType = System.Text.Encoding.ASCII.GetString(ReadBytes(privBlob, ref pidx, ktlen));

            if (keyType != "ssh-rsa") throw new NotSupportedException($"Key type '{keyType}' not supported by auto-convert.");

            static byte[] ReadMPInt(byte[] buf, ref int ii)
            {
                var l = (int)ReadUInt32(buf, ref ii);
                return ReadBytes(buf, ref ii, l);
            }

            var n = ReadMPInt(privBlob, ref pidx);
            var e = ReadMPInt(privBlob, ref pidx);
            var d = ReadMPInt(privBlob, ref pidx);
            var iqmp = ReadMPInt(privBlob, ref pidx);
            var p = ReadMPInt(privBlob, ref pidx);
            var q = ReadMPInt(privBlob, ref pidx);

            // build BouncyCastle RSA parameters
            var biN = new Org.BouncyCastle.Math.BigInteger(1, n);
            var biE = new Org.BouncyCastle.Math.BigInteger(1, e);
            var biD = new Org.BouncyCastle.Math.BigInteger(1, d);
            var biP = new Org.BouncyCastle.Math.BigInteger(1, p);
            var biQ = new Org.BouncyCastle.Math.BigInteger(1, q);

            var biDp = biD.Mod(biP.Subtract(Org.BouncyCastle.Math.BigInteger.One));
            var biDq = biD.Mod(biQ.Subtract(Org.BouncyCastle.Math.BigInteger.One));
            var biQInv = biQ.ModInverse(biP);

            var rsaPriv = new Org.BouncyCastle.Crypto.Parameters.RsaPrivateCrtKeyParameters(biN, biE, biD, biP, biQ, biDp, biDq, biQInv);

            // Write to PEM PKCS#1 (BEGIN RSA PRIVATE KEY)
            using var sw = new StringWriter();
            var pemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(sw);
            pemWriter.WriteObject(rsaPriv);
            pemWriter.Writer.Flush();
            return sw.ToString();
        }

        
    }
}
