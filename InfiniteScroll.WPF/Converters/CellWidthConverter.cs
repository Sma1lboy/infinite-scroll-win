using System.Globalization;
using System.Windows.Data;

namespace InfiniteScroll.Converters;

public class CellWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is double totalWidth && values[1] is int count && count > 0)
        {
            var dividers = count - 1;
            return Math.Max(0, (totalWidth - dividers) / count);
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
