using CommunityToolkit.Mvvm.ComponentModel;

namespace InfiniteScroll.Models;

public enum CellType
{
    Terminal,
    Notes
}

public partial class CellModel : ObservableObject
{
    public Guid Id { get; }
    public CellType Type { get; }

    [ObservableProperty]
    private string _cwd;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _isRunning = true;

    public CellModel(CellType type, Guid? id = null, string? cwd = null, string text = "")
    {
        Id = id ?? Guid.NewGuid();
        Type = type;
        _cwd = cwd ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _text = text;
    }

    public CellState ToState() => new()
    {
        Id = Id.ToString(),
        Type = Type.ToString().ToLowerInvariant(),
        Cwd = Type == CellType.Terminal ? Cwd : null,
        Text = Type == CellType.Notes ? Text : null
    };

    public static CellModel FromState(CellState state)
    {
        var type = state.Type?.ToLowerInvariant() == "notes" ? CellType.Notes : CellType.Terminal;
        Guid.TryParse(state.Id, out var id);
        if (id == Guid.Empty) id = Guid.NewGuid();
        return new CellModel(type, id, state.Cwd, state.Text ?? "");
    }
}

public class CellState
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Cwd { get; set; }
    public string? Text { get; set; }
}
