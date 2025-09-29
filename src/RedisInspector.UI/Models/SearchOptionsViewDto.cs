using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace RedisInspector.UI.Models
{
    public partial class SearchOptionsViewDto : ObservableObject
    {
        [ObservableProperty] private string? sslHost;
        [ObservableProperty] private string streamsCsv = "";
        [ObservableProperty] private string findField = "";
        [ObservableProperty] private string? findEq;
        [ObservableProperty] private string jsonField = "message";
        [ObservableProperty] private string? jsonPath;
        [ObservableProperty] private int findLast = 0;
        [ObservableProperty] private int findMax = 10;
        [ObservableProperty] private int findPage = 200;
        [ObservableProperty] private bool findCaseInsensitive = false;
        [ObservableProperty] private bool messageOnly = true;
        [ObservableProperty] private bool newestFirst = false;


        public List<string> Streams
        {
            get
            {
                if (string.IsNullOrWhiteSpace(streamsCsv))
                {
                    return new List<string>();
                }
                else
                {
                    var parts = streamsCsv.Split(',', ';', ' ', '\n', '\r');
                    var list = new List<string>();
                    foreach (var part in parts)
                    {
                        var trimmed = part.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            list.Add(trimmed);
                        }
                    }
                    return list;
                };
            }
        }
    }
}
