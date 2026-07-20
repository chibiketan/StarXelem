using System;
using System.IO;

namespace StarXelem.Models;

/// <summary>
/// Configuration CLI statique : --screen, --screenshot, --close
/// </summary>
static class AppConfig
{
    public static string? ScreenName { get; set; }
    public static string? ScreenshotPath { get; set; }
    public static bool AutoClose { get; set; }

    public static void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--screen" && i + 1 < args.Length)
            {
                ScreenName = args[++i].ToLowerInvariant();
            }
            else if (arg == "--screenshot" && i + 1 < args.Length)
            {
                ScreenshotPath = Path.GetFullPath(args[++i]);
            }
            else if (arg == "--close")
            {
                AutoClose = true;
            }
        }
    }
}
