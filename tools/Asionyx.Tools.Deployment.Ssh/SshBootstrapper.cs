using Renci.SshNet;

namespace Asionyx.Tools.Deployment.Ssh
{
    public class SshBootstrapper
    {
        /// <summary>
        /// Upload a single file to the remote host using a private key.
        /// </summary>
        public void UploadFile(string host, int port, string user, string keyPath, string localFile, string remotePath)
        {
            if (!File.Exists(localFile)) throw new FileNotFoundException("Local file not found", localFile);

            var conn = CreateConnectionInfo(host, port, user, keyPath);
            using var scp = new ScpClient(conn);
            scp.Connect();
            scp.Upload(new FileInfo(localFile), remotePath);
            scp.Disconnect();
        }

        /// <summary>
        /// Upload a directory recursively to the remote host. Creates remote directories as needed.
        /// </summary>
        public void UploadDirectory(string host, int port, string user, string keyPath, string localDir, string remoteDir)
        {
            if (!Directory.Exists(localDir)) throw new DirectoryNotFoundException(localDir);

            var conn = CreateConnectionInfo(host, port, user, keyPath);
            using var scp = new ScpClient(conn);
            using var ssh = new SshClient(conn);
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
        /// Run a remote command and return exit status, stdout and stderr.
        /// </summary>
        public (int ExitCode, string Output, string Error) RunCommand(string host, int port, string user, string keyPath, string command)
        {
            var conn = CreateConnectionInfo(host, port, user, keyPath);
            using var ssh = new SshClient(conn);
            ssh.Connect();
            var cmd = ssh.RunCommand(command);
            ssh.Disconnect();
            return (cmd.ExitStatus, cmd.Result ?? string.Empty, cmd.Error ?? string.Empty);
        }

        private ConnectionInfo CreateConnectionInfo(string host, int port, string user, string keyPath)
        {
            if (!string.IsNullOrEmpty(keyPath) && File.Exists(keyPath))
            {
                using var pkStream = File.OpenRead(keyPath);
                var pkey = new PrivateKeyFile(pkStream);
                var auth = new PrivateKeyAuthenticationMethod(user, pkey);
                return new ConnectionInfo(host, port, user, auth);
            }

            throw new InvalidOperationException("No authentication available. Provide a private key path.");
        }
    }
}
