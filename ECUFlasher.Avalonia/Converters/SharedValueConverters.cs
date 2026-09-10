/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2017  Nefarious Motorsports Inc

Avalonia IValueConverter wrappers around Shared.ConverterLogic.
*/

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Shared;

namespace NefMotoECUFlasher.Avalonia.Converters;

public static class Unset
{
    public static object? Map(object? value)
    {
        return value != null && ReferenceEquals(value, BindingSentinels.UnsetValue)
            ? AvaloniaProperty.UnsetValue
            : value;
    }
}

public class DescriptionAttributeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Unset.Map(DescriptionAttributeConverterLogic.Convert(value!, targetType, parameter!, culture));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class HexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Unset.Map(HexConverterLogic.Convert(value!, targetType, parameter!, culture));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Unset.Map(HexConverterLogic.ConvertBack(value!, targetType, parameter!, culture));
}

public class AsStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AsStringConverterLogic.Convert(value!, targetType, parameter!, culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class StringValidConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Unset.Map(StringValidConverterLogic.Convert(value!, targetType, parameter!, culture));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}

public class TimeSpanSecondsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => TimeSpanSecondsConverterLogic.Convert(value!, targetType, parameter!, culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => TimeSpanSecondsConverterLogic.ConvertBack(value!, targetType, parameter!, culture);
}

public class TimeSpanStringFormatConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => TimeSpanStringFormatConverterLogic.Convert(value!, targetType, parameter!, culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => TimeSpanStringFormatConverterLogic.ConvertBack(value!, targetType, parameter!, culture);
}
