using System.Globalization;
using System.Windows.Data;

namespace ProjectLauncher.Desktop.Services;

public sealed class RunningActionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 3 && values[0] is true && values[1] is string active && active.Length > 0 && values[2] is string id && active == id;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
