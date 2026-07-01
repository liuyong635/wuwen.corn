using System;
using System.IO;

namespace CreateMissingFiles
{
    class Program
    {
        static void Main(string[] args)
        {
            string missingFilesPath = @"d:\Share\project\wuwen.corn-master\missing_files.txt";
            byte[] bmpData = new byte[] {
                0x42,0x4D,0x36,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x36,0x00,0x00,0x00,
                0x28,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,0x00,
                0x18,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xFF,0x00
            };

            string[] lines = File.ReadAllLines(missingFilesPath);
            int count = 0;

            foreach (string line in lines)
            {
                string filePath = line.Trim();
                if (string.IsNullOrEmpty(filePath)) continue;

                try
                {
                    if (!File.Exists(filePath))
                    {
                        string directory = Path.GetDirectoryName(filePath);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        File.WriteAllBytes(filePath, bmpData);
                        count++;
                        Console.WriteLine($"Created: {filePath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating {filePath}: {ex.Message}");
                }
            }

            Console.WriteLine($"Total created: {count}");
        }
    }
}