using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace AlchemyStars.Avalonia;

public sealed class CastPreviewViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch clock = new();
    private CastPreviewScene? scene;
    private CastPreviewFrame? frameData;
    private PreviewCamera camera = PreviewCamera.Default;
    private UiText text;
    private int generation, renderRevision, width = 640, height = 360;
    private bool rendering, disposed, loading, playing, showBones;
    private double frame, startFrame;
    private string source = string.Empty, error = string.Empty;

    public CastPreviewViewModel(UiText text)
    {
        this.text = text;
        timer.Tick += (_, _) =>
        {
            if (scene is null || !playing) return;
            Frame = (startFrame + clock.Elapsed.TotalSeconds * scene.Framerate) % scene.FrameCount;
        };
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public UiText Text { get => text; set { text = value; Changed(); RefreshLabels(); } }
    public CastPreviewFrame? FrameData => frameData;
    public bool HasScene => scene is not null;
    public bool IsLoading { get => loading; private set { loading = value; Changed(); Changed(nameof(Status)); } }
    public bool IsPlaying => playing;
    public bool CanPlay => scene is { FrameCount: > 1 };
    public int LastFrame => Math.Max(0, (scene?.FrameCount ?? 1) - 1);
    public double PreviewTickFrequency => LastFrame switch
    {
        <= 30 => 1,
        <= 120 => 5,
        <= 300 => 10,
        <= 600 => 25,
        _ => 50,
    };
    public double Frame { get => frame; set { frame = Math.Clamp(value, 0, LastFrame); Changed(); Changed(nameof(FrameLabel)); RequestRender(); } }
    public string FrameLabel => $"{(int)Frame} / {LastFrame}";
    public string Source => source;
    public string Status => IsLoading ? Text.PreviewLoading : error.Length > 0 ? error : scene is null ? Text.PreviewEmpty : (scene.UsesProjectSkeleton ? Text.ProjectSkeletonPreview + " · " : "") + string.Format(Text.PreviewStats, scene.VertexCount, scene.BoneCount, scene.Framerate);
    public string PlayLabel => playing ? Text.PausePreview : Text.PlayPreview;
    public bool ShowBones { get => showBones; set { showBones = value; Changed(); RequestRender(); } }
    public bool IsFirstPerson => camera.Mode == PreviewCameraMode.FirstPerson;
    public bool CanAdjustCamera => HasScene && !IsFirstPerson;
    public float CameraYaw => camera.Yaw;
    public float CameraPitch => camera.Pitch;
    public string SceneUpAxis => scene?.UpAxis ?? "z";
    public string CameraModeLabel => IsFirstPerson ? Text.ExitFirstPersonView : Text.FirstPersonView;
    public string InteractionHelp => IsFirstPerson ? Text.FirstPersonPreviewHelp : Text.PreviewHelp;
    public string FirstPersonBadge => Text.FirstPersonBadge;

    public async Task LoadAsync(string path, string? label = null, IReadOnlyList<ModelPartSpec>? parts = null, bool legacy = false, string? upAxisOverride = null)
    {
        Clear();
        var version = generation;
        IsLoading = true;
        try
        {
            var loaded = await Task.Run(() => CastPreviewScene.Load(path, parts, legacy, upAxisOverride));
            if (version != generation || disposed) return;
            scene = loaded;
            renderRevision++;
            source = label ?? path;
            camera = PreviewCamera.Default;
            showBones = loaded.VertexCount == 0;
            Changed(nameof(Source)); Changed(nameof(ShowBones)); RefreshCameraState(); RefreshLabels();
            await RenderAsync();
        }
        catch (Exception exception)
        {
            if (version != generation || disposed) return;
            error = Text.PreviewFailed + ": " + exception.Message;
            Changed(nameof(Status));
        }
        finally { if (version == generation && !disposed) IsLoading = false; }
    }

    public void Clear()
    {
        generation++;
        renderRevision++;
        Pause();
        scene = null; frame = 0; source = string.Empty; error = string.Empty; camera = PreviewCamera.Default; IsLoading = false;
        frameData = null; Changed(nameof(FrameData));
        Changed(nameof(Source)); RefreshCameraState(); RefreshLabels();
    }

    public void SetViewportSize(double w, double h)
    {
        if (w < 1 || h < 1) return;
        // Cap software-rendering work while preserving the viewport's aspect ratio.
        var scale = Math.Min(1, Math.Min(960 / w, 640 / h));
        var newWidth = Math.Max(32, (int)(w * scale));
        var newHeight = Math.Max(32, (int)(h * scale));
        if (newWidth == width && newHeight == height) return;
        width = newWidth; height = newHeight; RequestRender();
    }
    public void TogglePlayback()
    {
        if (playing) { Pause(); return; }
        if (!CanPlay) return;
        playing = true; startFrame = Frame; clock.Restart(); timer.Start(); RefreshLabels();
    }
    public void Pause() { playing = false; timer.Stop(); clock.Stop(); RefreshLabels(); }
    public void Step(int delta) { Pause(); Frame = Math.Clamp(Math.Floor(Frame) + delta, 0, LastFrame); }
    public void Fit() => SetCamera(PreviewCamera.Default);
    public void FitAll() => SetCamera(PreviewCamera.Default with { AllGeometry = true, Zoom = 1 });
    public void ToggleFirstPerson() => SetCamera(IsFirstPerson ? PreviewCamera.Default : PreviewCamera.FirstPerson);
    public void Orbit(double x, double y)
    {
        if (IsFirstPerson) return;
        camera = camera with { Yaw = camera.Yaw + (float)x * 0.01f, Pitch = Math.Clamp(camera.Pitch + (float)y * 0.01f, -1.55f, 1.55f) };
        RefreshCameraState();
        RequestRender();
    }
    public void SetAxisView(char axis, bool positive)
    {
        if (scene is null || IsFirstPerson) return;
        SetCamera(CastPreviewRenderer.SnapToAxis(camera, scene.UpAxis, axis, positive));
    }
    public void Zoom(double amount)
    {
        if (IsFirstPerson) return;
        camera = camera with { Zoom = Math.Clamp(camera.Zoom * MathF.Pow(1.15f, (float)amount), 0.15f, 8) };
        RequestRender();
    }
    private void SetCamera(PreviewCamera value)
    {
        camera = value;
        Changed(nameof(IsFirstPerson));
        Changed(nameof(CanAdjustCamera));
        Changed(nameof(CameraModeLabel));
        Changed(nameof(InteractionHelp));
        Changed(nameof(FirstPersonBadge));
        RefreshCameraState();
        RequestRender();
    }
    private void RefreshCameraState()
    {
        Changed(nameof(CameraYaw));
        Changed(nameof(CameraPitch));
        Changed(nameof(SceneUpAxis));
    }
    private void RequestRender() { renderRevision++; _ = RenderAsync(); }

    // At most one scene is sampled/rasterized at a time; requests coalesce to the newest frame.
    private async Task RenderAsync()
    {
        if (rendering || disposed || scene is null) return;
        rendering = true;
        try
        {
            int revision;
            do
            {
                revision = renderRevision;
                var version = generation;
                var current = scene;
                if (current is null || disposed) break;
                var w = width; var h = height; var f = (float)frame; var view = camera; var bones = ShowBones;
                var prepared = await Task.Run(() => CastPreviewRenderer.Prepare(current, f, w, h, view, bones));
                if (disposed || version != generation) continue;
                frameData = prepared; Changed(nameof(FrameData));
            } while (revision != renderRevision && !disposed);
        }
        catch (Exception exception) { Pause(); error = Text.PreviewFailed + ": " + exception.Message; Changed(nameof(Status)); }
        finally { rendering = false; }
    }
    private void RefreshLabels()
    {
        foreach (var name in new[]
        {
            nameof(HasScene), nameof(CanPlay), nameof(LastFrame), nameof(Frame), nameof(FrameLabel), nameof(Status),
            nameof(PreviewTickFrequency), nameof(IsPlaying), nameof(PlayLabel), nameof(IsFirstPerson), nameof(CanAdjustCamera), nameof(CameraModeLabel),
            nameof(InteractionHelp), nameof(FirstPersonBadge),
        }) Changed(name);
    }
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    public void Dispose() { disposed = true; Clear(); timer.Stop(); }
}
