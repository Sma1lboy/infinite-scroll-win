using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using InfiniteScroll.Models;

namespace InfiniteScroll.Converters;

public class StatusColorConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new(Colors.Green);
    private static readonly SolidColorBrush GrayBrush = new(Colors.Gray);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 1 && values[0] is ObservableCollection<CellModel> cells)
        {
            var anyRunning = cells.Any(c => c.Type == CellType.Terminal && c.IsRunning);
            return anyRunning ? GreenBrush : GrayBrush;
        }
        return GrayBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
