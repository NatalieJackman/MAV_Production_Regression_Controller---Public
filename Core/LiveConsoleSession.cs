using Crestron.SimplSharp.Ssh;
using Crestron.SimplSharpPro.CrestronThread;
using System;

namespace MAV.ProductionRegressionController
{
    public sealed class LiveConsoleSession
    {
        private readonly TargetConsoleTranscript _console;
        private readonly object _sync = new object();
        private SshClient _client;
        private ShellStream _shell;
        private Thread _reader;
        private volatile bool _run;
        private string _target;

        public LiveConsoleSession(TargetConsoleTranscript console) { _console = console; }

        public bool Connected
        {
            get
            {
                lock (_sync) return _run && _client != null && _client.IsConnected && _shell != null;
            }
        }
        public string Target { get { lock (_sync) return _target; } }

        public void Connect(TargetSettings target)
        {
            if (target == null) throw new ArgumentNullException("target");
            Disconnect();
            SshClient client = null;
            ShellStream shell = null;
            try
            {
                client = new SshClient(target.Address, target.Username, target.Password);
                client.Connect();
                shell = client.CreateShellStream("xterm", 120, 36, 1200, 800, 16384);
                lock (_sync)
                {
                    _client = client;
                    _shell = shell;
                    _target = target.Address;
                    _run = true;
                }
                _console.System("Live SSH console connected to " + target.Address + ".");
                _reader = new Thread(ReadLoop, null, Thread.eThreadStartOptions.Running);
                client = null;
                shell = null;
            }
            finally
            {
                if (shell != null) try { shell.Dispose(); } catch { }
                if (client != null) { try { if (client.IsConnected) client.Disconnect(); } catch { } try { client.Dispose(); } catch { } }
            }
        }

        public void Disconnect()
        {
            SshClient client = null;
            ShellStream shell = null;
            lock (_sync)
            {
                _run = false;
                client = _client;
                shell = _shell;
                _client = null;
                _shell = null;
                _target = null;
            }
            if (shell != null) try { shell.Dispose(); } catch { }
            if (client != null) { try { if (client.IsConnected) client.Disconnect(); } catch { } try { client.Dispose(); } catch { } }
            if (client != null || shell != null) _console.System("Live SSH console disconnected.");
        }

        public void Send(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("Console command is required.");
            lock (_sync)
            {
                if (!_run || _shell == null || _client == null || !_client.IsConnected) throw new InvalidOperationException("Live target console is not connected.");
                _console.Command(command);
                _shell.WriteLine(command);
            }
        }

        private object ReadLoop(object ignored)
        {
            try
            {
                while (_run)
                {
                    ShellStream shell;
                    lock (_sync) shell = _shell;
                    if (shell == null) break;
                    if (shell.DataAvailable)
                    {
                        string data = shell.Read();
                        if (!string.IsNullOrEmpty(data)) _console.Output(data);
                    }
                    else Thread.Sleep(80);
                }
            }
            catch (Exception ex)
            {
                if (_run) _console.System("Live console reader stopped: " + ex.Message);
            }
            finally
            {
                if (_run)
                {
                    lock (_sync) _run = false;
                }
            }
            return null;
        }
    }
}
