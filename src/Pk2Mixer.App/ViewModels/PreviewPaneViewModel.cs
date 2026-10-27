using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Pk2Mixer.Core;
using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Graphics;
using Pk2Mixer.App.Services;

namespace Pk2Mixer.App.ViewModels;

public sealed class LngLineViewModel
{
    public LngLineViewModel(LngEntry entry)
    {
        Id = entry.Id.ToString();
        Text = entry.Text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    public string Id { get; }
    public string Text { get; }
}

/// <summary>The third column: preview of the asset selected in either explorer.</summary>
public sealed class PreviewPaneViewModel : ObservableObject
{
    private Bitmap? _image;
    private string _title = "Preview";
    private string _details = "Select an .IMB, .BIN, .DAT or .LNG asset.";
    private string? _text;
    private string? _error;
    private bool _showImbControls;
    private int _groupIndex;
    private bool _sheet;

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Details
    {
        get => _details;
        set => Set(ref _details, value);
    }

    public Bitmap? Image
    {
        get => _image;
        private set
        {
            if (ReferenceEquals(_image, value))
            {
                return;
            }
            var old = _image;
            Set(ref _image, value);
            Raise(nameof(HasImage));
            Raise(nameof(IsEmpty));
            old?.Dispose();
        }
    }

    public bool HasImage => _image is not null;

    public string? Text
    {
        get => _text;
        private set
        {
            if (Set(ref _text, value))
            {
                Raise(nameof(HasText));
                Raise(nameof(IsEmpty));
            }
        }
    }

    public bool HasText => !string.IsNullOrEmpty(_text);

    public string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
                Raise(nameof(IsEmpty));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public bool IsEmpty => !HasImage && !HasText && !HasLng && !HasError;

    public ObservableCollection<LngLineViewModel> LngLines { get; } = [];

    public bool HasLng => LngLines.Count > 0;

    public bool ShowImbControls
    {
        get => _showImbControls;
        private set => Set(ref _showImbControls, value);
    }

    public ObservableCollection<int> GroupNumbers { get; } = [];

    public int GroupIndex
    {
        get => _groupIndex;
        set => Set(ref _groupIndex, value);
    }

    public bool Sheet
    {
        get => _sheet;
        set => Set(ref _sheet, value);
    }

    public bool CanExportPng => ShowImbControls;

    public void Clear()
    {
        Title = "Preview";
        Details = "Select an .IMB, .BIN, .DAT or .LNG asset.";
        Image = null;
        Text = null;
        Error = null;
        LngLines.Clear();
        Raise(nameof(HasLng));
        ShowImbControls = false;
        GroupNumbers.Clear();
        Sheet = false;
    }

    public void ShowError(string title, string error)
    {
        Title = title;
        Details = string.Empty;
        Image = null;
        Text = null;
        LngLines.Clear();
        Raise(nameof(HasLng));
        ShowImbControls = false;
        Error = error;
        Raise(nameof(CanExportPng));
    }

    public void ShowEmpty(string title, string details)
    {
        Clear();
        Title = title;
        Details = details;
    }

    /// <summary>Applies preview content; IMB banks also populate the group selector.</summary>
    public void Apply(PreviewContent content, ImbBank? bank = null, bool keepGroupState = false)
    {
        Title = content.Title;
        Details = content.Details;
        Error = content.Error;
        Image = content.Image is null ? null : BitmapFactory.Create(content.Image);
        Text = content.Kind == PreviewKind.Text ? content.Text : null;

        LngLines.Clear();
        if (content.Lines is not null)
        {
            foreach (var line in content.Lines)
            {
                LngLines.Add(new LngLineViewModel(line));
            }
        }
        Raise(nameof(HasLng));

        var hasBank = bank is { GroupCount: > 0 };
        ShowImbControls = hasBank;
        Raise(nameof(CanExportPng));
        if (!hasBank)
        {
            GroupNumbers.Clear();
            Sheet = false;
            return;
        }

        if (!keepGroupState)
        {
            GroupNumbers.Clear();
            for (var i = 0; i < bank!.GroupCount; i++)
            {
                GroupNumbers.Add(i);
            }
            GroupIndex = Math.Clamp(GroupIndex, 0, bank.GroupCount - 1);
        }
    }

    public void ReplaceImage(RgbaImage? image)
    {
        Image = image is null ? null : BitmapFactory.Create(image);
    }
}
