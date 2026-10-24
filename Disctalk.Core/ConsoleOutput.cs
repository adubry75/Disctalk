#nullable disable
using System;
using System.Collections.Generic;

namespace Disctalk
{
    public static class ConsoleOutput
    {
        public static void WriteMulticolorLine(List<(string Text, ConsoleColor Color)> parts)
        {
            foreach (var part in parts)
            {
                Console.ForegroundColor = part.Color;
                Console.Write(part.Text);
            }
            Console.ResetColor(); // Reset the color at the end
            Console.WriteLine();  // Move to the next line
        }
    }
}
