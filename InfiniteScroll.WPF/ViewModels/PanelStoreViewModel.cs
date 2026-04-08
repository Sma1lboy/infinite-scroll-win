using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfiniteScroll.Models;
using InfiniteScroll.Services;

namespace InfiniteScroll.ViewModels;

public partial class PanelStoreViewModel : ObservableObject
{
    [ObservableProperty]
    private double _fontSize = 16;

    [ObservableProperty]
    private Guid? _focusedCellId;

    [ObservableProperty]
    private bool _showHelp;

    [RelayCommand]
    private void ToggleHelp() => ShowHelp = !ShowHelp;

    private int _focusedRow;
    private int _focusedCell;
    private int _nextIndex = 1;
    private DispatcherTimer? _autosaveTimer;

    public ObservableCollection<PanelModel> Panels { get; } = new();

    public PanelStoreViewModel()
    {
        var saved = PersistenceManager.Load();
        if (saved is { Panels.Count: > 0 })
        {
            _nextIndex = saved.NextIndex;
            FontSize = saved.FontSize ?? 16;
            for (var i = 0; i < saved.Panels.Count; i++)
            {
                Panels.Add(PanelModel.FromState(saved.Panels[i], i + 1));
            }
            _focusedRow = 0;
            _focusedCell = 0;
        }
        else
        {
            AddPanel();
        }

        // Autosave timer (debounced at 2s interval)
        _autosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _autosaveTimer.Tick += (_, _) =>
        {
            _autosaveTimer?.Stop();
            Save();
        };

        Panels.CollectionChanged += (_, _) => ScheduleSave();
    }

    // MARK: - Row operations

    [RelayCommand]
    public void AddPanel()
    {
        var panel = new PanelModel(Panels.Count + 1);
        _nextIndex++;
        var insertAt = Math.Min(_focusedRow + 1, Panels.Count);
        Panels.Insert(insertAt, panel);
        RenumberRows();
        _focusedRow = insertAt;
        _focusedCell = 0;
        ScheduleFocus();
    }

    [RelayCommand]
    public void RemovePanel(Guid id)
    {
        var panel = Panels.FirstOrDefault(p => p.Id == id);
        if (panel != null)
        {
            Panels.Remove(panel);
        }

        if (Panels.Count == 0)
        {
            Save();
            Application.Current.Shutdown();
            return;
        }

        _focusedRow = Math.Min(_focusedRow, Math.Max(Panels.Count - 1, 0));
        ClampCell();
        RenumberRows();
    }

    private void RenumberRows()
    {
        for (var i = 0; i < Panels.Count; i++)
        {
            Panels[i].Title = $"Row #{i + 1}";
        }
    }

    // MARK: - Cell operations

    [RelayCommand]
    public void DuplicateCurrentCell()
    {
        if (_focusedRow >= Panels.Count) return;
        var panel = Panels[_focusedRow];
        if (_focusedCell >= panel.Cells.Count) return;

        var current = panel.Cells[_focusedCell];
        var sourceCwd = current.Type == CellType.Terminal
            ? current.Cwd
            : panel.Cells.LastOrDefault(c => c.Type == CellType.Terminal)?.Cwd
              ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var newCell = new CellModel(CellType.Terminal, cwd: sourceCwd);

        // Insert before notes cell if present
        var notesIdx = -1;
        for (var i = 0; i < panel.Cells.Count; i++)
        {
            if (panel.Cells[i].Type == CellType.Notes)
            {
                notesIdx = i;
                break;
            }
        }

        var insertIdx = notesIdx >= 0 ? notesIdx : _focusedCell + 1;
        panel.Cells.Insert(insertIdx, newCell);
        _focusedCell = insertIdx;
        ScheduleFocus();
    }

    [RelayCommand]
    public void CloseCurrentCell()
    {
        if (_focusedRow >= Panels.Count) return;
        var panel = Panels[_focusedRow];
        if (_focusedCell >= panel.Cells.Count) return;

        panel.Cells.RemoveAt(_focusedCell);

        if (panel.Cells.Count == 0)
        {
            RemovePanel(panel.Id);
            return;
        }

        _focusedCell = Math.Min(_focusedCell, panel.Cells.Count - 1);
        ScheduleFocus();
    }

    // MARK: - Zoom

    [RelayCommand]
    public void ZoomIn()
    {
        FontSize = Math.Min(FontSize + 1, 32);
        ScheduleSave();
    }

    [RelayCommand]
    public void ZoomOut()
    {
        FontSize = Math.Max(FontSize - 1, 8);
        ScheduleSave();
    }

    // MARK: - Focus navigation

    [RelayCommand]
    public void FocusUp()
    {
        if (Panels.Count <= 1) return;
        _focusedRow = (_focusedRow - 1 + Panels.Count) % Panels.Count;
        ClampCell();
        ApplyFocus();
    }

    [RelayCommand]
    public void FocusDown()
    {
        if (Panels.Count <= 1) return;
        _focusedRow = (_focusedRow + 1) % Panels.Count;
        ClampCell();
        ApplyFocus();
    }

    [RelayCommand]
    public void FocusLeft()
    {
        if (_focusedRow >= Panels.Count) return;
        var count = Panels[_focusedRow].Cells.Count;
        if (count <= 1) return;
        _focusedCell = (_focusedCell - 1 + count) % count;
        ApplyFocus();
    }

    [RelayCommand]
    public void FocusRight()
    {
        if (_focusedRow >= Panels.Count) return;
        var count = Panels[_focusedRow].Cells.Count;
        if (count <= 1) return;
        _focusedCell = (_focusedCell + 1) % count;
        ApplyFocus();
    }

    private void ClampCell()
    {
        if (_focusedRow >= Panels.Count) return;
        var count = Panels[_focusedRow].Cells.Count;
        if (_focusedCell >= count)
            _focusedCell = Math.Max(count - 1, 0);
    }

    private void ScheduleFocus()
    {
        Application.Current?.Dispatcher.InvokeAsync(() => ApplyFocus(),
            DispatcherPriority.Background);
    }

    private void ApplyFocus()
    {
        if (_focusedRow >= Panels.Count) return;
        var panel = Panels[_focusedRow];
        if (_focusedCell >= panel.Cells.Count) return;
        FocusedCellId = panel.Cells[_focusedCell].Id;
    }

    public void SetFocusFromCell(Guid cellId)
    {
        for (var row = 0; row < Panels.Count; row++)
        {
            for (var cell = 0; cell < Panels[row].Cells.Count; cell++)
            {
                if (Panels[row].Cells[cell].Id == cellId)
                {
                    _focusedRow = row;
                    _focusedCell = cell;
                    FocusedCellId = cellId;
                    return;
                }
            }
        }
    }

    // MARK: - Persistence

    private void ScheduleSave()
    {
        _autosaveTimer?.Stop();
        _autosaveTimer?.Start();
    }

    public void Save()
    {
        var state = new AppState
        {
            Panels = Panels.Select(p => p.ToState()).ToList(),
            NextIndex = _nextIndex,
            FontSize = FontSize
        };
        PersistenceManager.Save(state);
    }
}
