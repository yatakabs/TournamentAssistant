﻿using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using TournamentAssistantShared;

/*
 * File created by Moon on 1/28/2022
 * https://stackoverflow.com/questions/20707160/data-binding-int-property-to-enum-in-wpf
 */

namespace TournamentAssistantUI.UI.Converters
{
    public class DifficultyEnumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int[])
            {
                return (value as int[]).Select(x => (Constants.BeatmapDifficulty)x).ToList();
            }
            else if (value is int intVaule)
            {
                return (Constants.BeatmapDifficulty)intVaule;
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Constants.BeatmapDifficulty enumValue)
            {
                return (int)enumValue;
            }
            else if (value is null)
            {
                return null;
            }

            return (int)Enum.Parse(typeof(Constants.BeatmapDifficulty), value.ToString());
        }
    }
}
