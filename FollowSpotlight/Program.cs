﻿using Accord.Video.FFMPEG;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace FollowSpotlight
{
    class Program
    {
        static int NotGood()
        {
            Console.WriteLine("Usage example: " + AppDomain.CurrentDomain.FriendlyName + " -arg video1 [video2 ...] -mode [1..2]");
            Console.WriteLine("-mode 1 to track spotlight");
            Console.WriteLine("-mode 2 to measure spotlight");
            return 0;
        }

        static int Main(string[] args)
        {
            if (args.Length == 0)
                return NotGood();
            
            int indexOfArg = Array.IndexOf(args, "-arg");
            int indexOfMode = Array.IndexOf(args, "-mode");
            if (indexOfMode < indexOfArg)
                return NotGood();
            List<string> inputs = args.ToList().GetRange(indexOfArg + 1, indexOfMode - indexOfArg - 1);
            string modeStr = args.ToList().GetRange(indexOfMode + 1, 1)[0];
            if (!int.TryParse(modeStr, out int mode))
                return NotGood();

            List<Point> avgPeak = new List<Point>();
            List<double> peak = new List<double>();
            for (int i = 0; i < inputs.Count; i++)
            {
                string input = inputs[i];
                Console.WriteLine("Processing " + input);

                string ext = Path.GetExtension(input).ToLower();
                if (new string[] { ".bmp", ".jpeg", ".jpg", ".png" }.Contains(ext))
                {
                    Bitmap bmp = new Bitmap(input);
                    if (mode == 1)
                        avgPeak.Add(ProcessFrameMode1(ref bmp));
                    else if (mode == 2)
                        peak.Add(ProcessFrameMode2(ref bmp));
                }
                else if (new string[] { ".avi" }.Contains(ext))
                {
                    VideoFileReader vfr = new VideoFileReader();
                    vfr.Open(input);
                    Console.WriteLine("Video name: " + input);
                    Console.WriteLine("Height: " + vfr.Height);
                    Console.WriteLine("Width: " + vfr.Width);
                    Console.WriteLine("Bitrate: " + vfr.BitRate);
                    Console.WriteLine("CodecName: " + vfr.CodecName);
                    Console.WriteLine("Number of frames: " + vfr.FrameCount);
                    Console.WriteLine("Frame rate: " + vfr.FrameRate);
                    Console.Write("Loading... ");

                    for (int f = 0; f < vfr.FrameCount; f++)
                    {
                        Bitmap bmp = vfr.ReadVideoFrame(f);
                        if (mode == 1)
                            avgPeak.Add(ProcessFrameMode1(ref bmp));
                        else if (mode == 2)
                            peak.Add(ProcessFrameMode2(ref bmp));

                        Console.Write(Math.Round((float)f * 100.0 / vfr.FrameCount, 2) + "%");
                        Console.CursorLeft = 11;
                    }

                    Console.CursorTop++;
                    Console.CursorLeft = 0;
                }

                if (mode == 1)
                {
                    string dump = "deltax(px) " + (avgPeak.Max(x => x.X) - avgPeak.Min(x => x.X)) + Environment.NewLine;
                    dump += "deltay(px) " + (avgPeak.Max(x => x.Y) - avgPeak.Min(x => x.Y)) + Environment.NewLine;
                    Console.WriteLine(dump);
                    dump += string.Join(Environment.NewLine, avgPeak.Select(x => x.X + " " + x.Y));
                    string path = Path.GetDirectoryName(input) + Path.GetFileNameWithoutExtension(input) + ".txt";
                    File.WriteAllText(path, dump);
                }
                else if (mode == 2)
                {
                    peak.ForEach(x => Console.WriteLine(x));
                    string dump = string.Join(Environment.NewLine, peak);
                    string path = Path.GetDirectoryName(input) + "\\" + Path.GetFileNameWithoutExtension(input) + ".txt";
                    File.WriteAllText(path, dump);
                }
            }

            return 0;
        }

        private static Point ProcessFrameMode1(ref Bitmap bmp)
        {
            List<Tuple<double, Point>> points = GetLightness(ref bmp);
            double avgBright = points.Average(x => x.Item1);
            List<Point> pointsAbove = points.Where(x => x.Item1 > avgBright).Select(x => x.Item2).ToList();

            int avgX = (int)pointsAbove.Average(x => (double)x.X);
            int avgY = (int)pointsAbove.Average(x => (double)x.Y);
            return new Point(avgX, avgY);
        }

        private static double ProcessFrameMode2(ref Bitmap bmp)
        {
            List<Tuple<double, Point>> points = GetLightness(ref bmp);
            double avgBright = points.Average(x => x.Item1);
            List<Point> pointsAbove = points.Where(x => x.Item1 > avgBright).Select(x => x.Item2).ToList();
            int xMax = pointsAbove.Max(x => x.X);
            int xMin = pointsAbove.Min(x => x.X);
            int yMax = pointsAbove.Max(x => x.Y);
            int yMin = pointsAbove.Min(x => x.Y);
            pointsAbove = pointsAbove.Select(x => new Point(x.X - xMin, x.Y - yMin)).ToList();

            Bitmap tmp = new Bitmap(xMax - xMin + 3, yMax - yMin + 3);
            Graphics.FromImage(tmp).FillRectangle(new SolidBrush(Color.White), 0, 0, tmp.Width, tmp.Height);
            pointsAbove.ForEach(x => tmp.SetPixel(x.X + 1, x.Y + 1, Color.Black));

            List<Point> border = GetBorder(ref tmp);
            //for (int x = 1; x < tmp.Width - 1; x++)
            //    for (int y = 1; y < tmp.Height - 1; y++)
            //        if ((tmp.GetPixel(x, y).Name == "ff000000" && tmp.GetPixel(x, y - 1).Name == "ffffffff") ||
            //                (tmp.GetPixel(x, y).Name == "ff000000" && tmp.GetPixel(x - 1, y).Name == "ffffffff") ||
            //                (tmp.GetPixel(x, y).Name == "ff000000" && tmp.GetPixel(x, y + 1).Name == "ffffffff") ||
            //                (tmp.GetPixel(x, y).Name == "ff000000" && tmp.GetPixel(x + 1, y).Name == "ffffffff"))
            //            border.Add(new Point(x, y));
            double middle = (border.Max(x => (double)x.X) - border.Min(x => (double)x.X)) / 2.0;
            double distanceFromMiddle = border.Average(x => x.Distance(new Point((int)middle, x.Y)));
            double peak = border.Max(x => x.Y) - border.Min(x => x.Y) - 2 * distanceFromMiddle;

            return peak;
        }

        public static List<Tuple<double, Point>> GetLightness(ref Bitmap img)
        {
            var width = img.Width;
            var height = img.Height;
            var bppModifier = img.PixelFormat == PixelFormat.Format24bppRgb ? 3 : 4;

            var srcData = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly, img.PixelFormat);
            var stride = srcData.Stride;
            var scan0 = srcData.Scan0;
            double lum = 0;

            List<Tuple<double, Point>> points = new List<Tuple<double, Point>>();

            unsafe
            {
                byte* p = (byte*)(void*)scan0;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = (y * stride) + x * bppModifier;
                        lum = (0.2989 * p[idx + 2] + 0.587 * p[idx + 1] + 0.114 * p[idx]);
                        points.Add(new Tuple<double, Point>(lum, new Point(x, y)));
                    }
                }
            }

            img.UnlockBits(srcData);

            return points;
        }

        public static List<Point> GetBorder(ref Bitmap img)
        {
            var width = img.Width;
            var height = img.Height;
            var bppModifier = img.PixelFormat == PixelFormat.Format24bppRgb ? 3 : 4;

            var srcData = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly, img.PixelFormat);
            var stride = srcData.Stride;
            var scan0 = srcData.Scan0;

            List<Point> points = new List<Point>();

            unsafe
            {
                byte* p = (byte*)(void*)scan0;

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        int idx = (y * stride) + x * bppModifier;
                        if ((p[idx] == 0 && p[((y - 1) * stride) + x * bppModifier] == 255) ||
                            (p[idx] == 0 && p[(y * stride) + (x - 1) * bppModifier] == 255) ||
                            (p[idx] == 0 && p[((y + 1) * stride) + x * bppModifier] == 255) ||
                            (p[idx] == 0 && p[(y * stride) + (x + 1) * bppModifier] == 255))
                            points.Add(new Point(x, y));
                    }
                }
            }

            img.UnlockBits(srcData);

            return points;
        }
    }

    public static class Ext
    {
        public static double Distance(this Point p1, Point p2)
        {
            return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }
    }
}