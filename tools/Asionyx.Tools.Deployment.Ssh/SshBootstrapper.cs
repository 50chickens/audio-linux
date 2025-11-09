using Renci.SshNet;

namespace Asionyx.Tools.Deployment.Ssh
{
    public class SshBootstrapper
    {
        private readonly string? _host;
        private readonly int? _port;
        private readonly string? _user;
        private readonly string? _keyPath;

        public SshBootstrapper()
        {
        }

        public SshBootstrapper(string host, int port, string user, string keyPath)
        {
            _host = host;
            _port = port;
            _user = user;
            _keyPath = keyPath;
        }
        /// <summary>
        /// Upload a single file to the remote host using a private key.
        /// </summary>
        public void UploadFile(string host, int port, string user, string keyPath, string localFile, string remotePath)
        {
            if (!File.Exists(localFile)) throw new FileNotFoundException("Local file not found", localFile);
            if (string.IsNullOrEmpty(keyPath) || !File.Exists(keyPath)) throw new InvalidOperationException("No private key file found for authentication.");

            using var pkStream = File.OpenRead(keyPath);
            var pkey = new PrivateKeyFile(pkStream);
            using var scp = new ScpClient(host, port, user, pkey);
            scp.Connect();
            scp.Upload(new FileInfo(localFile), remotePath);
            scp.Disconnect();
        }

        /// <summary>
        /// Upload a single file using constructor-configured connection info.
        /// </summary>
        public void UploadFile(string localFile, string remotePath)
        {
            if (string.IsNullOrEmpty(_host) || !_port.HasValue || string.IsNullOrEmpty(_user) || string.IsNullOrEmpty(_keyPath))
            {
                throw new InvalidOperationException("SshBootstrapper not configured: provide host, port, user and keyPath in constructor or call the overload with parameters.");
            }

            UploadFile(_host, _port.Value, _user, _keyPath, localFile, remotePath);
        }

        /// <summary>
        /// Upload a directory recursively to the remote host. Creates remote directories as needed.
        /// </summary>
        public void UploadDirectory(string host, int port, string user, string keyPath, string localDir, string remoteDir)
        {
            if (!Directory.Exists(localDir)) throw new DirectoryNotFoundException(localDir);

            if (string.IsNullOrEmpty(keyPath) || !File.Exists(keyPath)) throw new InvalidOperationException("No private key file found for authentication.");

            using var pkStream = File.OpenRead(keyPath);
            var pkey = new PrivateKeyFile(pkStream);
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

        /// <summary>
        /// Upload a directory using constructor-configured connection info.
        /// </summary>
        public void UploadDirectory(string localDir, string remoteDir)
        {
            if (string.IsNullOrEmpty(_host) || !_port.HasValue || string.IsNullOrEmpty(_user) || string.IsNullOrEmpty(_keyPath))
            {
                throw new InvalidOperationException("SshBootstrapper not configured: provide host, port, user and keyPath in constructor or call the overload with parameters.");
            }

            UploadDirectory(_host, _port.Value, _user, _keyPath, localDir, remoteDir);
        }

        /// <summary>
        /// Run a remote command and return exit status, stdout and stderr.
        /// </summary>
        public (int ExitCode, string Output, string Error) RunCommand(string host, int port, string user, string keyPath, string command)
        {
            if (string.IsNullOrEmpty(keyPath) || !File.Exists(keyPath)) throw new InvalidOperationException("No private key file found for authentication.");

            using var pkStream = File.OpenRead(keyPath);
            var pkey = new PrivateKeyFile(pkStream);
            using var ssh = new SshClient(host, port, user, pkey);
            ssh.Connect();
            var cmd = ssh.RunCommand(command);
            ssh.Disconnect();

            // Use reflection to read properties to avoid runtime linkage issues across different Renci.SshNet package versions
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
                // best-effort: leave defaults
            }

            return (exitStatus, result, error);
        }

        /// <summary>
        /// Run a remote command using constructor-configured connection info.
        /// </summary>
        public (int ExitCode, string Output, string Error) RunCommand(string command)
        {
            if (string.IsNullOrEmpty(_host) || !_port.HasValue || string.IsNullOrEmpty(_user) || string.IsNullOrEmpty(_keyPath))
            {
                throw new InvalidOperationException("SshBootstrapper not configured: provide host, port, user and keyPath in constructor or call the overload with parameters.");
            }

            return RunCommand(_host!, _port!.Value, _user!, _keyPath!, command);
        }

        // ConnectionInfo factory removed - use direct client constructors that accept PrivateKeyFile to avoid cross-version linkage issues.
    }
}
