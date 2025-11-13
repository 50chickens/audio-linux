using System.CommandLine.Parsing;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Configuration;
using Asionyx.Tools.Deployment.Client.Library.Ssh;

public class SshCliRunner
{
    private readonly SshOptions _options;
    private readonly IConfiguration _config;

    public SshCliRunner(SshOptions options, IConfiguration config)
    {
        _options = options ?? new SshOptions();
        _config = config;
    }

    public async Task<int> HandleAsync(string? genPrefix, bool genRetry, bool toStdout, bool doVerify, bool verifyHostConfig, bool checkService, string? serviceName, string? hostOverride, int? portOverride, string? userOverride, string? keyOverride, bool clearJournal, bool ensureRemoteDir, bool ensureUserDataDir, bool installSystemdUnit)
    {
        // Optional overrides for verify/generate
        var host = hostOverride ?? _options.Host;
    int effectivePort = portOverride ?? _options.Port;
        var user = userOverride ?? _options.User;
        var keyPath = string.IsNullOrWhiteSpace(keyOverride) ? _options.KeyPath : keyOverride;

        // Generation flow
        if (!string.IsNullOrEmpty(genPrefix))
        {
            var prefix = string.IsNullOrEmpty(genPrefix) ? "id_rsa" : genPrefix;
            var (priv, pubKey) = KeyGenerator.GenerateRsaKeyPair();
            if (toStdout)
            {
                Console.WriteLine(priv);
                Console.WriteLine(pubKey);
            }

            Console.WriteLine();
            Console.WriteLine("--- Remote install script (paste into remote host shell) ---");
            var publicKeyLine = pubKey?.TrimEnd('\r','\n') ?? string.Empty;
            var script = $@"# Paste the following on the REMOTE HOST to add the public key to ~/.ssh/authorized_keys
mkdir -p ~/.ssh
chmod 700 ~/.ssh
cat >> ~/.ssh/authorized_keys <<'AUTHORIZED_KEYS'
{publicKeyLine}
AUTHORIZED_KEYS
chmod 600 ~/.ssh/authorized_keys
echo 'Public key installed'
";
            Console.WriteLine(script);
            Console.WriteLine("--- end script ---");

            if (genRetry)
            {
                if (toStdout)
                {
                    Console.Error.WriteLine("Cannot use --retry when writing keys to stdout. Provide a file prefix instead.");
                    return 2;
                }

                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user))
                {
                    Console.Error.WriteLine("Error: --ssh-host and --ssh-user are required when using --retry to test generated keys.");
                    return 2;
                }

                var privatePath = System.IO.Path.GetFullPath(prefix);
                Console.WriteLine($"Waiting to test generated keypair using private key file: {privatePath}");
                while (true)
                {
                    Console.WriteLine("Press Enter to attempt authentication with the generated key, or type 'q' then Enter to quit.");
                    var line = Console.ReadLine();
                    if (line != null && line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Aborting wait/test loop by user request.");
                        break;
                    }

                    try
                    {
                        var sbTest = new SshBootstrapper(host!, user!, privatePath, effectivePort);
                        var (exit, output, err) = await Task.Run(() => sbTest.RunCommand("echo hello-asionyx-keytest"));
                        if (exit == 0)
                        {
                            Console.WriteLine("Success: generated key authenticated successfully.");
                            break;
                        }
                        else
                        {
                            Console.WriteLine($"Authentication failed (exit={exit}). Error: {err}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Authentication attempt threw:");
                        Console.WriteLine(ex.ToString());
                    }
                }
            }

            return 0;
        }

        // Option: Verify remote host configuration (quick checks)
        if (verifyHostConfig)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyPath))
            {
                Console.Error.WriteLine("Error: --ssh-host, --ssh-user and --ssh-key (or configured defaults) are required for --verify-host-configuration.");
                return 2;
            }

            Console.WriteLine($"Verifying remote host configuration on {host} (sudo, pwsh, dotnet)...");
            try
            {
                var sb = new SshBootstrapper(host!, user!, keyPath!, effectivePort);

                // Test non-interactive sudo
                try
                {
                    var (sExit, sOut, sErr) = await Task.Run(() => sb.RunCommand("sudo -n true"));
                    if (sExit == 0)
                    {
                        Console.WriteLine("Sudo: non-interactive sudo appears to work (sudo -n true returned 0).");
                    }
                    else
                    {
                        Console.WriteLine("Sudo: non-interactive sudo failed (sudo -n true returned non-zero). Some install steps may require interactive sudo.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Sudo check threw: " + ex.Message);
                }

                // Check pwsh
                try
                {
                    var (pExit, pOut, pErr) = await Task.Run(() => sb.RunCommand("pwsh --version"));
                    if (pExit == 0)
                    {
                        Console.WriteLine("pwsh: found -> " + (pOut ?? pErr ?? string.Empty).Trim());
                    }
                    else
                    {
                        Console.WriteLine("pwsh: not found or returned error (exit=" + pExit + ").");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("pwsh check threw: " + ex.Message);
                }

                // Check dotnet
                try
                {
                    var (dExit, dOut, dErr) = await Task.Run(() => sb.RunCommand("dotnet --version"));
                    if (dExit == 0)
                    {
                        Console.WriteLine("dotnet: found -> " + (dOut ?? dErr ?? string.Empty).Trim());
                    }
                    else
                    {
                        Console.WriteLine("dotnet: not found or returned error (exit=" + dExit + "). Output: " + (dOut ?? dErr ?? string.Empty));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("dotnet check threw: " + ex.Message);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Remote host verification failed: " + ex.Message);
                return 2;
            }
        }

        // Option: Ensure remote deploy directory exists (performed by the SSH client over the SSH session)
        if (ensureRemoteDir)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyPath))
            {
                Console.Error.WriteLine("Error: --ssh-host, --ssh-user and --ssh-key are required for --ensure-remote-dir.");
                return 2;
            }

            Console.WriteLine($"Ensuring remote deploy directory '{"/opt/Asionyx.Service.Deployment.Linux"}' on {host}...");
            try
            {
                var sb = new SshBootstrapper(host!, user!, keyPath!, effectivePort);
                var cmd = $"sudo mkdir -p /opt/Asionyx.Service.Deployment.Linux && sudo chown -R {user}:{user} /opt/Asionyx.Service.Deployment.Linux && sudo chmod 755 /opt/Asionyx.Service.Deployment.Linux";
                var (exit, outp, err) = await Task.Run(() => sb.RunCommand(cmd));
                if (exit == 0)
                {
                    Console.WriteLine("Remote deploy directory ensured.");
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"Failed to ensure remote directory (exit={exit}). stdout: {outp} stderr: {err}");
                    return 3;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Ensure-remote-dir failed: " + ex.Message);
                return 2;
            }
        }

        // Option: Ensure user data directory (~/.Asionyx.Service.Deployment.Linux)
        if (ensureUserDataDir)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyPath))
            {
                Console.Error.WriteLine("Error: --ssh-host, --ssh-user and --ssh-key are required for --ensure-user-data-dir.");
                return 2;
            }

            Console.WriteLine($"Ensuring user data directory for user {user} on {host}...");
            try
            {
                var sb = new SshBootstrapper(host!, user!, keyPath!, effectivePort);
                // Try without sudo first
                var (mkExit, mkOut, mkErr) = await Task.Run(() => sb.RunCommand("mkdir -p ~/.Asionyx.Service.Deployment.Linux && chmod 700 ~/.Asionyx.Service.Deployment.Linux"));
                if (mkExit == 0)
                {
                    Console.WriteLine("User data directory ensured: ~/.Asionyx.Service.Deployment.Linux");
                    return 0;
                }
                else
                {
                    Console.WriteLine("Falling back to sudo creation under /home/{user}...");
                    var cmd = $"sudo mkdir -p /home/{user}/.Asionyx.Service.Deployment.Linux && sudo chown -R {user}:{user} /home/{user}/.Asionyx.Service.Deployment.Linux && sudo chmod 700 /home/{user}/.Asionyx.Service.Deployment.Linux";
                    var (exit2, out2, err2) = await Task.Run(() => sb.RunCommand(cmd));
                    if (exit2 == 0)
                    {
                        Console.WriteLine("User data directory ensured under /home.");
                        return 0;
                    }
                    else
                    {
                        Console.Error.WriteLine($"Failed to ensure user data directory (exit={exit2}). stdout: {out2} stderr: {err2}");
                        return 3;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Ensure-user-data-dir failed: " + ex.Message);
                return 2;
            }
        }

        // Option: Install systemd unit (performed on remote host via SSH)
        if (installSystemdUnit)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyPath))
            {
                Console.Error.WriteLine("Error: --ssh-host, --ssh-user and --ssh-key are required for --install-systemd-unit.");
                return 2;
            }

            try
            {
                var sb = new SshBootstrapper(host!, user!, keyPath!, effectivePort);
                var unit = "[Unit]\\nDescription=Asionyx Deployment Service\\nAfter=network.target\\nStartLimitBurst=3\\nStartLimitIntervalSec=60\\n\\n[Service]\\nWorkingDirectory=/opt/Asionyx.Service.Deployment.Linux\\nExecStart=/usr/bin/dotnet /opt/Asionyx.Service.Deployment.Linux/Asionyx.Service.Deployment.Linux.dll\\nRestart=on-failure\\nRestartSec=5\\nUser=" + user + "\\n\\n[Install]\\nWantedBy=multi-user.target";
                var psSvc = "$unit = @'\\n" + unit + "\\n'@; $unit | Out-File -FilePath /tmp/deployment-service.service -Encoding utf8; sudo mv /tmp/deployment-service.service /etc/systemd/system/deployment-service.service; sudo chmod 644 /etc/systemd/system/deployment-service.service; sudo systemctl daemon-reload; sudo journalctl --rotate; sudo journalctl --vacuum-size=1K -u deployment-service || true; sudo systemctl enable --now deployment-service; sudo systemctl status deployment-service --no-pager -l; sudo journalctl -u deployment-service --no-pager -n 200; sudo cat /etc/systemd/system/deployment-service.service";
                var (exit, outp, err) = await Task.Run(() => sb.RunCommand($"pwsh -NoProfile -NonInteractive -EncodedCommand {Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psSvc))}"));
                if (exit == 0)
                {
                    Console.WriteLine("Systemd unit installed and started.");
                    Console.WriteLine(outp);
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"Failed to install systemd unit (exit={exit}). stdout: {outp} stderr: {err}");
                    return 3;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Install-systemd-unit failed: " + ex.Message);
                return 2;
            }
        }

        // Option: Check a remote systemd service
        if (checkService)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyPath))
            {
                Console.Error.WriteLine("Error: --ssh-host, --ssh-user and --ssh-key (or configured defaults) are required for --check-service.");
                return 2;
            }

            var svc = string.IsNullOrWhiteSpace(serviceName) ? "deployment-service" : serviceName;
            Console.WriteLine($"Checking remote systemd service '{svc}' on {host}...");
            try
            {
                var sbCheck = new SshBootstrapper(host!, user!, keyPath!, effectivePort);
                var (exit, output, err) = await Task.Run(() => sbCheck.RunCommand($"systemctl is-active {svc}"));
                if (exit == 0)
                {
                    Console.WriteLine($"Service '{svc}' is active (output: {output?.Trim()}).");
                    // If pwsh is available, try to invoke the local /status via PowerShell to validate the app responds
                    try
                    {
                        // Build a small PowerShell script and use -EncodedCommand to avoid complex shell quoting issues
                        // PowerShell Core (pwsh) does not support -UseBasicParsing; remove it so Invoke-RestMethod works on pwsh 7+
                        // Prefer writing a simple plain-text failure indicator instead of Write-Error (avoids CLIXML output over SSH)
                        var psScript = "try { $r = Invoke-RestMethod -Uri 'http://localhost:5001/status'; $r | ConvertTo-Json -Compress; } catch { Write-Output 'invoke-failed'; exit 4 }";
                        var psaBytes = System.Text.Encoding.Unicode.GetBytes(psScript);
                        var psaB64 = Convert.ToBase64String(psaBytes);
                        // Execute pwsh directly on the remote host (no bash wrapper)
                        var checkCmd = $"pwsh -NoProfile -NonInteractive -EncodedCommand {psaB64}";
                        var (iExit, iOut, iErr) = await Task.Run(() => sbCheck.RunCommand(checkCmd));
                        if (iExit == 0)
                        {
                            Console.WriteLine("Remote /status response: " + (iOut ?? string.Empty).Trim());
                        }
                        else
                        {
                            Console.WriteLine($"Remote /status check failed (exit={iExit}). Output: {iOut} Error: {iErr}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Invoke-RestMethod test threw: " + ex.Message);
                    }

                    return 0;
                }
                else
                {
                    Console.WriteLine($"Service '{svc}' is not active (exit={exit}). Output: {output}");
                    return 3;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to query systemd status: " + ex.Message);
                return 2;
            }

        }

        // Option: Clear journalctl logs for the named service on the remote host
            if (clearJournal)
            {
                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyPath))
                {
                    Console.Error.WriteLine("Error: --ssh-host, --ssh-user and --ssh-key (or configured defaults) are required for --clear-journal.");
                    return 2;
                }

                var svc = string.IsNullOrWhiteSpace(serviceName) ? "deployment-service" : serviceName;
                Console.WriteLine($"Clearing journalctl logs for service '{svc}' on {host} (requires sudo)...");
                try
                {
                    var sbClear = new SshBootstrapper(host!, user!, keyPath!, effectivePort);

                    // rotate logs and vacuum entries for the service so recent journal is removed
                    // use a very small vacuum-time to purge existing entries (1s)
                    var cmd = $"sudo journalctl --rotate && sudo journalctl --vacuum-time=1s -u {svc}";
                    var (cExit, cOut, cErr) = await Task.Run(() => sbClear.RunCommand(cmd));
                    if (cExit == 0)
                    {
                        Console.WriteLine($"Journal cleared for service '{svc}'. Output: {cOut}");
                        return 0;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to clear journal (exit={cExit}). stdout: {cOut} stderr: {cErr}");
                        return 4;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Clearing journal failed: " + ex.Message);
                    return 2;
                }
            }


        // Verify (test) private key flow
        if (doVerify)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(keyPath))
            {
                Console.Error.WriteLine("Error: --ssh-host, --ssh-user and --ssh-key (or configured defaults) are required for --verify-private-key.");
                return 2;
            }

            Console.WriteLine($"Testing private key file: {keyPath}");
            try
            {
                var sbTest = new SshBootstrapper(host!, user!, keyPath!, effectivePort);
                var (exit, output, err) = await Task.Run(() => sbTest.RunCommand("echo hello-asionyx-keytest"));
                if (exit == 0)
                {
                    Console.WriteLine($"Success: key file '{keyPath}' authenticated successfully (raw key).");
                    try
                    {
                        var (hExit, hostOut, hostErr) = await Task.Run(() => sbTest.RunCommand("hostname"));
                        if (hExit == 0)
                        {
                            var remoteHost = (hostOut ?? string.Empty).Trim();
                            Console.WriteLine($"Remote hostname: {remoteHost}");
                        }
                        else
                        {
                            Console.WriteLine($"Remote hostname (raw output): {hostOut}");
                        }
                    }
                    catch (Exception exHost)
                    {
                        Console.WriteLine("Failed to query remote hostname: " + exHost.Message);
                    }

                    return 0;
                }
                else
                {
                    Console.WriteLine($"Raw key test failed (exit={exit}). Error: {err}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Raw key test threw:");
                Console.WriteLine(ex.ToString());
            }

            Console.Error.WriteLine("Private key authentication failed. See messages above.");
            return 1;
        }

    // Default (publish) flow: use configured settings only (no CLI overrides allowed for publish)
        try
        {
            var sb = new SshBootstrapper(_options.Host, _options.User, _options.KeyPath, _options.Port);
            Console.WriteLine("Pre-deploy checks: verifying remote directory ownership and appsettings.json presence...");
            // Deploy into /opt so the systemd unit can point to a stable location
            var remoteDeployDir = $"/opt/Asionyx.Service.Deployment.Linux";

            try
            {
                // Check ownership of the deploy directory
                var (statExit, statOut, statErr) = await Task.Run(() => sb.RunCommand($"stat -c \"%U:%G\" {remoteDeployDir}"));
                var desiredOwner = $"{_options.User}:{_options.User}";
                if (statExit == 0)
                {
                    var owner = (statOut ?? string.Empty).Trim();
                    if (!string.Equals(owner, desiredOwner, StringComparison.Ordinal))
                    {
                        Console.WriteLine($"Remote deploy dir owner is '{owner}', expected '{desiredOwner}'. Attempting to fix using sudo...");
                        await Task.Run(() => sb.RunCommand($"sudo mkdir -p {remoteDeployDir}"));
                        await Task.Run(() => sb.RunCommand($"sudo chown -R {_options.User}:{_options.User} {remoteDeployDir}"));
                        await Task.Run(() => sb.RunCommand($"sudo chmod -R u+rwX {remoteDeployDir}"));
                        var (stat2Exit, stat2Out, stat2Err) = await Task.Run(() => sb.RunCommand($"stat -c \"%U:%G\" {remoteDeployDir}"));
                        var owner2 = (stat2Out ?? string.Empty).Trim();
                        if (stat2Exit != 0 || !string.Equals(owner2, desiredOwner, StringComparison.Ordinal))
                        {
                            Console.Error.WriteLine($"Failed to ensure correct ownership for {remoteDeployDir}. Owner is '{owner2}'. Aborting deploy.");
                            return 1;
                        }
                        else
                        {
                            Console.WriteLine("Ownership fixed.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Remote deploy dir ownership OK: {owner}");
                    }
                }
                else
                {
                    // Directory missing or stat failed: try to create and set perms
                    Console.WriteLine($"Remote deploy dir appears missing or inaccessible (stat exit={statExit}). Creating and setting permissions via sudo...");
                    await Task.Run(() => sb.RunCommand($"sudo mkdir -p {remoteDeployDir}"));
                    await Task.Run(() => sb.RunCommand($"sudo chown -R {_options.User}:{_options.User} {remoteDeployDir}"));
                    await Task.Run(() => sb.RunCommand($"sudo chmod -R u+rwX {remoteDeployDir}"));
                    var (stat3Exit, stat3Out, stat3Err) = await Task.Run(() => sb.RunCommand($"stat -c \"%U:%G\" {remoteDeployDir}"));
                    var owner3 = (stat3Out ?? string.Empty).Trim();
                    if (stat3Exit != 0 || !string.Equals(owner3, desiredOwner, StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Failed to create or set ownership for {remoteDeployDir}. Aborting deploy.");
                        return 1;
                    }
                }

                // Check for existing appsettings.json and back it up if present
                var (cfgExit, cfgOut, cfgErr) = await Task.Run(() => sb.RunCommand($"if [ -f {remoteDeployDir}/appsettings.json ]; then echo exists; else echo missing; fi"));
                if (cfgExit == 0 && (cfgOut ?? string.Empty).Trim().Equals("exists", StringComparison.OrdinalIgnoreCase))
                {
                    var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    var backupPath = $"{remoteDeployDir}/appsettings.json.bak.{ts}";
                    Console.WriteLine($"Remote appsettings.json detected at {remoteDeployDir}. Backing up to {backupPath} (using sudo)...");
                    var (mvExit, mvOut, mvErr) = await Task.Run(() => sb.RunCommand($"sudo mv {remoteDeployDir}/appsettings.json {backupPath}"));
                    if (mvExit == 0)
                    {
                        Console.WriteLine("Remote appsettings.json backed up successfully.");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to back up remote appsettings.json (exit={mvExit}). Output: {mvOut} Error: {mvErr}");
                    }
                }
                else
                {
                    Console.WriteLine("No existing remote appsettings.json found.");
                }
                // Ensure the user's data directory exists in their home (use ~). This is where the service will store Asionyx.Service.Deployment.Linux.json and other data.
                try
                {
                    var userDataDir = "~/.Asionyx.Service.Deployment.Linux";
                    // Attempt to create under the user's home; if that fails, fall back to /home/{user}/.Asionyx.Service.Deployment.Linux with sudo
                    var (mkExit, mkOut, mkErr) = await Task.Run(() => sb.RunCommand($"mkdir -p {userDataDir} && chmod 700 {userDataDir}"));
                    if (mkExit != 0)
                    {
                        Console.WriteLine($"Creating user data dir with normal user failed (exit={mkExit}), attempting with sudo.");
                        await Task.Run(() => sb.RunCommand($"sudo mkdir -p /home/{_options.User}/.Asionyx.Service.Deployment.Linux"));
                        await Task.Run(() => sb.RunCommand($"sudo chown -R {_options.User}:{_options.User} /home/{_options.User}/.Asionyx.Service.Deployment.Linux"));
                        await Task.Run(() => sb.RunCommand($"sudo chmod 700 /home/{_options.User}/.Asionyx.Service.Deployment.Linux"));
                    }
                    else
                    {
                        Console.WriteLine("User data directory ensured: " + userDataDir);
                    }
                }
                catch (Exception exDataDir)
                {
                    Console.WriteLine("Warning: failed to ensure user data directory: " + exDataDir.Message);
                }
            }
            catch (Exception exPre)
            {
                Console.WriteLine("Pre-deploy checks failed: " + exPre.Message);
                return 2;
            }

            Console.WriteLine("Uploading publish directory...");
            await Task.Run(() => sb.UploadDirectory(_options.PublishDir, remoteDeployDir));
            // Do not run arbitrary install.sh on the remote host; instead ensure the deployed files are present and ownership is correct
            Console.WriteLine("Checking deployed files and fixing ownership where necessary...");
            try
            {
                var checkDeploy = $"if (Test-Path -Path '{remoteDeployDir}/Asionyx.Service.Deployment.Linux.dll') {{ Write-Output 'deployed-file-found'; }} else {{ Write-Output 'deployed-file-missing'; exit 5 }}";
                var checkB = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(checkDeploy));
                var (dExit, dOut, dErr) = await Task.Run(() => sb.RunCommand($"pwsh -NoProfile -NonInteractive -EncodedCommand {checkB}"));
                Console.WriteLine($"Deployed-file check exit={dExit}, output={dOut}, err={dErr}");

                // Ensure the deployment directory and files are owned by the deploy user and are readable
                var fixPerms = $"try {{ sudo mkdir -p {remoteDeployDir}; sudo chown -R { _options.User }:{ _options.User } {remoteDeployDir}; sudo chmod -R u+rwX {remoteDeployDir}; Write-Output 'perms-ok'; }} catch {{ Write-Output 'perms-failed'; exit 6 }}";
                var fixB = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(fixPerms));
                var (fExit, fOut, fErr) = await Task.Run(() => sb.RunCommand($"pwsh -NoProfile -NonInteractive -EncodedCommand {fixB}"));
                Console.WriteLine($"ownership/permissions exit={fExit}, output={fOut}, err={fErr}");
            }
            catch (Exception exCheck)
            {
                Console.WriteLine("Deployed-file/permission check threw: " + exCheck.Message);
            }
            // After upload/install, create or replace the systemd unit, reload daemon, enable+start, then collect status and journal logs for troubleshooting
            int sExit = -1;
            string sOut = null;
            string sErr = null;
            try
            {
                Console.WriteLine("Creating/updating systemd unit and attempting to enable/start service (requires sudo)...");

                // Run the published dotnet app (framework-dependent) from /opt so the systemd unit is stable and does not reference run.sh
                // Include an environment variable pointing to the user's data folder under %h so the service can find its data (Asionyx.Service.Deployment.Linux.json)
                var unit = $"[Unit]\nDescription=Asionyx Deployment Service\nAfter=network.target\nStartLimitBurst=3\nStartLimitIntervalSec=60\n\n[Service]\nWorkingDirectory=/opt/Asionyx.Service.Deployment.Linux\nExecStart=/usr/bin/dotnet /opt/Asionyx.Service.Deployment.Linux/Asionyx.Service.Deployment.Linux.dll\nRestart=on-failure\nRestartSec=5\nUser={_options.User}\n\n[Install]\nWantedBy=multi-user.target";

                    // Build a script that writes the unit file, reloads systemd, clears existing journal entries for the unit, then enables+starts the service
                    var psSvc = $"$unit = @'\n{unit}\n'@; $unit | Out-File -FilePath /tmp/deployment-service.service -Encoding utf8; sudo mv /tmp/deployment-service.service /etc/systemd/system/deployment-service.service; sudo chmod 644 /etc/systemd/system/deployment-service.service; sudo systemctl daemon-reload; sudo journalctl --rotate; sudo journalctl --vacuum-size=1K -u deployment-service || true; sudo systemctl enable --now deployment-service; sudo systemctl status deployment-service --no-pager -l; sudo journalctl -u deployment-service --no-pager -n 200; sudo cat /etc/systemd/system/deployment-service.service";
                    var psSvcB = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psSvc));
                    (sExit, sOut, sErr) = await Task.Run(() => sb.RunCommand($"pwsh -NoProfile -NonInteractive -EncodedCommand {psSvcB}"));
                Console.WriteLine("--- service enable/start output ---");
                Console.WriteLine($"Exit={sExit}");
                if (!string.IsNullOrWhiteSpace(sOut)) Console.WriteLine("STDOUT:\n" + sOut);
                if (!string.IsNullOrWhiteSpace(sErr)) Console.WriteLine("STDERR:\n" + sErr);
                Console.WriteLine("--- end service output ---");
            }
            catch (Exception exSvc)
            {
                Console.WriteLine("Service management failed: " + exSvc.Message);
            }
            return sExit == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Bootstrap failed:");
            Console.Error.WriteLine(ex.ToString());
            return 2;
        }
    }
}
