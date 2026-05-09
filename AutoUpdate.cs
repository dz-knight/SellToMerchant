using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace SellToMerchant
{
    public static class AutoUpdate
    {
        private const string GithubRepo = "dz-knight/SellToMerchant";
        private const string GithubRepoUrl = "https://github.com/dz-knight/SellToMerchant";
        private const string CurrentVersion = "1.0.5";
        private const string StateFileName = "SellToMerchant.update-state.json";
        private const string ResultFileName = "SellToMerchant.update-result.json";

        private static readonly System.Net.Http.HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static readonly object OverlayLock = new();
        private static SynchronizationContext? _mainThreadContext;
        private static UpdateOverlay? _overlay;

        static AutoUpdate()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("SellToMerchant-Mod/1.0");
        }

        public static void CaptureMainThreadContext()
        {
            _mainThreadContext = SynchronizationContext.Current;
            GD.Print(_mainThreadContext == null
                ? "[SellToMerchant] Auto update main thread context unavailable."
                : "[SellToMerchant] Auto update main thread context captured.");
        }

        public static async Task CheckForUpdateAsync(GameVersionInfo versionInfo)
        {
            if (string.IsNullOrWhiteSpace(GithubRepo) || GithubRepo.Contains("yourname/", StringComparison.OrdinalIgnoreCase))
            {
                GD.Print("[SellToMerchant] Auto update is disabled until GithubRepo is configured.");
                return;
            }

            GD.Print("[SellToMerchant] Auto update check started.");
            await WaitForUiReadyAsync();
            GD.Print("[SellToMerchant] Auto update UI reported ready.");
            RunOnMainThread(TryShowPendingUpdateResult, "show pending update result");

            try
            {
                var modDir = GetModDirectory();
                var statePath = Path.Combine(modDir, StateFileName);
                var state = UpdateState.Load(statePath);
                var expectedChannel = ChannelId(versionInfo.Channel);
                var forceChannelSwitch = !string.Equals(state.InstalledChannel, expectedChannel, StringComparison.OrdinalIgnoreCase);

                var release = await FetchLatestReleaseAsync();
                if (release == null)
                {
                    GD.Print("[SellToMerchant] Auto update release info is null.");
                    return;
                }

                var assetName = AssetNameFor(versionInfo.Channel);
                var asset = release.Value.Assets
                    .FirstOrDefault(a => string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));

                if (asset.Name == null || asset.ApiUrl == null)
                {
                    GD.Print($"[SellToMerchant] No update asset found for channel '{expectedChannel}'. Expected: {assetName}");
                    return;
                }

                var latestVersion = release.Value.Version;
                var needsVersionUpdate = IsNewer(latestVersion, CurrentVersion);
                GD.Print($"[SellToMerchant] Auto update version comparison. Current={CurrentVersion}, Latest={latestVersion}, ForceChannelSwitch={forceChannelSwitch}, NeedsUpdate={needsVersionUpdate}");

                if (!needsVersionUpdate && !forceChannelSwitch)
                {
                    state.InstalledVersion = CurrentVersion;
                    state.InstalledChannel = expectedChannel;
                    state.Save(statePath);
                    GD.Print($"[SellToMerchant] Up to date on channel '{expectedChannel}' (v{CurrentVersion}).");
                    return;
                }

                RunOnMainThread(() => ShowUpdatePrompt(versionInfo, release.Value, asset, needsVersionUpdate), "show update prompt");
            }
            catch (System.Net.Http.HttpRequestException)
            {
                GD.Print("[SellToMerchant] Auto update check failed: network unavailable.");
            }
            catch (Exception ex)
            {
                GD.Print($"[SellToMerchant] Auto update check failed: {ex}");
            }
        }

        public static async Task CheckForUpdateAfterStartupAsync(GameVersionInfo versionInfo)
        {
            GD.Print("[SellToMerchant] Auto update delayed startup check queued.");
            await Task.Delay(15000);
            GD.Print("[SellToMerchant] Auto update delayed startup window reached.");
            await CheckForUpdateAsync(versionInfo);
        }

        private static async Task WaitForUiReadyAsync()
        {
            for (int i = 0; i < 120; i++)
            {
                if (Engine.GetMainLoop() is SceneTree tree &&
                    tree.Root != null &&
                    tree.Root.IsNodeReady())
                {
                    GD.Print($"[SellToMerchant] Auto update UI ready after {i + 1} checks.");
                    return;
                }

                await Task.Delay(500);
            }

            GD.Print("[SellToMerchant] Auto update UI readiness wait timed out.");
        }

        private static async Task<UpdateReleaseInfo?> FetchLatestReleaseAsync()
        {
            var url = $"https://api.github.com/repos/{GithubRepo}/releases/latest";
            GD.Print($"[SellToMerchant] Fetching latest release from {url}");
            var response = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);

            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var version = tag.TrimStart('v');
            var htmlUrl = doc.RootElement.TryGetProperty("html_url", out var htmlUrlProp)
                ? htmlUrlProp.GetString() ?? GithubRepoUrl
                : GithubRepoUrl;

            var assets = doc.RootElement.GetProperty("assets")
                .EnumerateArray()
                .Select(a => new UpdateAssetInfo(
                    a.GetProperty("name").GetString(),
                    a.GetProperty("url").GetString(),
                    a.GetProperty("browser_download_url").GetString()))
                .ToArray();

            return new UpdateReleaseInfo(version, htmlUrl, assets);
        }

        private static void RunOnMainThread(Action action, string operationName, bool log = true)
        {
            var context = _mainThreadContext;
            if (context == null)
            {
                GD.Print($"[SellToMerchant] Auto update could not {operationName}: main thread context is unavailable.");
                return;
            }

            if (log)
                GD.Print($"[SellToMerchant] Auto update queued main-thread action: {operationName}.");

            context.Post(_ =>
            {
                try
                {
                    if (log)
                        GD.Print($"[SellToMerchant] Auto update running main-thread action: {operationName}.");
                    action();
                }
                catch (Exception ex)
                {
                    GD.Print($"[SellToMerchant] Auto update main-thread action failed ({operationName}): {ex}");
                }
            }, null);
        }

        private static void ShowUpdatePrompt(
            GameVersionInfo versionInfo,
            UpdateReleaseInfo release,
            UpdateAssetInfo asset,
            bool needsVersionUpdate)
        {
            var overlay = EnsureOverlay();
            if (overlay == null)
            {
                GD.Print("[SellToMerchant] Update prompt could not be shown: overlay creation failed.");
                return;
            }

            var reason = needsVersionUpdate
                ? $"检测到新版本 v{release.Version}。当前版本为 v{CurrentVersion}。"
                : "检测到当前安装包与游戏分支不匹配，需要切换到对应分支包。";
            var channelText = $"当前分支：{ChannelId(versionInfo.Channel)}，目标安装包：{asset.Name}";

            GD.Print($"[SellToMerchant] Update prompt shown. Current={CurrentVersion}, Latest={release.Version}, Channel={ChannelId(versionInfo.Channel)}, Asset={asset.Name}");

            overlay.ShowChoice(
                "发现可用更新",
                $"{reason}\n{channelText}",
                onAutoUpdate: () => _ = StartAutomaticUpdateAsync(versionInfo, release, asset, overlay),
                onGithubDownload: () =>
                {
                    GD.Print("[SellToMerchant] User selected manual GitHub download.");
                    OpenUrl(release.HtmlUrl);
                    overlay.CloseOverlay();
                },
                onSkip: () =>
                {
                    GD.Print("[SellToMerchant] User skipped update.");
                    overlay.CloseOverlay();
                });
        }

        private static async Task StartAutomaticUpdateAsync(
            GameVersionInfo versionInfo,
            UpdateReleaseInfo release,
            UpdateAssetInfo asset,
            UpdateOverlay overlay)
        {
            try
            {
                GD.Print($"[SellToMerchant] User selected automatic update. Target version={release.Version}, asset={asset.Name}.");
                RunOnMainThread(() => overlay.StartProgress("正在从 GitHub 获取更新包...", 0d, false), "start update progress");

                var stage = await DownloadUpdatePackageAsync(asset.ApiUrl!, overlay);
                RunOnMainThread(() => overlay.SetProgressState("正在准备安装程序...", 1d, false), "prepare installer", log: false);

                LaunchUpdater(stage, release.Version, ChannelId(versionInfo.Channel));

                RunOnMainThread(() =>
                {
                    overlay.ShowCompletionAction(
                        "更新包已下载完成",
                        "点击“完成”后将退出游戏，外部更新程序会覆盖文件并自动重新启动游戏。下次进入后会显示“更新已完成”。",
                        onComplete: () =>
                        {
                            overlay.CloseOverlay();
                            if (Engine.GetMainLoop() is SceneTree tree)
                                tree.Quit();
                        });
                }, "show update completion");
            }
            catch (System.Net.Http.HttpRequestException)
            {
                GD.Print("[SellToMerchant] Automatic update download failed with network error.");
                RunOnMainThread(() =>
                    overlay.ShowInfo("更新失败", "无法连接到 GitHub 下载更新包。你可以稍后重试，或者选择从 GitHub 自行下载。"),
                    "show network error");
            }
            catch (Exception ex)
            {
                GD.Print($"[SellToMerchant] Automatic update failed: {ex}");
                RunOnMainThread(() =>
                    overlay.ShowInfo("更新失败", $"自动更新过程中发生错误：{ex.Message}"),
                    "show update error");
            }
        }

        private static async Task<UpdateStageInfo> DownloadUpdatePackageAsync(string assetApiUrl, UpdateOverlay overlay)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "SellToMerchantUpdate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, "update.zip");
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, assetApiUrl);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            using var response = await Http.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalLength = response.Content.Headers.ContentLength;
            var progressTimer = Stopwatch.StartNew();
            long lastReportedMs = -1;

            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var destination = File.Create(zipPath))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;

                    await destination.WriteAsync(buffer, 0, read);
                    totalRead += read;

                    var shouldReport = lastReportedMs < 0 || progressTimer.ElapsedMilliseconds - lastReportedMs >= 120;
                    if (!shouldReport && totalLength.HasValue && totalRead < totalLength.Value)
                        continue;

                    lastReportedMs = progressTimer.ElapsedMilliseconds;
                    var progress = totalLength.HasValue && totalLength.Value > 0
                        ? Math.Clamp((double)totalRead / totalLength.Value, 0d, 1d)
                        : 0d;
                    var downloadedMb = $"{totalRead / 1024d / 1024d:0.0} MB";
                    var totalMb = totalLength.HasValue ? $"{totalLength.Value / 1024d / 1024d:0.0} MB" : "未知大小";
                    var status = $"正在下载更新包... {downloadedMb} / {totalMb}";
                    RunOnMainThread(() => overlay.SetProgressState(status, progress, !totalLength.HasValue), "update download progress", log: false);
                }
            }

            RunOnMainThread(() => overlay.SetProgressState("正在解压更新包...", 1d, false), "extract update package", log: false);

            var extractDir = Path.Combine(tempDir, "extract");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var sourceDll = Directory.GetFiles(extractDir, "SellToMerchant.dll", SearchOption.AllDirectories).FirstOrDefault();
            var sourceJson = Directory.GetFiles(extractDir, "SellToMerchant.json", SearchOption.AllDirectories).FirstOrDefault();

            if (sourceDll == null || sourceJson == null)
                throw new InvalidOperationException("下载的更新包缺少 SellToMerchant.dll 或 SellToMerchant.json。");

            var stageDir = Path.Combine(tempDir, "stage");
            Directory.CreateDirectory(stageDir);
            File.Copy(sourceDll, Path.Combine(stageDir, "SellToMerchant.dll"), overwrite: true);
            File.Copy(sourceJson, Path.Combine(stageDir, "SellToMerchant.json"), overwrite: true);

            return new UpdateStageInfo(tempDir, stageDir);
        }

        private static void LaunchUpdater(UpdateStageInfo stage, string targetVersion, string targetChannel)
        {
            var modDir = GetModDirectory();
            var resultPath = Path.Combine(modDir, ResultFileName);
            var updaterScriptPath = Path.Combine(stage.TempDirectory, "apply-update.ps1");
            var gameExePath = OS.GetExecutablePath();
            var processId = System.Environment.ProcessId;

            var script = $$"""
$ErrorActionPreference = 'Stop'
$sourceDir = '{{EscapeForPowerShell(stage.StageDirectory)}}'
$destDir = '{{EscapeForPowerShell(modDir)}}'
$resultPath = '{{EscapeForPowerShell(resultPath)}}'
$gameExe = '{{EscapeForPowerShell(gameExePath)}}'
$processId = {{processId}}
$targetVersion = '{{EscapeForPowerShell(targetVersion)}}'
$targetChannel = '{{EscapeForPowerShell(targetChannel)}}'

while (Get-Process -Id $processId -ErrorAction SilentlyContinue) {
    Start-Sleep -Milliseconds 500
}

Copy-Item -LiteralPath (Join-Path $sourceDir 'SellToMerchant.dll') -Destination (Join-Path $destDir 'SellToMerchant.dll') -Force
Copy-Item -LiteralPath (Join-Path $sourceDir 'SellToMerchant.json') -Destination (Join-Path $destDir 'SellToMerchant.json') -Force

$result = @{
    version = $targetVersion
    channel = $targetChannel
    updated_at = (Get-Date).ToString('O')
}
$result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8

Start-Process -FilePath $gameExe -WorkingDirectory (Split-Path -Parent $gameExe)
""";

            File.WriteAllText(updaterScriptPath, script, Encoding.UTF8);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{updaterScriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        private static void TryShowPendingUpdateResult()
        {
            try
            {
                var resultPath = Path.Combine(GetModDirectory(), ResultFileName);
                if (!File.Exists(resultPath))
                    return;

                var json = File.ReadAllText(resultPath);
                var result = JsonSerializer.Deserialize<UpdateResultRecord>(json);
                File.Delete(resultPath);

                if (result == null || string.IsNullOrWhiteSpace(result.Version))
                    return;

                var overlay = EnsureOverlay();
                overlay?.ShowInfo("更新已完成", $"已更新到 v{result.Version}（{result.Channel}）。");
            }
            catch (Exception ex)
            {
                GD.Print($"[SellToMerchant] Failed to read update result marker: {ex.Message}");
            }
        }

        private static UpdateOverlay? EnsureOverlay()
        {
            lock (OverlayLock)
            {
                if (_overlay != null && GodotObject.IsInstanceValid(_overlay))
                {
                    _overlay.BringToFront();
                    return _overlay;
                }

                if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                    return null;

                _overlay = new UpdateOverlay(() =>
                {
                    lock (OverlayLock)
                    {
                        _overlay = null;
                    }
                });
                tree.Root.AddChild(_overlay);
                return _overlay;
            }
        }

        private static string GetModDirectory()
        {
            var assemblyPath = typeof(AutoUpdate).Assembly.Location;
            var modDir = Path.GetDirectoryName(assemblyPath);
            return string.IsNullOrWhiteSpace(modDir) ? AppContext.BaseDirectory : modDir;
        }

        private static void OpenUrl(string url)
        {
            GD.Print($"[SellToMerchant] Opening external URL: {url}");
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private static string AssetNameFor(GameChannel channel)
        {
            return channel switch
            {
                GameChannel.PublicBeta => "SellToMerchant-public-beta.zip",
                _ => "SellToMerchant-stable.zip",
            };
        }

        private static string ChannelId(GameChannel channel)
        {
            return channel switch
            {
                GameChannel.PublicBeta => "public-beta",
                GameChannel.Stable => "stable",
                _ => "unknown",
            };
        }

        private static bool IsNewer(string incoming, string current)
        {
            try
            {
                var left = incoming.Split('.');
                var right = current.Split('.');
                var len = Math.Max(left.Length, right.Length);
                for (int i = 0; i < len; i++)
                {
                    var l = i < left.Length && int.TryParse(left[i], out var lv) ? lv : 0;
                    var r = i < right.Length && int.TryParse(right[i], out var rv) ? rv : 0;
                    if (l != r)
                        return l > r;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string EscapeForPowerShell(string value)
        {
            return value.Replace("'", "''");
        }

        private readonly record struct UpdateAssetInfo(string? Name, string? ApiUrl, string? DownloadUrl);

        private readonly record struct UpdateReleaseInfo(string Version, string HtmlUrl, UpdateAssetInfo[] Assets);

        private readonly record struct UpdateStageInfo(string TempDirectory, string StageDirectory);

        private sealed class UpdateResultRecord
        {
            public string Version { get; set; } = "";
            public string Channel { get; set; } = "";
            public string UpdatedAt { get; set; } = "";
        }

        private sealed class UpdateOverlay : CanvasLayer
        {
            private const float PanelWidth = 560f;
            private const float PanelHeight = 260f;
            private const float Margin = 18f;
            private const float TitleTop = 16f;
            private const float TitleHeight = 34f;
            private const float BodyTop = 62f;
            private const float BodyHeight = 88f;
            private const float ProgressBarTop = 156f;
            private const float ProgressBarHeight = 20f;
            private const float ProgressLabelTop = 182f;
            private const float ProgressLabelHeight = 22f;
            private const float ButtonTop = 210f;
            private const float ButtonWidth = 140f;
            private const float ButtonHeight = 36f;
            private const float ButtonGap = 12f;

            private readonly Action _onClosed;
            private readonly Control _root;
            private readonly Control _panel;
            private readonly Label _titleLabel;
            private readonly Label _bodyLabel;
            private readonly ProgressBar _progressBar;
            private readonly Label _progressLabel;
            private readonly ActionButton _primaryButton;
            private readonly ActionButton _secondaryButton;
            private readonly ActionButton _tertiaryButton;
            private bool _isDragging;
            private bool _hasManualPosition;

            public UpdateOverlay(Action onClosed)
            {
                _onClosed = onClosed;
                Layer = 5000;
                ProcessMode = ProcessModeEnum.Always;
                SetProcess(true);

                _root = new Control
                {
                    AnchorRight = 1,
                    AnchorBottom = 1
                };
                AddChild(_root);

                var backdrop = new ColorRect
                {
                    AnchorRight = 1,
                    AnchorBottom = 1,
                    Color = new Color(0f, 0f, 0f, 0.55f),
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                _root.AddChild(backdrop);

                _panel = new Control
                {
                    Size = new Vector2(PanelWidth, PanelHeight),
                    CustomMinimumSize = new Vector2(PanelWidth, PanelHeight),
                    MouseFilter = Control.MouseFilterEnum.Stop
                };
                _panel.GuiInput += OnPanelGuiInput;
                _root.AddChild(_panel);

                var background = new ColorRect
                {
                    Position = Vector2.Zero,
                    Size = new Vector2(PanelWidth, PanelHeight),
                    Color = new Color(0.08f, 0.09f, 0.11f, 0.98f),
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                _panel.AddChild(background);

                AddBorderLine(new Vector2(0, 0), new Vector2(PanelWidth, 2));
                AddBorderLine(new Vector2(0, PanelHeight - 2), new Vector2(PanelWidth, 2));
                AddBorderLine(new Vector2(0, 0), new Vector2(2, PanelHeight));
                AddBorderLine(new Vector2(PanelWidth - 2, 0), new Vector2(2, PanelHeight));

                var titleBar = new ColorRect
                {
                    Position = new Vector2(Margin, TitleTop),
                    Size = new Vector2(PanelWidth - Margin * 2, TitleHeight),
                    Color = new Color(0.18f, 0.15f, 0.08f, 0.95f),
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                _panel.AddChild(titleBar);

                _titleLabel = new Label
                {
                    Position = titleBar.Position,
                    Size = titleBar.Size,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    SelfModulate = new Color(1f, 0.86f, 0.33f),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                _panel.AddChild(_titleLabel);

                _bodyLabel = new Label
                {
                    Position = new Vector2(Margin, BodyTop),
                    Size = new Vector2(PanelWidth - Margin * 2, BodyHeight),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    VerticalAlignment = VerticalAlignment.Top,
                    SelfModulate = new Color(0.92f, 0.92f, 0.92f),
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                _panel.AddChild(_bodyLabel);

                _progressBar = new ProgressBar
                {
                    Position = new Vector2(Margin, ProgressBarTop),
                    Size = new Vector2(PanelWidth - Margin * 2, ProgressBarHeight),
                    MinValue = 0,
                    MaxValue = 100,
                    Value = 0,
                    Visible = false,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                _panel.AddChild(_progressBar);

                _progressLabel = new Label
                {
                    Position = new Vector2(Margin, ProgressLabelTop),
                    Size = new Vector2(PanelWidth - Margin * 2, ProgressLabelHeight),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visible = false,
                    SelfModulate = new Color(0.92f, 0.92f, 0.92f),
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                _panel.AddChild(_progressLabel);

                _primaryButton = new ActionButton();
                _secondaryButton = new ActionButton();
                _tertiaryButton = new ActionButton();
                _panel.AddChild(_primaryButton);
                _panel.AddChild(_secondaryButton);
                _panel.AddChild(_tertiaryButton);
                LayoutButtons();
            }

            public override void _Ready()
            {
                UpdateRootSize();
                ResetCenteredLayout();
            }

            public override void _Process(double delta)
            {
                if (GetViewport() == null)
                    return;

                UpdateRootSize();

                if (_isDragging && !Input.IsMouseButtonPressed(MouseButton.Left))
                    _isDragging = false;

                if (!_hasManualPosition)
                    CenterPanel();
                else
                    ClampPanelToViewport();
            }

            public void BringToFront()
            {
                Visible = true;
                ResetCenteredLayout();
            }

            public void CloseOverlay()
            {
                _onClosed();
                QueueFree();
            }

            public void ShowChoice(string title, string body, Action onAutoUpdate, Action onGithubDownload, Action onSkip)
            {
                _titleLabel.Text = title;
                _bodyLabel.Text = body;
                _progressBar.Visible = false;
                _progressLabel.Visible = false;

                _primaryButton.Configure("自动爬取更新", onAutoUpdate, true);
                _secondaryButton.Configure("GitHub 自行下载", onGithubDownload, true);
                _tertiaryButton.Configure("暂不更新", onSkip, true);
                LayoutButtons();
                ResetCenteredLayout();
            }

            public void StartProgress(string status, double progress, bool indeterminate)
            {
                _titleLabel.Text = "正在更新";
                _bodyLabel.Text = "正在自动获取更新，请不要关闭游戏。";
                _progressBar.Visible = true;
                _progressLabel.Visible = true;
                SetProgressState(status, progress, indeterminate);

                _primaryButton.Configure("", null, false);
                _secondaryButton.Configure("", null, false);
                _tertiaryButton.Configure("", null, false);
                LayoutButtons();
                ResetCenteredLayout();
            }

            public void SetProgressState(string status, double progress, bool indeterminate)
            {
                _progressLabel.Text = status;
                _progressBar.Visible = true;
                _progressLabel.Visible = true;
                _progressBar.Value = indeterminate ? 0 : Math.Clamp(progress * 100d, 0d, 100d);
                _progressBar.ShowPercentage = !indeterminate;
            }

            public void ShowCompletionAction(string title, string body, Action onComplete)
            {
                _titleLabel.Text = title;
                _bodyLabel.Text = body;
                _progressBar.Visible = true;
                _progressBar.Value = 100;
                _progressBar.ShowPercentage = false;
                _progressLabel.Visible = true;
                _progressLabel.Text = "更新包已准备完成";

                _primaryButton.Configure("完成", onComplete, true);
                _secondaryButton.Configure("", null, false);
                _tertiaryButton.Configure("", null, false);
                LayoutButtons();
                ResetCenteredLayout();
            }

            public void ShowInfo(string title, string body)
            {
                _titleLabel.Text = title;
                _bodyLabel.Text = body;
                _progressBar.Visible = false;
                _progressLabel.Visible = false;

                _primaryButton.Configure("确定", CloseOverlay, true);
                _secondaryButton.Configure("", null, false);
                _tertiaryButton.Configure("", null, false);
                LayoutButtons();
                ResetCenteredLayout();
            }

            private void LayoutButtons()
            {
                var visibleButtons = new[] { _primaryButton, _secondaryButton, _tertiaryButton }
                    .Where(button => button.Visible)
                    .ToArray();

                if (visibleButtons.Length == 0)
                    return;

                var totalWidth = visibleButtons.Length * ButtonWidth + (visibleButtons.Length - 1) * ButtonGap;
                var startX = (PanelWidth - totalWidth) * 0.5f;

                for (int i = 0; i < visibleButtons.Length; i++)
                {
                    visibleButtons[i].Position = new Vector2(startX + i * (ButtonWidth + ButtonGap), ButtonTop);
                    visibleButtons[i].Size = new Vector2(ButtonWidth, ButtonHeight);
                }
            }

            private void OnPanelGuiInput(InputEvent @event)
            {
                if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
                {
                    if (IsPointerOverAnyButton(mouseButton.GlobalPosition))
                        return;

                    if (mouseButton.Pressed)
                    {
                        _isDragging = true;
                        _hasManualPosition = true;
                        _panel.AcceptEvent();
                    }
                    else if (_isDragging)
                    {
                        _isDragging = false;
                        _panel.AcceptEvent();
                    }

                    return;
                }

                if (@event is InputEventMouseMotion motion && _isDragging)
                {
                    if (!Input.IsMouseButtonPressed(MouseButton.Left))
                    {
                        _isDragging = false;
                        return;
                    }

                    _panel.Position += motion.Relative;
                    ClampPanelToViewport();
                    _panel.AcceptEvent();
                }
            }

            private bool IsPointerOverAnyButton(Vector2 globalPosition)
            {
                return _primaryButton.Visible && _primaryButton.GetGlobalRect().HasPoint(globalPosition) ||
                       _secondaryButton.Visible && _secondaryButton.GetGlobalRect().HasPoint(globalPosition) ||
                       _tertiaryButton.Visible && _tertiaryButton.GetGlobalRect().HasPoint(globalPosition);
            }

            private void UpdateRootSize()
            {
                _root.Size = GetViewportSize();
            }

            private void ResetCenteredLayout()
            {
                _hasManualPosition = false;
                CenterPanel();
            }

            private void CenterPanel()
            {
                var viewportSize = GetViewportSize();
                _panel.Position = (viewportSize - _panel.Size) * 0.5f;
                ClampPanelToViewport();
            }

            private void ClampPanelToViewport()
            {
                var viewportSize = GetViewportSize();
                var maxX = Math.Max(0f, viewportSize.X - _panel.Size.X);
                var maxY = Math.Max(0f, viewportSize.Y - _panel.Size.Y);
                _panel.Position = new Vector2(
                    Math.Clamp(_panel.Position.X, 0f, maxX),
                    Math.Clamp(_panel.Position.Y, 0f, maxY));
            }

            private Vector2 GetViewportSize()
            {
                return GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
            }

            private void AddBorderLine(Vector2 position, Vector2 size)
            {
                var line = new ColorRect
                {
                    Position = position,
                    Size = size,
                    Color = new Color(0.80f, 0.64f, 0.20f, 1f),
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                _panel.AddChild(line);
            }
        }

        private sealed class ActionButton : Control
        {
            private readonly ColorRect _background;
            private readonly ColorRect _borderTop;
            private readonly ColorRect _borderBottom;
            private readonly ColorRect _borderLeft;
            private readonly ColorRect _borderRight;
            private readonly Label _label;
            private Action? _onClick;

            public ActionButton()
            {
                MouseFilter = MouseFilterEnum.Stop;

                _background = new ColorRect
                {
                    Position = Vector2.Zero,
                    Size = new Vector2(140f, 36f),
                    Color = new Color(0.18f, 0.18f, 0.18f, 0.92f),
                    MouseFilter = MouseFilterEnum.Ignore
                };
                AddChild(_background);

                _borderTop = CreateBorderRect();
                _borderBottom = CreateBorderRect();
                _borderLeft = CreateBorderRect();
                _borderRight = CreateBorderRect();
                AddChild(_borderTop);
                AddChild(_borderBottom);
                AddChild(_borderLeft);
                AddChild(_borderRight);

                _label = new Label
                {
                    Position = Vector2.Zero,
                    Size = new Vector2(140f, 36f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    SelfModulate = new Color(0.95f, 0.95f, 0.95f),
                    MouseFilter = MouseFilterEnum.Ignore
                };
                AddChild(_label);

                GuiInput += e =>
                {
                    if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                    {
                        _onClick?.Invoke();
                        AcceptEvent();
                    }
                };

                UpdateVisualSize(Size == Vector2.Zero ? new Vector2(140f, 36f) : Size);
            }

            public void Configure(string text, Action? onClick, bool visible)
            {
                Visible = visible;
                _label.Text = text;
                _onClick = visible ? onClick : null;
            }

            public override void _Notification(int what)
            {
                if (what == NotificationResized)
                    UpdateVisualSize(Size);
            }

            private static ColorRect CreateBorderRect()
            {
                return new ColorRect
                {
                    Color = new Color(0.70f, 0.55f, 0.15f, 1f),
                    MouseFilter = MouseFilterEnum.Ignore
                };
            }

            private void UpdateVisualSize(Vector2 size)
            {
                _background.Size = size;
                _borderTop.Position = new Vector2(0, 0);
                _borderTop.Size = new Vector2(size.X, 1);
                _borderBottom.Position = new Vector2(0, Math.Max(0, size.Y - 1));
                _borderBottom.Size = new Vector2(size.X, 1);
                _borderLeft.Position = new Vector2(0, 0);
                _borderLeft.Size = new Vector2(1, size.Y);
                _borderRight.Position = new Vector2(Math.Max(0, size.X - 1), 0);
                _borderRight.Size = new Vector2(1, size.Y);
                _label.Size = size;
            }
        }
    }
}
