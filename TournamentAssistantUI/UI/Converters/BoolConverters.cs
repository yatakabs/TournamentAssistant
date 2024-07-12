using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TournamentAssistantUI.UI.Converters;

public class BoolConverterBase<T> : IValueConverter
{
    public T TrueValue { get; set; }
    public T FalseValue { get; set; }
    public T DefaultValue { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool boolValue
            ? boolValue
                ? this.TrueValue
                : this.FalseValue
            : this.DefaultValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ReverseBoolConverter : BoolConverterBase<bool>
{
    public ReverseBoolConverter()
    {
        this.TrueValue = false;
        this.FalseValue = true;
        this.DefaultValue = false;
    }
}

public class BoolVisibilityConverter : BoolConverterBase<Visibility>
{
}
