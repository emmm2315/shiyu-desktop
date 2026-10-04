using System;
using System.Drawing;
using System.IO;
namespace DeskSprout
{
    internal static class IconBuilder
    {
        private static void Main(string[] args)
        {
            using(Icon icon=Art.AppIcon())
            using(FileStream stream=File.Create(args[0]))icon.Save(stream);
        }
    }
}
