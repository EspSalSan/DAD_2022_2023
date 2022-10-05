using System;
using System.IO;

namespace Utilities
{
    public static class Common
    {
        public static string GetSolutionDir()
        {
            // Leads to /BoneyBank
            return Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent?.Parent?.Parent?.Parent?.FullName;
        }
    }
}