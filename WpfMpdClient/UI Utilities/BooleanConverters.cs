using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Globalization;
using System.Windows;

namespace UI
{
    public sealed class BooleanToVisibilityConverter : IValueConverter, IMultiValueConverter
    {
        public bool IsReversed { get; set; }
        public bool IsDisjunctive { get; set; }
        public bool UseHidden { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var val = ToBoolean(value, CultureInfo.InvariantCulture);
            if (this.IsReversed)
            {
                val = !val;
            }
            if (val)
            {
                return Visibility.Visible;
            }
            return this.UseHidden ? Visibility.Hidden : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return Convert(
                values != null && (
                    IsDisjunctive
                    ? values.OfType<System.IConvertible>().Any(ToBoolean)
                    : values.OfType<System.IConvertible>().All(ToBoolean)
                ),
                targetType, parameter, culture);
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public static bool ToBoolean(object value)
        {
            return ToBoolean(value, CultureInfo.InvariantCulture);
        }
        public static bool ToBoolean(object value, CultureInfo culture)
        {
            if (value is Visibility)
                return ((Visibility)value) == Visibility.Visible;
            else
                return System.Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
    }
    public sealed class BooleanToIntegerConverter : IValueConverter
    {
        public int TrueValue { get; set; }
        public int FalseValue { get; set; }
        public BooleanToIntegerConverter() { TrueValue = 1; FalseValue = 0; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return BooleanToVisibilityConverter.ToBoolean(value, CultureInfo.InvariantCulture) ? TrueValue : FalseValue;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is int && ((int)value) == TrueValue;
        }
    }
    public sealed class BooleanToValueConverter : IValueConverter
    {
        public object TrueValue { get; set; }
        public object FalseValue { get; set; }
        public BooleanToValueConverter() { TrueValue = 1; FalseValue = 0; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return BooleanToVisibilityConverter.ToBoolean(value, CultureInfo.InvariantCulture) ? TrueValue : FalseValue;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == TrueValue;
        }
    }
    public sealed class ValueToBooleanConverter : IValueConverter
    {
        public object TrueValue { get; set; }
        public object FalseValue { get; set; }
        public ValueToBooleanConverter() { TrueValue = 1; FalseValue = 0; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (parameter ?? TrueValue).Equals(value);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return BooleanToVisibilityConverter.ToBoolean(value, CultureInfo.InvariantCulture) ? parameter ?? TrueValue : FalseValue;
        }
    }
}
