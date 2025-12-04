using System;
using Microsoft.UI.Xaml.Data;

namespace Brainrot.UI
{
    internal sealed class AppIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string processName)
            {
                return ProcessIconProvider.GetIcon(processName);
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
