using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace InfiniteScroll.Converters;

public class FocusBorderConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush FocusBrush = new(Color.FromRgb(0x66, 0x99, 0xFF));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is Guid cellId && values[1] is Guid focusedId)
        {
            return cellId == focusedId ? FocusBrush : TransparentBrush;
        }
        return TransparentBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
