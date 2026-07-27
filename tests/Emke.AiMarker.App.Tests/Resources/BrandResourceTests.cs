using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Emke.AiMarker.App.Tests.TestSupport;

namespace Emke.AiMarker.App.Tests.Resources;

public sealed partial class BrandResourceTests
{
    private static readonly string Root = RepositoryRoot.Find();
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Brand_source_hash_dimensions_and_manifest_match_approved_asset()
    {
        string svgPath = FromRoot("assets", "branding", "emke-app-logo.svg");
        byte[] svgBytes = File.ReadAllBytes(svgPath);
        string svgText = File.ReadAllText(svgPath);

        Assert.Equal(
            "c98e5a189b344bd5adb6b49848acdf307ce0f12c4e60a428c4c421fe258142a6",
            Convert.ToHexString(SHA256.HashData(svgBytes)).ToLowerInvariant());
        Assert.Contains("width=\"202\"", svgText, StringComparison.Ordinal);
        Assert.Contains("height=\"202\"", svgText, StringComparison.Ordinal);
        Assert.Contains("#36A39E", svgText, StringComparison.Ordinal);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(
            FromRoot("assets", "branding", "brand-assets.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal("emke-app-logo.svg", root.GetProperty("source").GetString());
        Assert.Equal(202, root.GetProperty("source_width").GetInt32());
        Assert.Equal(202, root.GetProperty("source_height").GetInt32());
        Assert.Equal(
            "c98e5a189b344bd5adb6b49848acdf307ce0f12c4e60a428c4c421fe258142a6",
            root.GetProperty("source_sha256").GetString());
        Assert.Equal("#36A39E", root.GetProperty("accent").GetString());
    }

    [Theory]
    [InlineData("emke-app-logo-32.png", 32, 32)]
    [InlineData("emke-app-logo-256.png", 256, 256)]
    public void Png_derivatives_have_the_required_dimensions(
        string fileName,
        int expectedWidth,
        int expectedHeight)
    {
        using FileStream stream = File.OpenRead(
            FromRoot("src", "Emke.AiMarker.App", "Assets", fileName));
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        BitmapFrame frame = Assert.Single(decoder.Frames);
        Assert.Equal(expectedWidth, frame.PixelWidth);
        Assert.Equal(expectedHeight, frame.PixelHeight);
    }

    [Fact]
    public void Ico_derivative_is_a_non_empty_icon_container()
    {
        byte[] icon = File.ReadAllBytes(
            FromRoot("src", "Emke.AiMarker.App", "Assets", "emke-ai-marker.ico"));

        Assert.True(icon.Length > 6);
        Assert.Equal([0, 0, 1, 0], icon[..4]);
        Assert.True(BitConverter.ToUInt16(icon, 4) > 0);
    }

    [Fact]
    public void Theme_exposes_the_confirmed_brand_tokens()
    {
        XDocument theme = XDocument.Load(
            FromRoot("src", "Emke.AiMarker.App", "Resources", "Theme.xaml"));

        AssertResource(theme, "BrandAccentColor", "#36A39E");
        AssertResource(theme, "SafetyBackgroundColor", "#E3F3F1");
        AssertResource(theme, "AppBackgroundColor", "#F7F9F9");
        AssertResource(theme, "PrimaryTextColor", "#172022");
        AssertResource(theme, "DangerColor", "#B42318");
        AssertResource(theme, "AppFontFamily", "Segoe UI Variable, Segoe UI");
        AssertBrushResource(theme, "BrandAccentBrush", "BrandAccentColor");
        AssertBrushResource(theme, "SafetyBackgroundBrush", "SafetyBackgroundColor");
        AssertBrushResource(theme, "AppBackgroundBrush", "AppBackgroundColor");
        AssertBrushResource(theme, "PrimaryTextBrush", "PrimaryTextColor");
        AssertBrushResource(theme, "DangerBrush", "DangerColor");
    }

    [Fact]
    public void Primary_and_secondary_buttons_use_distinct_uncropped_focus_visuals()
    {
        XDocument controls = XDocument.Load(
            FromRoot("src", "Emke.AiMarker.App", "Resources", "Controls.xaml"));

        XElement secondaryFocus = FindStyle(controls, "BrandFocusVisual");
        XElement primaryFocus = FindStyle(controls, "PrimaryButtonFocusVisual");
        XElement buttonStyle = controls.Root!
            .Elements(Presentation + "Style")
            .Single(element =>
                (string?)element.Attribute("TargetType") == "Button"
                && element.Attribute(Xaml + "Key") is null);
        XElement primaryButtonStyle = FindStyle(controls, "PrimaryButtonStyle");

        AssertSetter(
            buttonStyle,
            "FocusVisualStyle",
            "{StaticResource BrandFocusVisual}");
        AssertSetter(
            primaryButtonStyle,
            "FocusVisualStyle",
            "{StaticResource PrimaryButtonFocusVisual}");
        AssertFocusBorders(
            secondaryFocus,
            "{StaticResource BrandAccentBrush}",
            "White");
        AssertFocusBorders(
            primaryFocus,
            "White",
            "{StaticResource PrimaryTextBrush}");
    }

    [Fact]
    public void Chinese_resources_cover_the_visible_shell_contract()
    {
        XDocument strings = XDocument.Load(
            FromRoot("src", "Emke.AiMarker.App", "Resources", "Strings.zh-CN.xaml"));
        HashSet<string> keys = strings.Root!
            .Elements()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        string[] requiredKeys =
        [
            "AppName",
            "WindowTitle",
            "SettingsButton",
            "WorkspaceTitle",
            "OfflinePrivacyCopy",
            "DropTargetTitle",
            "SupportedFormats",
            "SupportedMediaDialogFilter",
            "AddFilesButton",
            "AddFolderButton",
            "MarkerLabel",
            "SafeCopyStatement",
            "FileDetails",
            "FileColumn",
            "StatusColumn",
            "StartMarkButton",
            "VerifyOnlyButton",
            "SafeStopButton",
            "OpenOutputButton",
            "OpenLogButton",
            "ResetButton",
            "AdvancedWarning",
            "ErrorTitle",
            "ErrorDismissButton",
            "ResultAdded",
            "ResultCompliant",
            "ResultUnmarked",
            "ResultFailed",
        ];

        Assert.Empty(requiredKeys.Except(keys, StringComparer.Ordinal));
        Assert.Equal(
            "支持的媒体|*.jpg;*.jpeg;*.png;*.mp4",
            strings.Root!
                .Elements()
                .Single(element =>
                    (string?)element.Attribute(Xaml + "Key")
                    == "SupportedMediaDialogFilter")
                .Value);
        Assert.All(
            strings.Root!.Elements(),
            element => Assert.False(string.IsNullOrWhiteSpace(element.Value)));
    }

    [Fact]
    public void Visible_chinese_copy_is_centralized_outside_app_code_and_shell_xaml()
    {
        string appRoot = FromRoot("src", "Emke.AiMarker.App");
        string[] inspected = Directory
            .EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Append(Path.Combine(appRoot, "MainWindow.xaml"))
            .ToArray();

        Assert.All(
            inspected,
            path => Assert.DoesNotMatch(CjkText(), File.ReadAllText(path)));
    }

    [Fact]
    public void Main_window_has_one_compact_vertical_state_driven_shell()
    {
        XDocument window = XDocument.Load(
            FromRoot("src", "Emke.AiMarker.App", "MainWindow.xaml"));

        Assert.Equal(Presentation + "Window", window.Root!.Name);
        Assert.Equal(
            "720",
            window.Descendants(Presentation + "Grid")
                .Single(element => (string?)element.Attribute("MaxWidth") is not null)
                .Attribute("MaxWidth")!.Value);

        XElement scrollViewer = Assert.Single(
            window.Descendants(Presentation + "ScrollViewer"));
        Assert.Equal(
            "Auto",
            (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal(
            "Disabled",
            (string?)scrollViewer.Attribute("HorizontalScrollBarVisibility"));

        string xaml = File.ReadAllText(
            FromRoot("src", "Emke.AiMarker.App", "MainWindow.xaml"));
        Assert.Contains("WorkspaceState.Ready", xaml, StringComparison.Ordinal);
        Assert.Contains("WorkspaceState.Running", xaml, StringComparison.Ordinal);
        Assert.Contains("WorkspaceState.Completed", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"{Binding IsDetailsExpanded}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Drop=\"DropTarget_OnDrop\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DragOver=\"DropTarget_OnDragOver\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=", xaml, StringComparison.Ordinal);
    }

    private static void AssertResource(
        XDocument document,
        string key,
        string expectedValue)
    {
        XElement resource = document.Root!
            .Elements()
            .Single(element => (string?)element.Attribute(Xaml + "Key") == key);
        Assert.Equal(expectedValue, resource.Value.Trim());
    }

    private static void AssertBrushResource(
        XDocument document,
        string key,
        string colorKey)
    {
        XElement resource = document.Root!
            .Elements()
            .Single(element => (string?)element.Attribute(Xaml + "Key") == key);
        Assert.Equal(
            $"{{StaticResource {colorKey}}}",
            (string?)resource.Attribute("Color"));
    }

    private static XElement FindStyle(XDocument document, string key) =>
        document.Root!
            .Elements(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == key);

    private static void AssertSetter(
        XElement style,
        string property,
        string expectedValue)
    {
        XElement setter = style
            .Elements(Presentation + "Setter")
            .Single(element => (string?)element.Attribute("Property") == property);
        Assert.Equal(expectedValue, (string?)setter.Attribute("Value"));
    }

    private static void AssertFocusBorders(
        XElement style,
        string outerBrush,
        string innerBrush)
    {
        XElement[] borders = style
            .Descendants(Presentation + "Border")
            .ToArray();

        Assert.Equal(2, borders.Length);
        Assert.Equal(outerBrush, (string?)borders[0].Attribute("BorderBrush"));
        Assert.Equal(innerBrush, (string?)borders[1].Attribute("BorderBrush"));
        Assert.All(
            borders,
            border =>
            {
                string margin = (string?)border.Attribute("Margin") ?? "0";
                Assert.DoesNotContain("-", margin, StringComparison.Ordinal);
                Assert.NotEqual("0", (string?)border.Attribute("BorderThickness"));
            });
    }

    private static string FromRoot(params string[] components) =>
        Path.Combine([Root, .. components]);

    [GeneratedRegex(@"[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]")]
    private static partial Regex CjkText();
}
