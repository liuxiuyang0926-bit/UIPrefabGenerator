#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Lxy.UIEffectGenerator.Editor
{
    internal sealed class UIEffectCodexRequest
    {
        /// <summary>
        /// 公开的provider数据。
        /// </summary>
        public string provider = "Codex";
        /// <summary>
        /// 公开的codexCommand数据。
        /// </summary>
        public string codexCommand = "codex";
        /// <summary>
        /// 公开的project根节点数据。
        /// </summary>
        public string projectRoot = string.Empty;
        /// <summary>
        /// 公开的图片路径数据。
        /// </summary>
        public string imagePath = string.Empty;
        /// <summary>
        /// 公开的图片路径数据。
        /// </summary>
        public string[] imagePaths = Array.Empty<string>();
        /// <summary>
        /// 公开的prompt数据。
        /// </summary>
        public string prompt = string.Empty;
        /// <summary>
        /// 公开的输出Schema路径数据。
        /// </summary>
        public string outputSchemaPath = string.Empty;
        /// <summary>
        /// 公开的输出路径数据。
        /// </summary>
        public string outputPath = string.Empty;
        /// <summary>
        /// 公开的requiresFigmaMcp数据。
        /// </summary>
        public bool requiresFigmaMcp;
        /// <summary>
        /// 公开的requiresUnityMcp数据。
        /// </summary>
        public bool requiresUnityMcp;
        /// <summary>
        /// 公开的允许WorkspaceWrite数据。
        /// </summary>
        public bool allowWorkspaceWrite;
        /// <summary>
        /// 公开的unityMcp服务器路径数据。
        /// </summary>
        public string unityMcpServerPath = string.Empty;
        /// <summary>
        /// 公开的unityMcpNodeCommand数据。
        /// </summary>
        public string unityMcpNodeCommand = "node";
        /// <summary>
        /// 公开的unityBridgePort数据。
        /// </summary>
        public int unityBridgePort;
        /// <summary>
        /// 公开的reasoningEffort数据。
        /// </summary>
        public string reasoningEffort = "high";
    }

    internal sealed class UIEffectCodexRunResult
    {
        /// <summary>
        /// 公开的成功数据。
        /// </summary>
        public bool succeeded;
        /// <summary>
        /// 公开的canceled数据。
        /// </summary>
        public bool canceled;
        /// <summary>
        /// 公开的exitCode数据。
        /// </summary>
        public int exitCode;
        /// <summary>
        /// 公开的输出Json数据。
        /// </summary>
        public string outputJson = string.Empty;
        /// <summary>
        /// 公开的错误数据。
        /// </summary>
        public string error = string.Empty;
        /// <summary>
        /// 公开的usage数据。
        /// </summary>
        public UIEffectCodexUsage usage;
    }

    [Serializable]
    internal sealed class UIEffectCodexUsage
    {
        /// <summary>
        /// 公开的输入tokens数据。
        /// </summary>
        public long input_tokens;
        /// <summary>
        /// 公开的cached输入tokens数据。
        /// </summary>
        public long cached_input_tokens;
        /// <summary>
        /// 公开的输出tokens数据。
        /// </summary>
        public long output_tokens;
        /// <summary>
        /// 公开的reasoning输出tokens数据。
        /// </summary>
        public long reasoning_output_tokens;

        /// <summary>
        /// 向调用方提供总数Tokens。
        /// </summary>
        public long TotalTokens => input_tokens + output_tokens;
    }

    /// <summary>
    /// Runs a configured AI CLI without opening a terminal window. Local image
    /// analysis is read-only and explicitly disables unrelated MCP servers;
    /// callers that genuinely need Figma or Unity MCP opt in per request.
    /// </summary>
    internal sealed class UIEffectCodexRunner : IDisposable
    {
        private const int MaximumErrorLength = 32768;

        private readonly ConcurrentQueue<string> progressMessages =
            new ConcurrentQueue<string>();
        private readonly object stateLock = new object();
        private readonly StringBuilder errorOutput = new StringBuilder();

        private Process process;
        private UIEffectCodexRequest request;
        private UIEffectCodexRunResult completedResult;
        private int isRunning;
        private int completionAvailable;
        private int cancelRequested;
        private int exitHandled;
        private bool disposed;
        private UIEffectCodexUsage usage;

        [Serializable]
        private sealed class CodexEvent
        {
            /// <summary>
            /// 公开的类型数据。
            /// </summary>
            public string type;
            /// <summary>
            /// 公开的usage数据。
            /// </summary>
            public UIEffectCodexUsage usage;
        }

        /// <summary>
        /// 指示当前对象是否正在运行。
        /// </summary>
        public bool IsRunning =>
            Interlocked.CompareExchange(ref isRunning, 0, 0) == 1;

        /// <summary>
        /// 启动组件的运行流程。
        /// </summary>
        public void Start(UIEffectCodexRequest runRequest)
        {
            if (runRequest == null)
            {
                throw new ArgumentNullException(nameof(runRequest));
            }

            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(UIEffectCodexRunner));
            }

            if (IsRunning)
            {
                throw new InvalidOperationException(
                    "已有 Codex UI 分析任务正在运行。");
            }

            ValidateRequest(runRequest);
            string executable = ResolveExecutable(runRequest.codexCommand);
            if (runRequest.requiresFigmaMcp)
            {
                ValidateFigmaMcpConfiguration(
                    executable,
                    runRequest);
            }
            if (runRequest.requiresUnityMcp)
            {
                ValidateUnityMcpConfiguration(runRequest);
            }
            Directory.CreateDirectory(
                Path.GetDirectoryName(runRequest.outputSchemaPath));
            Directory.CreateDirectory(
                Path.GetDirectoryName(runRequest.outputPath));

            request = runRequest;
            completedResult = null;
            usage = null;
            errorOutput.Clear();
            while (progressMessages.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref completionAvailable, 0);
            Interlocked.Exchange(ref cancelRequested, 0);
            Interlocked.Exchange(ref exitHandled, 0);
            Interlocked.Exchange(ref isRunning, 1);

            ProcessStartInfo startInfo = CreateStartInfo(
                executable,
                runRequest);
            process = new Process
            {
                StartInfo = startInfo,
                // Attach Exited only after the stdin request has been written.
                // Otherwise a CLI bootstrap failure races the pipe write and
                // Unity only reports Win32 IO 232 instead of the real stderr.
                EnableRaisingEvents = false,
            };
            process.OutputDataReceived += HandleOutputDataReceived;
            process.ErrorDataReceived += HandleErrorDataReceived;

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "无法启动 Codex 后台进程。");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.StandardInput.Write(runRequest.prompt);
                process.StandardInput.Close();
                process.Exited += HandleProcessExited;
                process.EnableRaisingEvents = true;
                if (process.HasExited)
                {
                    HandleProcessExited(process, EventArgs.Empty);
                }
                progressMessages.Enqueue(
                    runRequest.provider +
                    " 已在后台启动，正在读取设计输入…");
            }
            catch (Exception exception)
            {
                Exception startupException = BuildStartupException(exception);
                Interlocked.Exchange(ref isRunning, 0);
                DisposeProcess();
                throw startupException;
            }
        }

        /// <summary>
        /// 尝试Dequeue进度，并返回是否成功。
        /// </summary>
        public bool TryDequeueProgress(out string message)
        {
            return progressMessages.TryDequeue(out message);
        }

        /// <summary>
        /// 尝试Take结果，并返回是否成功。
        /// </summary>
        public bool TryTakeResult(out UIEffectCodexRunResult result)
        {
            result = null;
            if (Interlocked.CompareExchange(
                    ref completionAvailable,
                    0,
                    1) != 1)
            {
                return false;
            }

            lock (stateLock)
            {
                result = completedResult;
                completedResult = null;
            }

            DisposeProcess();
            return true;
        }

        /// <summary>
        /// 执行取消相关逻辑。
        /// </summary>
        public void Cancel()
        {
            if (!IsRunning)
            {
                return;
            }

            Interlocked.Exchange(ref cancelRequested, 1);
            progressMessages.Enqueue("正在取消 Codex 分析任务…");

            Process current = process;
            if (current == null)
            {
                return;
            }

            try
            {
                if (current.HasExited)
                {
                    return;
                }

#if UNITY_EDITOR_WIN
                var killInfo = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/PID {current.Id} /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using (Process killer = Process.Start(killInfo))
                {
                    killer?.WaitForExit(3000);
                }
#else
                current.Kill();
#endif
            }
            catch (Exception exception)
            {
                AppendError(exception.Message);
                try
                {
                    if (!current.HasExited)
                    {
                        current.Kill();
                    }
                }
                catch
                {
                    // The process may already have exited between checks.
                }
            }
        }

        /// <summary>
        /// 释放当前实例持有的资源。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Cancel();
            DisposeProcess();
        }

        /// <summary>
        /// 解析Executable。
        /// </summary>
        internal static string ResolveExecutable(string command)
        {
            string value = (command ?? string.Empty)
                .Trim()
                .Trim('"');
            if (value.Length == 0 ||
                value.IndexOfAny(new[] { '\r', '\n', '|', '&', '<', '>' }) >= 0)
            {
                throw new InvalidOperationException(
                    "Codex 命令无效。请填写 codex 或 Codex 可执行文件路径。");
            }

            if (File.Exists(value))
            {
                return Path.GetFullPath(value);
            }

            if (value.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                value.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                throw new FileNotFoundException(
                    "找不到 Codex 命令。",
                    value);
            }

            string pathValue =
                Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] extensions = IsWindows()
                ? new[] { ".exe", ".cmd", ".bat", ".ps1", string.Empty }
                : new[] { string.Empty };
            foreach (string folder in pathValue.Split(Path.PathSeparator))
            {
                string trimmedFolder = folder.Trim().Trim('"');
                if (trimmedFolder.Length == 0)
                {
                    continue;
                }

                foreach (string extension in extensions)
                {
                    string candidate = Path.Combine(
                        trimmedFolder,
                        value + extension);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }

            throw new FileNotFoundException(
                "找不到 Codex 命令。请确认已安装 Codex CLI，或在窗口中填写 codex.cmd/codex.exe 的完整路径。",
                value);
        }

        /// <summary>
        /// 创建启动信息。
        /// </summary>
        private static ProcessStartInfo CreateStartInfo(
            string executable,
            UIEffectCodexRequest runRequest)
        {
            var argumentParts = new List<string>
            {
                "exec",
                "--skip-git-repo-check",
                "--json",
                "--ephemeral",
            };
            if (runRequest.allowWorkspaceWrite)
            {
                argumentParts.Add("--approve-for-me");
            }
            else
            {
                argumentParts.Add("--sandbox");
                argumentParts.Add("read-only");
            }

            argumentParts.Add("--cd");
            argumentParts.Add(QuoteArgument(runRequest.projectRoot));
            argumentParts.Add("--config");
            argumentParts.Add(QuoteArgument(
                "model_reasoning_effort=\"" +
                NormalizeReasoningEffort(runRequest.reasoningEffort) +
                "\""));
            if (!runRequest.requiresUnityMcp &&
                !runRequest.requiresFigmaMcp)
            {
                // A dotted `mcp_servers.foo.enabled=false` override replaces an
                // inherited server table in Codex CLI 0.150 and leaves it without
                // a transport. The CLI then exits before reading stdin. Replace
                // the complete map for image-only analysis instead: this is both
                // valid TOML and guarantees that no unrelated MCP catalog is
                // injected into the request.
                AddConfigArgument(argumentParts, "mcp_servers={}");
            }
            else if (runRequest.requiresUnityMcp)
            {
                AddUnityMcpArguments(argumentParts, runRequest);
            }
            else
            {
                // The user's global Codex configuration may already contain a
                // server named "unity". Explicitly disable it for image-only
                // analysis so its large tool catalog is not injected into every
                // reasoning step.
                AddDisabledMcpServer(argumentParts, "unity");
            }
            if (!runRequest.requiresFigmaMcp)
            {
                if (runRequest.requiresUnityMcp)
                {
                    AddDisabledMcpServer(argumentParts, "figma");
                }
            }

            List<string> imagePaths = GetImagePaths(runRequest);
            if (imagePaths.Count > 0)
            {
                argumentParts.Add("--image");
                foreach (string imagePath in imagePaths)
                {
                    argumentParts.Add(QuoteArgument(imagePath));
                }
            }

            argumentParts.Add("--output-schema");
            argumentParts.Add(QuoteArgument(runRequest.outputSchemaPath));
            argumentParts.Add("--output-last-message");
            argumentParts.Add(QuoteArgument(runRequest.outputPath));
            argumentParts.Add("-");
            string arguments = string.Join(" ", argumentParts);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = runRequest.projectRoot,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };

            if (string.Equals(
                    Path.GetExtension(executable),
                    ".ps1",
                    StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = IsWindows()
                    ? "powershell.exe"
                    : "pwsh";
                startInfo.Arguments = string.Join(
                    " ",
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    QuoteArgument(executable),
                    arguments);
            }
            else if (IsCommandScript(executable))
            {
                startInfo.FileName = Environment.GetEnvironmentVariable(
                    "ComSpec") ?? "cmd.exe";
                startInfo.Arguments =
                    "/d /s /c \"\"" + executable + "\" " +
                    arguments + "\"";
            }

            return startInfo;
        }

        /// <summary>
        /// 添加UnityMcp参数。
        /// </summary>
        private static void AddUnityMcpArguments(
            List<string> argumentParts,
            UIEffectCodexRequest runRequest)
        {
            string nodeCommand = NormalizeTomlPath(
                ResolveExecutable(runRequest.unityMcpNodeCommand));
            string serverPath = NormalizeTomlPath(
                Path.GetFullPath(runRequest.unityMcpServerPath));
            AddConfigArgument(
                argumentParts,
                "mcp_servers.unity.command=\"" +
                EscapeTomlString(nodeCommand) + "\"");
            AddConfigArgument(
                argumentParts,
                "mcp_servers.unity.args=[\"" +
                EscapeTomlString(serverPath) + "\"]");
            AddConfigArgument(
                argumentParts,
                "mcp_servers.unity.env.UNITY_BRIDGE_PORT=\"" +
                runRequest.unityBridgePort + "\"");
            AddConfigArgument(
                argumentParts,
                "mcp_servers.unity.startup_timeout_sec=120");
            AddConfigArgument(
                argumentParts,
                "mcp_servers.unity.tool_timeout_sec=300");
        }

        /// <summary>
        /// 添加配置参数。
        /// </summary>
        private static void AddConfigArgument(
            List<string> argumentParts,
            string value)
        {
            argumentParts.Add("--config");
            argumentParts.Add(QuoteArgument(value));
        }

        /// <summary>
        /// 添加DisabledMcp服务器。
        /// </summary>
        private static void AddDisabledMcpServer(
            List<string> argumentParts,
            string serverName)
        {
            // A disabled server still needs a syntactically complete transport
            // in current Codex CLI versions. It is never started because enabled
            // is false; the placeholder only prevents bootstrap validation from
            // rejecting a partial table.
            AddConfigArgument(
                argumentParts,
                "mcp_servers." + serverName +
                "={enabled=false,command=\"codex\",args=[\"--version\"]}");
        }

        /// <summary>
        /// 执行规范化ReasoningEffort相关逻辑。
        /// </summary>
        private static string NormalizeReasoningEffort(string value)
        {
            string normalized = (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
            switch (normalized)
            {
                case "low":
                case "medium":
                case "high":
                case "xhigh":
                    return normalized;
                default:
                    return "high";
            }
        }

        /// <summary>
        /// 执行规范化Toml路径相关逻辑。
        /// </summary>
        private static string NormalizeTomlPath(string value)
        {
            return (value ?? string.Empty).Replace('\\', '/');
        }

        /// <summary>
        /// 执行转义Toml字符串相关逻辑。
        /// </summary>
        private static string EscapeTomlString(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        /// <summary>
        /// 执行判断是否Command脚本相关逻辑。
        /// </summary>
        private static bool IsCommandScript(string executable)
        {
            string extension = Path.GetExtension(executable);
            return string.Equals(
                       extension,
                       ".cmd",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       extension,
                       ".bat",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取图片Paths。
        /// </summary>
        private static List<string> GetImagePaths(
            UIEffectCodexRequest runRequest)
        {
            var paths = new List<string>();
            if (runRequest.imagePaths != null)
            {
                foreach (string path in runRequest.imagePaths)
                {
                    if (!string.IsNullOrWhiteSpace(path) &&
                        !paths.Contains(path))
                    {
                        paths.Add(path);
                    }
                }
            }

            if (paths.Count == 0 &&
                !string.IsNullOrWhiteSpace(runRequest.imagePath))
            {
                paths.Add(runRequest.imagePath);
            }

            return paths;
        }

        /// <summary>
        /// 处理输出数据Received。
        /// </summary>
        private void HandleOutputDataReceived(
            object sender,
            DataReceivedEventArgs eventArgs)
        {
            string line = eventArgs.Data;
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            string progress = GetProgressMessage(line);
            TryCaptureUsage(line);
            if (!string.IsNullOrWhiteSpace(progress))
            {
                progressMessages.Enqueue(progress);
            }
        }

        /// <summary>
        /// 处理错误数据Received。
        /// </summary>
        private void HandleErrorDataReceived(
            object sender,
            DataReceivedEventArgs eventArgs)
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                AppendError(eventArgs.Data);
            }
        }

        /// <summary>
        /// 处理ProcessExited。
        /// </summary>
        private void HandleProcessExited(object sender, EventArgs eventArgs)
        {
            if (Interlocked.CompareExchange(ref exitHandled, 1, 0) != 0)
            {
                return;
            }

            int exitCode = -1;
            try
            {
                process?.WaitForExit();
                if (process != null)
                {
                    exitCode = process.ExitCode;
                }
            }
            catch (Exception exception)
            {
                AppendError(exception.Message);
            }

            bool wasCanceled = Interlocked.CompareExchange(
                                   ref cancelRequested,
                                   0,
                                   0) == 1;
            string outputJson = string.Empty;
            if (!wasCanceled &&
                request != null &&
                File.Exists(request.outputPath))
            {
                try
                {
                    outputJson = File.ReadAllText(
                        request.outputPath,
                        Encoding.UTF8);
                }
                catch (Exception exception)
                {
                    AppendError(exception.Message);
                }
            }

            string error;
            lock (stateLock)
            {
                error = errorOutput.ToString().Trim();
                completedResult = new UIEffectCodexRunResult
                {
                    succeeded =
                        !wasCanceled &&
                        exitCode == 0 &&
                        !string.IsNullOrWhiteSpace(outputJson),
                    canceled = wasCanceled,
                    exitCode = exitCode,
                    outputJson = outputJson,
                    error = error,
                    usage = usage,
                };
            }

            Interlocked.Exchange(ref isRunning, 0);
            Interlocked.Exchange(ref completionAvailable, 1);
        }

        /// <summary>
        /// 构建启动异常。
        /// </summary>
        private Exception BuildStartupException(Exception cause)
        {
            Process current = process;
            int exitCode = -1;
            bool exited = false;
            try
            {
                if (current != null)
                {
                    exited = current.WaitForExit(3000);
                    if (exited)
                    {
                        // Parameterless WaitForExit waits for redirected async
                        // output handlers to flush after the native process exits.
                        current.WaitForExit();
                        exitCode = current.ExitCode;
                    }
                }
            }
            catch
            {
                // Preserve the original startup exception below.
            }

            string detail;
            lock (stateLock)
            {
                detail = errorOutput.ToString().Trim();
            }

            if (!exited && !(cause is IOException))
            {
                return cause;
            }

            string exitSummary = exitCode >= 0
                ? "（ExitCode " + exitCode + "）"
                : string.Empty;
            string cliDetail = string.IsNullOrWhiteSpace(detail)
                ? cause.Message
                : detail;
            return new InvalidOperationException(
                "Codex 后台进程在接收 UI 分析请求前退出" +
                exitSummary + "。\nCLI 详情：" + cliDetail,
                cause);
        }

        /// <summary>
        /// 校验FigmaMcp配置。
        /// </summary>
        private static void ValidateFigmaMcpConfiguration(
            string executable,
            UIEffectCodexRequest runRequest)
        {
            ProcessStartInfo startInfo = CreateMcpInspectionStartInfo(
                executable,
                runRequest.projectRoot);
            using (Process inspection = Process.Start(startInfo))
            {
                if (inspection == null)
                {
                    throw new InvalidOperationException(
                        "无法启动 Figma MCP 配置检查。");
                }

                if (!inspection.WaitForExit(5000))
                {
                    try { inspection.Kill(); } catch { }
                    throw new TimeoutException(
                        "检查 Figma MCP 配置超时。");
                }

                string output = inspection.StandardOutput.ReadToEnd();
                string error = inspection.StandardError.ReadToEnd();
                bool needsAuthentication =
                    ContainsAuthenticationFailure(output) ||
                    ContainsAuthenticationFailure(error);
                if (inspection.ExitCode == 0 && !needsAuthentication)
                {
                    return;
                }

                const string provider = "Codex";
                const string setup =
                    "codex mcp add figma --url " +
                    "https://mcp.figma.com/mcp，然后执行 " +
                    "codex mcp login figma 完成 OAuth 授权";
                string detail = string.IsNullOrWhiteSpace(error)
                    ? output
                    : error;
                throw new InvalidOperationException(
                    (needsAuthentication
                        ? provider +
                          " CLI 已配置 Figma MCP，但尚未完成 OAuth。"
                        : provider +
                          " CLI 未配置名为 figma 的官方 MCP。") +
                    "请执行：" + setup + "。" +
                    (string.IsNullOrWhiteSpace(detail)
                        ? string.Empty
                        : "\nCLI 详情：" + detail.Trim()));
            }
        }

        /// <summary>
        /// 校验UnityMcp配置。
        /// </summary>
        private static void ValidateUnityMcpConfiguration(
            UIEffectCodexRequest runRequest)
        {
            if (!runRequest.allowWorkspaceWrite)
            {
                throw new InvalidOperationException(
                    "UnityMCP 直建模式必须启用项目 workspace-write 沙箱。");
            }

            if (runRequest.unityBridgePort < 1 ||
                runRequest.unityBridgePort > 65535)
            {
                throw new InvalidOperationException(
                    "当前 UnityMCP Bridge 未运行或端口无效。" +
                    "请在 Window/AB Unity MCP 中启动 Server。");
            }

            if (string.IsNullOrWhiteSpace(
                    runRequest.unityMcpServerPath) ||
                !File.Exists(runRequest.unityMcpServerPath))
            {
                throw new FileNotFoundException(
                    "找不到 Unity MCP Server 的 src/index.js。" +
                    "请在生成窗口的 Codex 设置中选择它。",
                    runRequest.unityMcpServerPath);
            }

            ResolveExecutable(runRequest.unityMcpNodeCommand);
        }

        /// <summary>
        /// 执行判断是否包含AuthenticationFailure相关逻辑。
        /// </summary>
        private static bool ContainsAuthenticationFailure(string value)
        {
            string text = value ?? string.Empty;
            return text.IndexOf(
                       "needs authentication",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf(
                       "authentication required",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf(
                       "not authenticated",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf(
                       "oauth required",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 创建McpInspection启动信息。
        /// </summary>
        private static ProcessStartInfo CreateMcpInspectionStartInfo(
            string executable,
            string projectRoot)
        {
            const string arguments = "mcp get figma";
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };

            if (string.Equals(
                    Path.GetExtension(executable),
                    ".ps1",
                    StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = IsWindows()
                    ? "powershell.exe"
                    : "pwsh";
                startInfo.Arguments = string.Join(
                    " ",
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    QuoteArgument(executable),
                    arguments);
            }
            else if (IsCommandScript(executable))
            {
                startInfo.FileName = Environment.GetEnvironmentVariable(
                    "ComSpec") ?? "cmd.exe";
                startInfo.Arguments =
                    "/d /s /c \"\"" + executable + "\" " +
                    arguments + "\"";
            }

            return startInfo;
        }

        /// <summary>
        /// 执行追加错误相关逻辑。
        /// </summary>
        private void AppendError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (stateLock)
            {
                if (errorOutput.Length >= MaximumErrorLength)
                {
                    return;
                }

                int remaining = MaximumErrorLength - errorOutput.Length;
                string value = message.Length <= remaining
                    ? message
                    : message.Substring(0, remaining);
                errorOutput.AppendLine(value);
            }
        }

        /// <summary>
        /// 获取进度消息。
        /// </summary>
        private string GetProgressMessage(string jsonLine)
        {
            if (jsonLine.IndexOf(
                    "\"type\":\"thread.started\"",
                    StringComparison.Ordinal) >= 0)
            {
                return request != null && request.requiresUnityMcp
                    ? "Codex 会话已建立，正在连接当前 UnityMCP…"
                    : "Codex 会话已建立，正在加载 unity-ui-generator…";
            }

            if (jsonLine.IndexOf(
                    "\"type\":\"turn.started\"",
                    StringComparison.Ordinal) >= 0)
            {
                return request != null && request.requiresUnityMcp
                    ? "正在完整分析效果图、项目 Sprite 与 Prefab 层级…"
                    : "正在读取设计输入并生成 UISchema…";
            }

            if (jsonLine.IndexOf(
                    "\"type\":\"item.started\"",
                    StringComparison.Ordinal) >= 0 &&
                jsonLine.IndexOf(
                    "command_execution",
                    StringComparison.Ordinal) >= 0)
            {
                return request != null && request.requiresUnityMcp
                    ? "正在通过 UnityMCP 创建或复核 Prefab…"
                    : "正在处理设计上下文…";
            }

            if (jsonLine.IndexOf(
                    "\"type\":\"turn.completed\"",
                    StringComparison.Ordinal) >= 0)
            {
                return request != null && request.requiresUnityMcp
                    ? "Codex 直建完成，正在接收已保存的 Prefab 路径…"
                    : "AI 分析完成，正在验证 UISchema…";
            }

            if (jsonLine.IndexOf(
                    "\"type\":\"turn.failed\"",
                    StringComparison.Ordinal) >= 0 ||
                jsonLine.IndexOf(
                    "\"type\":\"error\"",
                    StringComparison.Ordinal) >= 0)
            {
                return "Codex 分析失败，正在收集错误信息…";
            }

            return string.Empty;
        }

        /// <summary>
        /// 尝试CaptureUsage，并返回是否成功。
        /// </summary>
        private void TryCaptureUsage(string jsonLine)
        {
            if (jsonLine.IndexOf(
                    "\"type\":\"turn.completed\"",
                    StringComparison.Ordinal) < 0)
            {
                return;
            }

            try
            {
                CodexEvent codexEvent =
                    JsonUtility.FromJson<CodexEvent>(jsonLine);
                if (codexEvent?.usage != null)
                {
                    usage = codexEvent.usage;
                }
            }
            catch
            {
                // Usage is diagnostic only; never fail Prefab generation
                // because a future Codex event shape changes.
            }
        }

        /// <summary>
        /// 校验请求。
        /// </summary>
        private static void ValidateRequest(UIEffectCodexRequest value)
        {
            if (string.IsNullOrWhiteSpace(value.projectRoot) ||
                !Directory.Exists(value.projectRoot))
            {
                throw new DirectoryNotFoundException(
                    "找不到 Unity 项目根目录。");
            }

            foreach (string imagePath in GetImagePaths(value))
            {
                if (!File.Exists(imagePath))
                {
                    throw new FileNotFoundException(
                        "找不到待分析的效果图输入。",
                        imagePath);
                }
            }

            if (string.IsNullOrWhiteSpace(value.prompt))
            {
                throw new InvalidOperationException(
                    "Codex 分析提示不能为空。");
            }

            if (string.IsNullOrWhiteSpace(value.outputSchemaPath) ||
                string.IsNullOrWhiteSpace(value.outputPath))
            {
                throw new InvalidOperationException(
                    "Codex 输出路径不能为空。");
            }
        }

        /// <summary>
        /// 执行引用参数相关逻辑。
        /// </summary>
        private static string QuoteArgument(string value)
        {
            value ??= string.Empty;
            if (value.Length > 0 &&
                value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) < 0)
            {
                return value;
            }

            var builder = new StringBuilder();
            builder.Append('"');
            int backslashCount = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append('"');
                    backslashCount = 0;
                    continue;
                }

                builder.Append('\\', backslashCount);
                backslashCount = 0;
                builder.Append(character);
            }

            builder.Append('\\', backslashCount * 2);
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// 执行判断是否Windows相关逻辑。
        /// </summary>
        private static bool IsWindows()
        {
            return Path.DirectorySeparatorChar == '\\';
        }

        /// <summary>
        /// 执行Dispose流程相关逻辑。
        /// </summary>
        private void DisposeProcess()
        {
            Process current = process;
            process = null;
            if (current == null)
            {
                return;
            }

            current.OutputDataReceived -= HandleOutputDataReceived;
            current.ErrorDataReceived -= HandleErrorDataReceived;
            current.Exited -= HandleProcessExited;
            current.Dispose();
        }

    }
}
#endif
