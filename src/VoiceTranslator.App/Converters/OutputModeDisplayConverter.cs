using System.Globalization;
using System.Windows.Data;
using VoiceTranslator.Domain.Audio;

namespace VoiceTranslator.App.Converters;

public sealed class OutputModeDisplayConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value switch
        {
            OutputMode.Physical => "Физический выход",
            OutputMode.VirtualCable => "Виртуальный микрофон (Discord/Telegram)",
            OutputMode.Both => "Динамики + виртуальный микрофон",
            _ => value?.ToString() ?? string.Empty,
        };
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
