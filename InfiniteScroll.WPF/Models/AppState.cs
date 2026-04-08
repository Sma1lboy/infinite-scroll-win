namespace InfiniteScroll.Models;

public class AppState
{
    public List<PanelState> Panels { get; set; } = new();
    public int NextIndex { get; set; }
    public double? FontSize { get; set; }
}
