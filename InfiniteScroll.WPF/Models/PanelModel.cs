using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace InfiniteScroll.Models;

public partial class PanelModel : ObservableObject
{
    public Guid Id { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _notesText;

    [ObservableProperty]
    private bool _showNotes;

    public ObservableCollection<CellModel> Cells { get; }

    public PanelModel(int index, Guid? id = null, IEnumerable<CellModel>? cells = null,
        string notesText = "", bool showNotes = false)
    {
        Id = id ?? Guid.NewGuid();
        _title = $"Row #{index}";
        _notesText = notesText;
        _showNotes = showNotes;

        Cells = cells != null
            ? new ObservableCollection<CellModel>(cells)
            : new ObservableCollection<CellModel>(new[] { new CellModel(CellType.Terminal) });

        if (showNotes && !Cells.Any(c => c.Type == CellType.Notes))
        {
            Cells.Add(new CellModel(CellType.Notes, text: notesText));
        }
    }

    public void ToggleNotes()
    {
        var notesCell = Cells.FirstOrDefault(c => c.Type == CellType.Notes);
        if (notesCell != null)
        {
            NotesText = notesCell.Text;
            Cells.Remove(notesCell);
            ShowNotes = false;
        }
        else
        {
            Cells.Add(new CellModel(CellType.Notes, text: NotesText));
            ShowNotes = true;
        }
    }

    public PanelState ToState()
    {
        var currentNotesText = Cells.FirstOrDefault(c => c.Type == CellType.Notes)?.Text ?? NotesText;
        return new PanelState
        {
            Id = Id.ToString(),
            Title = Title,
            Cells = Cells.Select(c => c.ToState()).ToList(),
            NotesText = currentNotesText,
            ShowNotes = ShowNotes
        };
    }

    public static PanelModel FromState(PanelState state, int index)
    {
        var cells = new List<CellModel>();
        if (state.Cells is { Count: > 0 })
        {
            // Filter out notes cells — toggleNotes will re-add if needed
            cells = state.Cells
                .Where(c => c.Type?.ToLowerInvariant() != "notes")
                .Select(CellModel.FromState)
                .ToList();
        }
        else
        {
            // Backward compat
            cells.Add(new CellModel(CellType.Terminal, cwd: state.Cwd));
        }

        Guid.TryParse(state.Id, out var id);
        if (id == Guid.Empty) id = Guid.NewGuid();

        var model = new PanelModel(
            index,
            id,
            cells,
            state.NotesText ?? "",
            state.ShowNotes ?? false
        );
        model.Title = state.Title ?? $"Row #{index}";
        return model;
    }
}

public class PanelState
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public List<CellState>? Cells { get; set; }
    public string? NotesText { get; set; }
    public bool? ShowNotes { get; set; }
    public string? Cwd { get; set; }
}
