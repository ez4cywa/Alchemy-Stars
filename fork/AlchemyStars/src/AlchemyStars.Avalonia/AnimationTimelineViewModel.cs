using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace AlchemyStars.Avalonia;

internal readonly record struct AnimationTimelineSpan(int StartFrame, int DurationFrames);
internal readonly record struct AnimationTimelinePlacement(int LeadingFrames, int DurationFrames, int TrailingFrames);

internal static class AnimationTimelineLayout
{
    public static AnimationTimelinePlacement[] Calculate(IReadOnlyList<AnimationTimelineSpan> spans)
    {
        if (spans.Count == 0)
            return [];

        var firstFrame = Math.Min(0, spans.Min(span => span.StartFrame));
        var lastFrameExclusive = Math.Max(1, spans.Max(span => span.StartFrame + Math.Max(1, span.DurationFrames)));
        return spans.Select(span =>
        {
            var duration = Math.Max(1, span.DurationFrames);
            return new AnimationTimelinePlacement(
                Math.Max(0, span.StartFrame - firstFrame),
                duration,
                Math.Max(0, lastFrameExclusive - span.StartFrame - duration));
        }).ToArray();
    }
}

public sealed class AnimationTrackItem : INotifyPropertyChanged, IDisposable
{
    private readonly WorkspaceAnimation? animation;
    private readonly WorkspaceLayer? layer;
    private float targetFramerate;
    private UiText text;
    private int frameCount = 1;
    private bool metadataKnown;
    private bool loading;
    private GridLength leadingLength = new(0, GridUnitType.Star);
    private GridLength durationLength = new(1, GridUnitType.Star);
    private GridLength trailingLength = new(0, GridUnitType.Star);

    internal AnimationTrackItem(WorkspaceAnimation animation, UiText text)
    {
        this.animation = animation;
        targetFramerate = animation.OutputFramerate;
        this.text = text;
        animation.PropertyChanged += SourcePropertyChanged;
    }

    internal AnimationTrackItem(WorkspaceLayer layer, float targetFramerate, UiText text)
    {
        this.layer = layer;
        this.targetFramerate = targetFramerate;
        this.text = text;
        layer.PropertyChanged += SourcePropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event EventHandler<string?>? SourceChanged;

    internal WorkspaceLayer? Layer => layer;
    internal float TargetFramerate => targetFramerate;
    public bool IsBase => animation is not null;
    public bool IsLayer => layer is not null;
    public string SourcePath => animation?.Name ?? layer?.Name ?? string.Empty;
    public string DisplayName => animation?.DisplayName ?? layer?.DisplayName ?? string.Empty;
    public string TrackLabel => IsBase ? text.BaseAnimation : DisplayName;
    public int StartFrame => IsBase ? 0 : layer?.Offset ?? 0;
    public int FrameCount => frameCount;
    public bool IsMetadataKnown => metadataKnown;
    public bool IsLoading => loading;
    public string FrameLabel => loading ? text.ReadingFrames : metadataKnown ? text.FrameCount(frameCount) : text.FrameCountUnavailable;
    public string OffsetLabel => IsBase ? "—" : StartFrame.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string AccessibleName => metadataKnown
        ? text.TrackAccessibleName(TrackLabel, StartFrame, frameCount)
        : text.TrackMetadataUnavailable(TrackLabel);
    public string Tooltip => metadataKnown
        ? text.TrackTooltip(SourcePath, StartFrame, frameCount)
        : text.TrackMetadataUnavailable(SourcePath);
    public GridLength LeadingLength => leadingLength;
    public GridLength DurationLength => durationLength;
    public GridLength TrailingLength => trailingLength;

    internal void SetText(UiText value)
    {
        text = value;
        RaiseLabels();
    }

    internal void SetLoading()
    {
        loading = true;
        metadataKnown = false;
        frameCount = 1;
        RaiseMetadata();
    }

    internal void SetTargetFramerate(float value) => targetFramerate = value;

    internal void SetMetadata(AnimationClipMetadata metadata)
    {
        loading = false;
        metadataKnown = true;
        frameCount = Math.Max(1, metadata.FrameCount);
        RaiseMetadata();
    }

    internal void SetMetadataUnavailable()
    {
        loading = false;
        metadataKnown = false;
        frameCount = 1;
        RaiseMetadata();
    }

    internal void SetPlacement(AnimationTimelinePlacement placement)
    {
        leadingLength = new GridLength(placement.LeadingFrames, GridUnitType.Star);
        durationLength = new GridLength(placement.DurationFrames, GridUnitType.Star);
        trailingLength = new GridLength(placement.TrailingFrames, GridUnitType.Star);
        Changed(nameof(LeadingLength));
        Changed(nameof(DurationLength));
        Changed(nameof(TrailingLength));
    }

    public void Dispose()
    {
        if (animation is not null)
            animation.PropertyChanged -= SourcePropertyChanged;
        if (layer is not null)
            layer.PropertyChanged -= SourcePropertyChanged;
    }

    private void SourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceAnimation.Name) or nameof(WorkspaceAnimation.OutputName)
            or nameof(WorkspaceLayer.Name))
        {
            Changed(nameof(SourcePath));
            Changed(nameof(DisplayName));
            Changed(nameof(TrackLabel));
            RaiseLabels();
        }
        if (e.PropertyName == nameof(WorkspaceLayer.Offset))
        {
            Changed(nameof(StartFrame));
            Changed(nameof(OffsetLabel));
            RaiseLabels();
        }
        SourceChanged?.Invoke(this, e.PropertyName);
    }

    private void RaiseMetadata()
    {
        Changed(nameof(FrameCount));
        Changed(nameof(IsMetadataKnown));
        Changed(nameof(IsLoading));
        RaiseLabels();
    }

    private void RaiseLabels()
    {
        Changed(nameof(FrameLabel));
        Changed(nameof(AccessibleName));
        Changed(nameof(Tooltip));
    }

    private void Changed([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class AnimationTimelineViewModel : INotifyPropertyChanged, IDisposable
{
    private WorkspaceAnimation? animation;
    private AnimationTrackItem? baseTrack;
    private UiText text;
    private int generation;
    private int firstFrame;
    private int lastFrame;
    private bool disposed;

    public AnimationTimelineViewModel(UiText text) => this.text = text;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<AnimationTrackItem> LayerTracks { get; } = [];
    public AnimationTrackItem? BaseTrack { get => baseTrack; private set { baseTrack = value; Changed(); } }
    public string ScaleLabel => text.FrameRange(firstFrame, lastFrame);

    public UiText Text
    {
        set
        {
            text = value;
            BaseTrack?.SetText(value);
            foreach (var item in LayerTracks)
                item.SetText(value);
            Changed(nameof(ScaleLabel));
        }
    }

    public void SetAnimation(WorkspaceAnimation? value)
    {
        if (ReferenceEquals(animation, value))
            return;

        generation++;
        if (animation is not null)
            animation.Layers.CollectionChanged -= LayersCollectionChanged;
        if (BaseTrack is { } previousBase)
        {
            previousBase.SourceChanged -= TrackSourceChanged;
            previousBase.Dispose();
        }
        foreach (var item in LayerTracks)
        {
            item.SourceChanged -= TrackSourceChanged;
            item.Dispose();
        }
        LayerTracks.Clear();
        animation = value;
        BaseTrack = value is null ? null : Create(value);
        if (value is not null)
        {
            value.Layers.CollectionChanged += LayersCollectionChanged;
            SynchronizeLayers();
        }
        Recalculate();
    }

    public AnimationTrackItem? FindTrack(WorkspaceLayer? layer) =>
        layer is null ? null : LayerTracks.FirstOrDefault(item => ReferenceEquals(item.Layer, layer));

    public void Dispose()
    {
        disposed = true;
        generation++;
        if (animation is not null)
            animation.Layers.CollectionChanged -= LayersCollectionChanged;
        if (BaseTrack is { } currentBase)
        {
            currentBase.SourceChanged -= TrackSourceChanged;
            currentBase.Dispose();
        }
        foreach (var item in LayerTracks)
        {
            item.SourceChanged -= TrackSourceChanged;
            item.Dispose();
        }
    }

    private AnimationTrackItem Create(WorkspaceAnimation value)
    {
        var item = new AnimationTrackItem(value, text);
        item.SourceChanged += TrackSourceChanged;
        LoadMetadata(item);
        return item;
    }

    private AnimationTrackItem Create(WorkspaceLayer value)
    {
        var item = new AnimationTrackItem(value, animation?.OutputFramerate ?? 30f, text);
        item.SourceChanged += TrackSourceChanged;
        LoadMetadata(item);
        return item;
    }

    private void LayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SynchronizeLayers();

    private void SynchronizeLayers()
    {
        if (animation is null)
            return;

        foreach (var item in LayerTracks.Where(item => item.Layer is null || !animation.Layers.Contains(item.Layer)).ToArray())
        {
            item.SourceChanged -= TrackSourceChanged;
            item.Dispose();
            LayerTracks.Remove(item);
        }

        for (var index = 0; index < animation.Layers.Count; index++)
        {
            var layer = animation.Layers[index];
            var current = LayerTracks.FirstOrDefault(item => ReferenceEquals(item.Layer, layer));
            if (current is null)
                LayerTracks.Insert(index, Create(layer));
            else
            {
                var oldIndex = LayerTracks.IndexOf(current);
                if (oldIndex != index)
                    LayerTracks.Move(oldIndex, index);
            }
        }
        Recalculate();
        Changed(nameof(LayerTracks));
    }

    private void TrackSourceChanged(object? sender, string? propertyName)
    {
        if (sender is not AnimationTrackItem item)
            return;
        if (propertyName is nameof(WorkspaceAnimation.Name) or nameof(WorkspaceLayer.Name))
            LoadMetadata(item);
        if (propertyName == nameof(WorkspaceAnimation.OutputFramerate) && animation is not null)
        {
            generation++;
            var rate = animation.OutputFramerate;
            BaseTrack?.SetTargetFramerate(rate);
            foreach (var track in LayerTracks) track.SetTargetFramerate(rate);
            if (BaseTrack is not null) LoadMetadata(BaseTrack);
            foreach (var track in LayerTracks) LoadMetadata(track);
        }
        if (propertyName == nameof(WorkspaceLayer.Offset))
            Recalculate();
    }

    private async void LoadMetadata(AnimationTrackItem item)
    {
        var requestGeneration = generation;
        var sourcePath = item.SourcePath;
        item.SetLoading();
        Recalculate();
        AnimationClipMetadata? metadata = null;
        try
        {
            metadata = await Task.Run(() => AnimationClipMetadataReader.Read(sourcePath, item.TargetFramerate));
        }
        catch (Exception)
        {
            // Missing or malformed sources stay visible with an explicit unknown-duration label.
        }

        if (disposed || requestGeneration != generation
            || !string.Equals(sourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase)
            || (!ReferenceEquals(item, BaseTrack) && !LayerTracks.Contains(item)))
            return;
        if (metadata is { } value)
            item.SetMetadata(value);
        else
            item.SetMetadataUnavailable();
        Recalculate();
    }

    private void Recalculate()
    {
        var items = BaseTrack is null ? LayerTracks.ToArray() : [BaseTrack, .. LayerTracks];
        var spans = items.Select(item => new AnimationTimelineSpan(item.StartFrame, item.FrameCount)).ToArray();
        var placements = AnimationTimelineLayout.Calculate(spans);
        for (var index = 0; index < items.Length; index++)
            items[index].SetPlacement(placements[index]);

        firstFrame = items.Length == 0 ? 0 : Math.Min(0, items.Min(item => item.StartFrame));
        lastFrame = items.Length == 0 ? 0 : Math.Max(0, items.Max(item => item.StartFrame + item.FrameCount - 1));
        Changed(nameof(ScaleLabel));
    }

    private void Changed([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
