using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace LSMA.Services;

public sealed class DialogService
{
    private FrameworkElement? _root;
    private Panel? _hostPanel;
    private XamlRoot? _xamlRoot;

    public void AttachRoot(FrameworkElement root)
    {
        _root = root;
        _hostPanel = root as Panel;
        _xamlRoot = root.XamlRoot;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        if (_xamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "知道了",
            XamlRoot = _xamlRoot
        };
        await dialog.ShowAsync();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "继续")
    {
        if (_xamlRoot is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _xamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<string?> PromptTextAsync(
        string title,
        string message,
        string initialValue = "",
        string confirmText = "确定")
    {
        if (_xamlRoot is null)
        {
            return null;
        }

        var input = new TextBox
        {
            Text = initialValue,
            PlaceholderText = message,
            MinWidth = 320
        };
        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                input
            }
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = confirmText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _xamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return input.Text.Trim();
    }

    public async Task<NexusDownloadToken?> ShowNexusDownloadBrowserAsync(
        string startUrl,
        long expectedModId,
        long expectedFileId,
        NexusWebLoginCredential? webLogin,
        bool debugStepMode = false,
        bool debugShowWebViewMode = false,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_root is null || _hostPanel is null)
        {
            return null;
        }

        var result = new TaskCompletionSource<NexusDownloadToken?>();
        var keepWebViewVisible = debugStepMode || debugShowWebViewMode;
        progress?.Invoke("正在后台连接 Nexus 下载确认页...");
        var status = new TextBlock
        {
            Text = "正在等待 Nexus 验证...",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var cancelButton = new Button
        {
            Content = "取消下载",
            Style = Application.Current.Resources["DangerButtonStyle"] as Style,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var stepButton = new Button
        {
            Content = "下一步",
            Style = Application.Current.Resources["SubtleButtonStyle"] as Style,
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = debugStepMode ? Visibility.Visible : Visibility.Collapsed,
            IsEnabled = false
        };
        var webView = new WebView2
        {
            MinWidth = 980,
            MinHeight = 620,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var content = new Grid
        {
            Margin = new Thickness(28),
            RowSpacing = 12
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(status);
        Grid.SetColumn(stepButton, 1);
        header.Children.Add(stepButton);
        Grid.SetColumn(cancelButton, 2);
        header.Children.Add(cancelButton);
        content.Children.Add(header);
        Grid.SetRow(webView, 1);
        content.Children.Add(webView);

        var overlay = new Border
        {
            Child = content,
            Background = Application.Current.Resources["AppBackgroundBrush"] as Brush,
            Visibility = Visibility.Visible,
            Opacity = keepWebViewVisible ? 1 : 0,
            HorizontalAlignment = keepWebViewVisible ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalAlignment = keepWebViewVisible ? VerticalAlignment.Stretch : VerticalAlignment.Top,
            Width = keepWebViewVisible ? double.NaN : 1,
            Height = keepWebViewVisible ? double.NaN : 1,
            IsHitTestVisible = keepWebViewVisible
        };
        Grid.SetRowSpan(overlay, 2);
        Canvas.SetZIndex(overlay, 5000);
        _hostPanel.Children.Add(overlay);
        if (debugStepMode)
        {
            ShowBrowser("调试分步模式：点击右上角“下一步”执行一次 Nexus 自动化。");
        }
        else if (debugShowWebViewMode)
        {
            ShowBrowser("调试显示模式：WebView 可见，自动执行 Nexus 自动化。");
        }
        else
        {
            HideBrowser("正在后台连接 Nexus 下载确认页...");
        }

        void ShowBrowser(string message)
        {
            status.Text = message;
            overlay.Width = double.NaN;
            overlay.Height = double.NaN;
            overlay.HorizontalAlignment = HorizontalAlignment.Stretch;
            overlay.VerticalAlignment = VerticalAlignment.Stretch;
            overlay.Opacity = 1;
            overlay.IsHitTestVisible = true;
            progress?.Invoke(message);
        }

        void HideBrowser(string message)
        {
            status.Text = message;
            if (!keepWebViewVisible)
            {
                overlay.Width = 1;
                overlay.Height = 1;
                overlay.HorizontalAlignment = HorizontalAlignment.Left;
                overlay.VerticalAlignment = VerticalAlignment.Top;
                overlay.Opacity = 0;
                overlay.IsHitTestVisible = false;
            }

            progress?.Invoke(message);
        }

        void RemoveOverlay()
        {
            if (_hostPanel.Children.Contains(overlay))
            {
                _hostPanel.Children.Remove(overlay);
            }
        }

        cancelButton.Click += (_, _) =>
        {
            result.TrySetResult(null);
        };

        bool IsCloudflareHelpUri(string? uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
                || !parsed.Host.Equals("help.nexusmods.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var pathAndQuery = parsed.PathAndQuery.ToLowerInvariant();
            return pathAndQuery.Contains("captcha", StringComparison.Ordinal)
                || pathAndQuery.Contains("turnstile", StringComparison.Ordinal)
                || pathAndQuery.Contains("cloudflare", StringComparison.Ordinal);
        }

        bool TryBlockCloudflareHelp(string? uri)
        {
            if (!IsCloudflareHelpUri(uri))
            {
                return false;
            }

            if (keepWebViewVisible)
            {
                ShowBrowser("已阻止 Cloudflare 帮助页跳转；继续等待登录页验证。");
            }
            else
            {
                HideBrowser("已阻止 Cloudflare 帮助页跳转；继续等待登录页验证。");
            }

            return true;
        }

        string ShortUri(string? uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            {
                return uri?.Length > 96 ? uri[..96] : uri ?? "-";
            }

            var value = parsed.Host + parsed.PathAndQuery;
            return value.Length > 96 ? value[..96] : value;
        }

        bool TryAccept(string? uri, bool cancelNavigation)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return false;
            }

            if (!NexusDownloadToken.TryParse(uri, out var token)
                || token is null
                || token.ModId != expectedModId
                || token.FileId != expectedFileId)
            {
                return false;
            }

            result.TrySetResult(token);
            HideBrowser("已获得 Nexus 下载令牌，正在开始下载...");
            return cancelNavigation;
        }

        var debugStepRunning = false;
        var automationReadyAt = DateTimeOffset.UtcNow.AddSeconds(3);
        var initialNavigationStarted = false;
        async Task WaitForAutomationReadyAsync()
        {
            var wait = automationReadyAt - DateTimeOffset.UtcNow;
            if (wait <= TimeSpan.Zero)
            {
                return;
            }

            var message = $"WebView 已打开，等待页面稳定 {Math.Ceiling(wait.TotalSeconds)} 秒后再操作。";
            if (keepWebViewVisible)
            {
                ShowBrowser(message);
            }
            else
            {
                HideBrowser(message);
            }

            await Task.Delay(wait, cancellationToken);
        }

        async Task RunDebugStepAsync()
        {
            if (debugStepRunning || result.Task.IsCompleted || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            debugStepRunning = true;
            stepButton.IsEnabled = false;
            try
            {
                await WaitForAutomationReadyAsync();
                await RunNexusAutomationStepAsync(
                    webView,
                    CreateNexusAutomationScript(webLogin, expectedFileId),
                    ShowBrowser,
                    HideBrowser,
                    result,
                    cancellationToken);
            }
            finally
            {
                debugStepRunning = false;
                stepButton.IsEnabled = debugStepMode && !result.Task.IsCompleted && !cancellationToken.IsCancellationRequested;
            }
        }

        stepButton.Click += async (_, _) =>
        {
            await RunDebugStepAsync();
        };

        async Task RunAutomationAfterInitialDelayAsync()
        {
            await WaitForAutomationReadyAsync();
            await RunNexusAutomationAsync(
                webView,
                webLogin,
                expectedFileId,
                ShowBrowser,
                HideBrowser,
                result,
                cancellationToken);
        }

        async Task EnableDebugStepAfterInitialDelayAsync()
        {
            try
            {
                await WaitForAutomationReadyAsync();
                stepButton.IsEnabled = !result.Task.IsCompleted && !cancellationToken.IsCancellationRequested;
                ShowBrowser("调试分步模式：点击右上角“下一步”执行一次 Nexus 自动化。");
            }
            catch (OperationCanceledException)
            {
            }
        }

        webView.NavigationStarting += (_, args) =>
        {
            args.Cancel = TryBlockCloudflareHelp(args.Uri)
                || TryAccept(args.Uri, cancelNavigation: true);
        };
        webView.CoreWebView2Initialized += (sender, args) =>
        {
            if (args.Exception is not null)
            {
                result.TrySetException(args.Exception);
                return;
            }

            webView.CoreWebView2.NewWindowRequested += (_, windowArgs) =>
            {
                if (TryBlockCloudflareHelp(windowArgs.Uri))
                {
                    windowArgs.Handled = true;
                    return;
                }

                if (TryAccept(windowArgs.Uri, cancelNavigation: false))
                {
                    windowArgs.Handled = true;
                    return;
                }

                windowArgs.Handled = true;
                webView.CoreWebView2.Navigate(windowArgs.Uri);
            };
            webView.CoreWebView2.NavigationStarting += (_, navigationArgs) =>
            {
                if (debugShowWebViewMode)
                {
                    ShowBrowser($"Nexus WebView 正在导航：{ShortUri(navigationArgs.Uri)}");
                }

                navigationArgs.Cancel = TryBlockCloudflareHelp(navigationArgs.Uri)
                    || TryAccept(navigationArgs.Uri, cancelNavigation: true);
            };
            webView.CoreWebView2.NavigationCompleted += (_, navigationArgs) =>
            {
                var source = ShortUri(webView.CoreWebView2.Source);
                if (!navigationArgs.IsSuccess)
                {
                    ShowBrowser($"Nexus WebView 导航失败：{navigationArgs.WebErrorStatus}；{source}");
                }
                else if (debugShowWebViewMode)
                {
                    ShowBrowser($"Nexus WebView 导航完成：{source}");
                }
            };
            webView.CoreWebView2.ProcessFailed += (_, processArgs) =>
            {
                ShowBrowser($"Nexus WebView 进程异常：{processArgs.ProcessFailedKind}");
            };
            webView.CoreWebView2.LaunchingExternalUriScheme += (_, schemeArgs) =>
            {
                schemeArgs.Cancel = TryBlockCloudflareHelp(schemeArgs.Uri)
                    || TryAccept(schemeArgs.Uri, cancelNavigation: true);
            };
            if (!initialNavigationStarted)
            {
                initialNavigationStarted = true;
                webView.CoreWebView2.Navigate(startUrl);
            }

            automationReadyAt = DateTimeOffset.UtcNow.AddSeconds(3);
            if (debugStepMode)
            {
                stepButton.IsEnabled = false;
                ShowBrowser("调试分步模式：WebView 已打开，3 秒后可点击“下一步”。");
                _ = EnableDebugStepAfterInitialDelayAsync();
            }
            else
            {
                if (debugShowWebViewMode)
                {
                    ShowBrowser("调试显示模式：WebView 可见，等待 3 秒后自动执行 Nexus 自动化。");
                }

                _ = RunAutomationAfterInitialDelayAsync();
            }
        };
        webView.Loaded += async (_, _) =>
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
            }
            catch (Exception exception)
            {
                result.TrySetException(exception);
            }
        };

        await using var registration = cancellationToken.Register(() =>
        {
            result.TrySetCanceled(cancellationToken);
        });

        try
        {
            await webView.EnsureCoreWebView2Async();
        }
        catch (Exception exception)
        {
            result.TrySetException(exception);
        }

        try
        {
            return await result.Task;
        }
        finally
        {
            webView.Close();
            RemoveOverlay();
        }
    }

    private static async Task RunNexusAutomationAsync(
        WebView2 webView,
        NexusWebLoginCredential? webLogin,
        long expectedFileId,
        Action<string> showBrowser,
        Action<string> hideBrowser,
        TaskCompletionSource<NexusDownloadToken?> result,
        CancellationToken cancellationToken)
    {
        var script = CreateNexusAutomationScript(webLogin, expectedFileId);
        while (!result.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            await RunNexusAutomationStepAsync(webView, script, showBrowser, hideBrowser, result, cancellationToken);
            await Task.Delay(850, cancellationToken);
        }
    }

    private static async Task RunNexusAutomationStepAsync(
        WebView2 webView,
        string script,
        Action<string> showBrowser,
        Action<string> hideBrowser,
        TaskCompletionSource<NexusDownloadToken?> result,
        CancellationToken cancellationToken)
    {
        if (result.Task.IsCompleted || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var raw = await webView.ExecuteScriptAsync(script);
            var message = JsonSerializer.Deserialize<string>(raw);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (message.StartsWith("SHOW|", StringComparison.Ordinal))
            {
                showBrowser(message[5..]);
            }
            else
            {
                var value = message.StartsWith("HIDE|", StringComparison.Ordinal) ? message[5..] : message;
                hideBrowser(value);
            }
        }
        catch
        {
            hideBrowser("正在等待 Nexus 页面加载...");
        }
    }

    private static string CreateNexusAutomationScript(NexusWebLoginCredential? webLogin, long expectedFileId)
    {
        var payloadJson = JsonSerializer.Serialize(new
        {
            fileId = expectedFileId,
            credential = webLogin is null
                ? new { userName = string.Empty, password = string.Empty }
                : new { userName = webLogin.UserName, password = webLogin.Password }
        });
        return NexusAutomationScriptPrefix + payloadJson + NexusAutomationScriptSuffix;
    }

    private const string NexusAutomationScriptPrefix = """
(() => {
    const payload =
""";

    private const string NexusAutomationScriptSuffix = """
;
    const credential = payload.credential || { userName: "", password: "" };
    const expectedFileId = payload.fileId;
    const visible = element => {
        if (!element) return false;
        const style = getComputedStyle(element);
        const rect = element.getBoundingClientRect();
        return style.display !== "none" && style.visibility !== "hidden" && rect.width > 0 && rect.height > 0;
    };
    const enabled = element =>
        visible(element)
        && !element.disabled
        && element.getAttribute("aria-disabled") !== "true"
        && !element.classList.contains("disabled");
    const textOf = element => [
        element.innerText,
        element.textContent,
        element.value,
        element.title,
        element.getAttribute("aria-label"),
        element.getAttribute("href"),
        element.href
    ].filter(Boolean).join(" ").replace(/\s+/g, " ").trim().toLowerCase();
    const normalizeText = value => (value || "").replace(/\s+/g, " ").trim().toLowerCase();
    const labelOf = element => [
        element.innerText || element.textContent,
        element.value,
        element.title,
        element.getAttribute("aria-label")
    ].filter(Boolean).join(" ").replace(/\s+/g, " ").trim().toLowerCase();
    const hrefOf = element => [
        element.getAttribute("href"),
        element.href
    ].filter(Boolean).join(" ").replace(/\s+/g, " ").trim().toLowerCase();
    const ownHrefOf = element => {
        const value = element?.href || element?.getAttribute?.("href") || "";
        return String(value).trim();
    };
    const mediaSelector = [
        "[data-fancybox]",
        "[data-lightbox]",
        "[data-gallery]",
        "[class*='gallery']",
        "[class*='image']",
        "[class*='media']",
        ".modimages",
        ".mod-image",
        ".image-tile",
        ".thumb"
    ].join(",");
    const isMediaTarget = element => {
        if (!element) return false;
        const href = hrefOf(element);
        const label = labelOf(element);
        return href.includes("tab=images")
            || href.includes("/images/")
            || href.includes("image_id=")
            || href.includes("gallery")
            || href.includes("media")
            || Boolean(element.closest?.(mediaSelector))
            || (Boolean(element.querySelector?.("img,picture,video,canvas")) && !label.includes("slow download"));
    };
    const collectRoots = root => {
        const values = [root];
        for (const element of Array.from(root.querySelectorAll?.("*") || [])) {
            if (element.shadowRoot) {
                values.push(...collectRoots(element.shadowRoot));
            }
        }

        return values;
    };
    const roots = collectRoots(document);
    const queryAll = selector => roots.flatMap(root => Array.from(root.querySelectorAll?.(selector) || []));
    const queryWithin = (scope, selector) => Array.from(scope?.querySelectorAll?.(selector) || []);
    const textNodes = () => roots.flatMap(root => {
        const nodes = [];
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        let node;
        while ((node = walker.nextNode())) {
            nodes.push(node);
        }

        return nodes;
    });
    const looksLikeLightboxOpen = () => {
        const viewportMedia = queryAll("img,picture,video,canvas").find(element => {
            const rect = element.getBoundingClientRect();
            return visible(element) && rect.width >= innerWidth * 0.36 && rect.height >= innerHeight * 0.36;
        });
        const knownOverlay = queryAll(".pswp, .pswp__bg, .mfp-wrap, .fancybox-container, .lg-outer, .lg-backdrop, .tingle-modal, [class*='lightbox'], [class*='media-viewer'], [class*='image-viewer'], [class*='fslightbox']")
            .find(visible);
        const blackOverlay = queryAll("body *, *").find(element => {
            const rect = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            return visible(element)
                && (style.position === "fixed" || style.position === "absolute")
                && rect.left <= 12
                && rect.top <= 120
                && rect.width >= innerWidth * 0.72
                && rect.height >= innerHeight * 0.62
                && /rgba?\((0,\s*0,\s*0|10,\s*10,\s*10|16,\s*16,\s*16)/.test(style.backgroundColor);
        });
        return Boolean(knownOverlay || (blackOverlay && viewportMedia));
    };
    const state = window.__lsmaAutomation ||= {
        lastAction: "",
        lastUrl: "",
        clickCount: 0,
        lastClickAt: 0,
        lastTargetText: "",
        loginSubmittedAt: 0,
        loginSubmittedUrl: "",
        loginAttempts: 0,
        closedLightboxAt: 0,
        awaitingSlowDownloadUntil: 0,
        cloudflareWaitStartedAt: 0,
        loginCloudflareWaitStartedAt: 0
    };
    const now = Date.now();
    const readSessionValue = key => {
        try {
            return sessionStorage.getItem(key) || "";
        } catch {
            return "";
        }
    };
    const writeSessionValue = (key, value) => {
        try {
            sessionStorage.setItem(key, value);
        } catch {
        }
    };
    const removeSessionValue = key => {
        try {
            sessionStorage.removeItem(key);
        } catch {
        }
    };
    const persistedSlowDownloadUntil = Number(readSessionValue("__lsmaSlowDownloadUntil") || "0");
    if (persistedSlowDownloadUntil > state.awaitingSlowDownloadUntil) {
        state.awaitingSlowDownloadUntil = persistedSlowDownloadUntil;
    }

    const markAwaitingSlowDownload = until => {
        state.awaitingSlowDownloadUntil = until;
        writeSessionValue("__lsmaSlowDownloadUntil", String(until));
    };
    const clearAwaitingSlowDownload = () => {
        state.awaitingSlowDownloadUntil = 0;
        removeSessionValue("__lsmaSlowDownloadUntil");
    };
    const locationLabel = location.pathname.split("/").filter(Boolean).slice(-3).join("/") || location.hostname;
    const candidates = queryAll("a,button,input[type=button],input[type=submit],[role=button]");
    const actionCandidates = candidates.filter(element => {
        const rect = element.getBoundingClientRect();
        const label = labelOf(element);
        const href = hrefOf(element);
        const downloadLike =
            label.includes("slow download")
            || /^mod manager download$/.test(label)
            || /^vortex$/.test(label)
            || /log in|login|sign in|continue/.test(label)
            || /(^|["'\s])nxm:\/\//i.test(href)
            || href.includes("nmm=1");
        return enabled(element)
            && rect.width <= 760
            && rect.height <= 220
            && !label.includes("skip to content")
            && !href.endsWith("#maincontent")
            && !(!downloadLike && isMediaTarget(element));
    });
    const describe = element => (labelOf(element) || hrefOf(element) || element.tagName.toLowerCase()).slice(0, 96);
    const shouldRetry = action => state.lastAction !== action || state.lastUrl !== location.href || now - state.lastClickAt > 850;
    const elementCenterHit = element => {
        const rect = element.getBoundingClientRect();
        return document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
    };
    const isSafeSlowTarget = element => {
        const rect = element.getBoundingClientRect();
        const label = labelOf(element);
        const hit = elementCenterHit(element);
        return visible(element)
            && label.includes("slow download")
            && label.length <= 72
            && !label.includes("npc map")
            && !label.includes("image")
            && !label.includes("screenshot")
            && !label.includes("premium")
            && !label.includes("fast download")
            && rect.width > 24
            && rect.height > 18
            && rect.width <= 460
            && rect.height <= 96
            && !isMediaTarget(element)
            && !isMediaTarget(hit);
    };
    const findSlowDownload = () => {
        const direct = queryAll("a,button,[role=button],input[type=button],input[type=submit]").find(isSafeSlowTarget);
        if (direct) {
            return direct;
        }

        for (const node of textNodes()) {
            if (normalizeText(node.nodeValue) !== "slow download") continue;
            const target = node.parentElement?.closest?.("a,button,[role=button],input[type=button],input[type=submit]");
            if (target && isSafeSlowTarget(target)) {
                return target;
            }
        }

        return null;
    };
    const click = (action, element, message) => {
        if (!shouldRetry(action)) {
            return `HIDE|${message}；已点击 ${state.clickCount} 次，等待页面变化：${locationLabel}`;
        }

        if (action === "slow" && !isSafeSlowTarget(element)) {
            state.lastAction = "reject-slow";
            state.lastUrl = location.href;
            state.lastClickAt = now;
            state.lastTargetText = describe(element);
            return `HIDE|已拒绝疑似图片/容器目标：${state.lastTargetText}；继续查找 Slow download`;
        }

        const href = ownHrefOf(element);
        if (action === "slow" && href && !href.startsWith("#") && !href.includes("tab=images") && !href.includes("/images/")) {
            const wasSameAction = state.lastAction === action && state.lastUrl === location.href;
            state.lastAction = action;
            state.lastUrl = location.href;
            state.clickCount = wasSameAction ? state.clickCount + 1 : 1;
            state.lastClickAt = now;
            state.lastTargetText = describe(element);
            markAwaitingSlowDownload(now + 22000);
            location.href = href;
            return `HIDE|已打开 Slow download 链接，等待 Nexus 倒计时或下载令牌；位置=${locationLabel}`;
        }

        element.scrollIntoView({ block: "center", inline: "center" });
        element.focus?.();
        if (action === "slow") {
            element.removeAttribute("disabled");
            element.setAttribute("aria-disabled", "false");
            element.classList.remove("disabled");
            element.dispatchEvent(new PointerEvent("pointerdown", { bubbles: true, pointerType: "mouse" }));
            element.dispatchEvent(new MouseEvent("mousedown", { bubbles: true }));
            element.dispatchEvent(new MouseEvent("mouseup", { bubbles: true }));
        } else {
            element.dispatchEvent(new PointerEvent("pointerdown", { bubbles: true, pointerType: "mouse" }));
            element.dispatchEvent(new MouseEvent("mousedown", { bubbles: true }));
            element.dispatchEvent(new MouseEvent("mouseup", { bubbles: true }));
        }
        element.click();
        const nextCount = state.lastAction === action && state.lastUrl === location.href ? state.clickCount + 1 : 1;
        state.lastAction = action;
        state.lastUrl = location.href;
        state.clickCount = nextCount;
        state.lastClickAt = now;
        state.lastTargetText = describe(element);
        if (action === "slow") {
            markAwaitingSlowDownload(now + 22000);
        }

        return `HIDE|${message}；目标=${state.lastTargetText}；次数=${state.clickCount}；位置=${locationLabel}`;
    };
    const scrollDown = message => {
        const maxScroll = Math.max(0, document.documentElement.scrollHeight - innerHeight);
        const before = Math.round(scrollY);
        const next = Math.min(maxScroll, before + Math.max(520, Math.round(innerHeight * 0.72)));
        const wasScrolling = state.lastAction === "scroll-slow" && state.lastUrl === location.href;
        if (next > before) {
            window.scrollTo({ top: next, behavior: "smooth" });
        }

        state.lastAction = "scroll-slow";
        state.lastUrl = location.href;
        state.lastClickAt = now;
        state.clickCount = wasScrolling ? state.clickCount + 1 : 1;
        return `HIDE|${message}；滚动=${next}/${Math.round(maxScroll)}；位置=${locationLabel}`;
    };
    const setValue = (element, value) => {
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
        if (setter) setter.call(element, value);
        else element.value = value;
        element.dispatchEvent(new Event("input", { bubbles: true }));
        element.dispatchEvent(new Event("change", { bubbles: true }));
    };
    const bodyText = [
        document.body?.innerText || "",
        ...roots.map(root => root.textContent || "")
    ].join(" ").replace(/\s+/g, " ").toLowerCase();
    const isDownloadPopupPage = location.pathname.toLowerCase().includes("/core/libs/common/widgets/downloadpopup");
    const visibleActionCount = queryAll("a,button,input,iframe,[role=button],mod-file-download").filter(visible).length;
    if (isDownloadPopupPage && document.readyState !== "complete" && visibleActionCount === 0) {
        return `HIDE|Nexus Slow download 页面仍在加载；状态=${document.readyState}`;
    }

    if (isDownloadPopupPage && visibleActionCount === 0 && bodyText.trim().length === 0) {
        const blankStartedAt = Number(readSessionValue("__lsmaBlankDownloadPopupAt") || now);
        writeSessionValue("__lsmaBlankDownloadPopupAt", String(blankStartedAt));
        const elapsedSeconds = ((now - blankStartedAt) / 1000) | 0;
        return `HIDE|Nexus Slow download 页面为空白，等待页面自行完成渲染；已等待 ${elapsedSeconds} 秒。`;
    }

    removeSessionValue("__lsmaBlankDownloadPopupAt");
    if (bodyText.includes("you have been banned")
        || bodyText.includes("account has been banned")
        || bodyText.includes("access denied")
        || bodyText.includes("error 1020")
        || bodyText.includes("rate limited")) {
        return "SHOW|Nexus/Cloudflare 已拒绝访问；请在此窗口查看页面提示。";
    }

    const hasRequirementPrompt =
        bodyText.includes("additional files required")
        || (bodyText.includes("this mod requires") && bodyText.includes("additional files"));
    const requirementDialog = hasRequirementPrompt
        ? queryAll("[role=dialog], .modal, .reveal, .popup, .mfp-content, .fancybox-content, [class*='modal'], [class*='popup'], body *")
            .filter(element => {
                const rect = element.getBoundingClientRect();
                const label = labelOf(element);
                return visible(element)
                    && rect.width >= 300
                    && rect.width <= 980
                    && rect.height >= 150
                    && rect.height <= 760
                    && label.includes("additional files required");
            })
            .sort((left, right) => {
                const leftRect = left.getBoundingClientRect();
                const rightRect = right.getBoundingClientRect();
                return (leftRect.width * leftRect.height) - (rightRect.width * rightRect.height);
            })[0] || document.body
        : null;
    if (requirementDialog) {
        document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", code: "Escape", bubbles: true }));
        window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", code: "Escape", bubbles: true }));
        return "HIDE|检测到前置确认弹窗，已关闭并继续查找 Slow download";
    }

    const imageViewer = queryAll(".pswp, .pswp__bg, .mfp-wrap, .fancybox-container, .lg-outer, .lg-backdrop, [class*='lightbox'], [class*='media-viewer'], [class*='image-viewer'], [class*='fslightbox']")
        .find(visible)
        || (looksLikeLightboxOpen() ? document.body : null)
        || queryAll("body *, *").find(element => {
            const rect = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            const label = labelOf(element);
            return visible(element)
                && (style.position === "fixed" || style.position === "sticky")
                && rect.width >= innerWidth * 0.75
                && rect.height >= innerHeight * 0.75
                && /\d+\s*\/\s*\d+/.test(label)
                && Boolean(element.querySelector("img,picture"));
        });
    if (imageViewer) {
        const close = queryAll(".pswp__button--close, .mfp-close, .lg-close, [class*='close'], [aria-label*='close' i], [title*='close' i], button, [role=button]")
            .find(element => {
                const rect = element.getBoundingClientRect();
                const label = labelOf(element);
                return visible(element)
                    && rect.width <= 120
                    && rect.height <= 120
                    && (label === "x" || label === "×" || label.includes("close") || element.className?.toString().toLowerCase().includes("close"));
            });
        if (close) {
            close.click();
        } else {
            document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", code: "Escape", bubbles: true }));
            window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", code: "Escape", bubbles: true }));
        }

        state.closedLightboxAt = now;
        return "HIDE|正在关闭图片预览，完成后继续查找 Slow download";
    }
    if (state.closedLightboxAt && now - state.closedLightboxAt < 2200) {
        return "HIDE|等待图片预览完全关闭后继续...";
    }

    const passwordInput = queryAll("input[type=password]").find(visible);
    const hasDownloadEntry = bodyText.includes("slow download") || bodyText.includes("mod manager download") || bodyText.includes("vortex");
    const cloudflareFrame = queryAll("iframe[src*='challenges.cloudflare.com'], iframe[src*='turnstile'], iframe[src*='cloudflare'], iframe[title*='challenge' i], iframe[title*='turnstile' i], iframe[title*='cloudflare' i]").find(visible);
    const cloudflareInput = queryAll("input[name='cf-turnstile-response'], textarea[name='cf-turnstile-response']")[0];
    const cloudflareBox = queryAll(".cf-turnstile, #challenge-stage, [data-sitekey]").find(visible);
    const cloudflareText =
        bodyText.includes("cloudflare")
        || bodyText.includes("turnstile")
        || bodyText.includes("captcha")
        || bodyText.includes("help us prevent spam")
        || bodyText.includes("checking if the site connection is secure")
        || bodyText.includes("verify you are human")
        || bodyText.includes("just a moment")
        || bodyText.includes("review the security of your connection")
        || bodyText.includes("please wait while we verify");
    const cloudflareComplete = Boolean(cloudflareInput?.value && cloudflareInput.value.length > 10);
    const nexusLoginPage =
        Boolean(passwordInput)
        && location.hostname.toLowerCase().endsWith("nexusmods.com");
    const loginCloudflarePending =
        Boolean(passwordInput)
        && Boolean(cloudflareFrame || cloudflareBox || cloudflareText || nexusLoginPage)
        && !cloudflareComplete;
    const cloudflarePending =
        !passwordInput
        && ((cloudflareText && !hasDownloadEntry)
            || ((cloudflareFrame || cloudflareBox) && !cloudflareComplete));
    if (cloudflarePending) {
        state.cloudflareWaitStartedAt ||= now;
        const elapsedSeconds = ((now - state.cloudflareWaitStartedAt) / 1000) | 0;
        if (elapsedSeconds >= 25) {
            return `SHOW|需要手动完成 Cloudflare 验证；已等待 ${elapsedSeconds} 秒。`;
        }

        return `HIDE|等待 Cloudflare 验证完成；已等待 ${elapsedSeconds} 秒。`;
    }
    state.cloudflareWaitStartedAt = 0;

    const manualCaptcha = queryAll("iframe[src*='hcaptcha'], iframe[src*='recaptcha'], [class*='captcha'], [id*='captcha']").find(visible);
    if (!passwordInput && (manualCaptcha || bodyText.includes("verification code") || bodyText.includes("two-factor") || bodyText.includes("authenticator") || bodyText.includes("one-time code"))) {
        return "SHOW|需要验证码或二次验证，请在此窗口完成后 LSMA 会自动继续。";
    }
    if (bodyText.includes("incorrect") || bodyText.includes("invalid password") || bodyText.includes("login failed")) {
        state.loginSubmittedAt = 0;
        state.loginAttempts = 0;
        return "SHOW|Nexus 登录失败，请检查设置页保存的网页登录账号密码，或在此窗口手动登录。";
    }
    if (passwordInput) {
        const userInput = queryAll("input[type=email], input[type=text], input[name*='login' i], input[name*='user' i], input[name*='email' i], input[id*='login' i], input[id*='user' i], input[id*='email' i]").find(visible);
        if (state.loginSubmittedUrl !== location.href) {
            state.loginSubmittedUrl = location.href;
            state.loginSubmittedAt = 0;
            state.loginAttempts = 0;
            state.loginFilledAt = 0;
        }

        if (credential.userName && credential.password && userInput) {
            const credentialsChanged = userInput.value !== credential.userName || passwordInput.value !== credential.password;
            if (userInput.value !== credential.userName) setValue(userInput, credential.userName);
            if (passwordInput.value !== credential.password) setValue(passwordInput, credential.password);
            if (!state.loginFilledAt || credentialsChanged) {
                state.loginFilledAt = now;
            }

            const fillElapsed = now - state.loginFilledAt;
            if (loginCloudflarePending) {
                state.loginCloudflareWaitStartedAt ||= now;
                const elapsedSeconds = ((now - state.loginCloudflareWaitStartedAt) / 1000) | 0;
                if (elapsedSeconds >= 25) {
                    return `SHOW|已填写 Nexus 登录信息，需要手动完成 Cloudflare 验证；已等待 ${elapsedSeconds} 秒。`;
                }

                return `HIDE|已填写 Nexus 登录信息，等待 Cloudflare 验证完成；已等待 ${elapsedSeconds} 秒。`;
            }
            state.loginCloudflareWaitStartedAt = 0;
            if (manualCaptcha || bodyText.includes("verification code") || bodyText.includes("two-factor") || bodyText.includes("authenticator") || bodyText.includes("one-time code")) {
                return "SHOW|已填写 Nexus 登录信息，需要验证码或二次验证；请在此窗口完成后 LSMA 会自动继续。";
            }
            if (fillElapsed < 7000) {
                return `HIDE|已填写 Nexus 登录信息，等待 Cloudflare 初始化；${(fillElapsed / 1000) | 0}/7 秒。`;
            }

            const loginForm = passwordInput.closest("form");
            const loginScope = loginForm || passwordInput.getRootNode?.() || document;
            const isCloudflareTarget = element => {
                const label = labelOf(element);
                const href = ownHrefOf(element).toLowerCase();
                const fullText = textOf(element);
                return href.includes("cloudflare")
                    || href.includes("help.nexusmods.com")
                    || label.includes("cloudflare")
                    || label.includes("turnstile")
                    || fullText.includes("cloudflare turnstile")
                    || Boolean(element.closest?.(".cf-turnstile, #challenge-stage, [data-sitekey]"));
            };
            const isLoginSubmit = element => {
                const tag = element.tagName.toLowerCase();
                const type = (element.getAttribute("type") || "").toLowerCase();
                const label = labelOf(element);
                return (tag === "button" || (tag === "input" && (type === "submit" || type === "button")))
                    && /\b(log in|login|sign in|continue)\b/.test(label)
                    && !label.includes("forgot")
                    && !label.includes("password")
                    && !label.includes("register")
                    && !isCloudflareTarget(element);
            };
            const submit = queryWithin(loginScope, "button,input[type=submit],input[type=button]")
                .filter(enabled)
                .find(isLoginSubmit)
                || queryWithin(loginScope, "button[type=submit],input[type=submit]")
                    .filter(enabled)
                    .find(element => !isCloudflareTarget(element));
            if (!submit) {
                return "HIDE|已填写 Nexus 登录信息，等待真实登录按钮可用；不会点击 Cloudflare 区域。";
            }

            if (state.loginAttempts >= 3) {
                return "SHOW|Nexus 登录已自动尝试 3 次；请在此窗口确认账号、密码或验证状态。";
            }

            const shouldSubmitLogin = submit && (state.loginAttempts === 0 || now - state.loginSubmittedAt > 4500);
            if (shouldSubmitLogin) {
                state.loginSubmittedAt = now;
                state.loginAttempts += 1;
                submit.scrollIntoView({ block: "center", inline: "center" });
                submit.focus?.();
                submit.click();
                return `HIDE|已自动提交 Nexus 登录（第 ${state.loginAttempts} 次），正在等待确认...`;
            }

            const elapsedSeconds = state.loginSubmittedAt > 0 ? ((now - state.loginSubmittedAt) / 1000 | 0) : 0;
            return `HIDE|已填写 Nexus 登录信息，等待页面确认；第 ${state.loginAttempts} 次提交后 ${elapsedSeconds} 秒`;
        }

        return "SHOW|需要 Nexus 登录；请在设置页保存网页登录账号密码，或在此窗口手动登录。";
    }
    const directNxm = actionCandidates.find(element => /(^|["'\s])nxm:\/\//i.test(textOf(element)));
    if (directNxm) {
        clearAwaitingSlowDownload();
        return click("nxm", directNxm, "已点击 nxm 下载令牌入口，正在交给 LSMA 下载");
    }
    const slowCountdownText =
        bodyText.includes("download will start")
        || bodyText.includes("download should start")
        || bodyText.includes("starting download")
        || bodyText.includes("your download")
        || bodyText.includes("please wait")
        || /\b\d+\s*(second|seconds|秒)\b/.test(bodyText);
    if (slowCountdownText && (state.lastAction === "slow" || now < (state.awaitingSlowDownloadUntil || 0))) {
        markAwaitingSlowDownload(Math.max(state.awaitingSlowDownloadUntil || 0, now + 12000));
    }

    if (now < (state.awaitingSlowDownloadUntil || 0)) {
        const remainingSeconds = Math.max(1, Math.ceil(((state.awaitingSlowDownloadUntil || now) - now) / 1000));
        return `HIDE|已点击 Slow download，等待 Nexus 倒计时或下载令牌；剩余最多 ${remainingSeconds} 秒`;
    }

    const slow = findSlowDownload();
    if (slow) return click("slow", slow, "已自动点击 Slow download，正在等待 Nexus 返回下载令牌");
    if (bodyText.includes("you need an account to download")) {
        const loginLink = actionCandidates.find(element => {
            const label = labelOf(element);
            return label === "login" || label === "log in" || label.includes("login or register");
        });
        if (loginLink) {
            return click("login-required", loginLink, "需要登录 Nexus，正在打开登录页");
        }

        return "SHOW|需要 Nexus 登录；请在设置页保存网页登录账号密码，或在此窗口手动登录。";
    }
    const isStardewModPage = /\/stardewvalley\/mods\/\d+/i.test(location.pathname);
    const searchParams = new URLSearchParams(location.search);
    const currentFileId = searchParams.get("file_id");
    if (isStardewModPage && expectedFileId && currentFileId !== String(expectedFileId)) {
        const url = new URL(location.href);
        url.searchParams.set("tab", "files");
        url.searchParams.set("file_id", expectedFileId);
        location.replace(url.toString());
        return `HIDE|已跳到指定文件区域，准备查找 Slow download；file_id=${expectedFileId}`;
    }

    if (isStardewModPage) {
        if (bodyText.includes("slow download")) {
            return `HIDE|模组文件页已出现 Slow download 文本，等待按钮渲染；位置=${locationLabel}`;
        }

        return scrollDown("正在模组文件页查找 Slow download");
    }

    const modManager = actionCandidates.find(element =>
        /^mod manager download$/.test(labelOf(element))
        || /^vortex$/.test(labelOf(element)));
    if (modManager) return click("manager", modManager, "已自动点击 Nexus 模组管理器下载入口");
    if (bodyText.includes("slow download")) {
        return `HIDE|检测到 Slow download 文本但按钮不可点；上次目标=${state.lastTargetText || "-"}；次数=${state.clickCount}；位置=${locationLabel}`;
    }
    return `HIDE|正在等待 Nexus 下载确认页加载；上次目标=${state.lastTargetText || "-"}；次数=${state.clickCount}；位置=${locationLabel}`;
})()
""";
}
