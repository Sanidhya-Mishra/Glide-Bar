using System.Globalization;
using System.Windows.Data;

namespace GlideBar.App.Converters;

/// <summary>MultiBinding converter: true only when every bound value is boolean true.</summary>
public sealed class AllTrueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values is not null && values.All(v => v is bool b && b);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
