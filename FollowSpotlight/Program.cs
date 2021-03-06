using Accord.Video.FFMPEG;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Wolf;

namespace FollowSpotlight
{
    class Program
    {
        private static WolframLink wolf;

        static int Main(string[] args)
        {
            if (args.Length == 0)
                return 0;

            int indexOfArg = Array.IndexOf(args, "-arg");
            List<string> inputs = args.ToList().GetRange(indexOfArg + 1, args.Length - indexOfArg - 1);

            wolf = new WolframLink();
            List<Tuple<string, double>> outputs = new List<Tuple<string, double>>();
            for (int i = 0; i < inputs.Count; i++)
            {
                string input = inputs[i];
                Console.WriteLine("Processing " + input);

                string ext = Path.GetExtension(input).ToLower();
                if (new string[] { ".bmp", ".jpeg", ".jpg", ".png" }.Contains(ext))
                {
                    Bitmap bmp = new Bitmap(input);
                    outputs.Add(new Tuple<string, double>(input, ProcessFrameMode2(ref bmp)));
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
                        outputs.Add(new Tuple<string, double>(input + "_frame" + f.ToString(), ProcessFrameMode2(ref bmp)));

                        Console.Write(Math.Round((float)f * 100.0 / vfr.FrameCount, 2) + "%");
                        Console.CursorLeft = 11;
                    }

                    Console.CursorTop++;
                    Console.CursorLeft = 0;
                }
            }

            outputs.ForEach(x => Console.WriteLine(x.Item1 + "\t" + x.Item2));

            return 0;
        }

        private static double ProcessFrameMode2(ref Bitmap bmp)
        {
            //workaround to loose information from 24bpp to 4bpp
            bmp = bmp
                .Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), PixelFormat.Format4bppIndexed)
                .Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), PixelFormat.Format24bppRgb);

            //get points above the average
            List<Tuple<double, Point>> points = GetLightness(ref bmp);
            double avgBright = points.Average(x => x.Item1);
            List<Point> pointsAbove = points.Where(x => x.Item1 > avgBright).Select(x => x.Item2).ToList();
            int xMax = pointsAbove.Max(x => x.X);
            int xMin = pointsAbove.Min(x => x.X);
            int yMax = pointsAbove.Max(x => x.Y);
            int yMin = pointsAbove.Min(x => x.Y);

            //map points above on the resized position
            pointsAbove = pointsAbove.Select(x => new Point(x.X - xMin, x.Y - yMin)).ToList();
            Bitmap tmp = new Bitmap(xMax - xMin + 3, yMax - yMin + 3);
            Graphics.FromImage(tmp).FillRectangle(new SolidBrush(Color.White), 0, 0, tmp.Width, tmp.Height);
            pointsAbove.ForEach(x => tmp.SetPixel(x.X + 1, x.Y + 1, Color.Black));

            //get borders of the points above
            List<Point> border = GetBorder(ref tmp);
            xMax = border.Max(x => x.X);
            yMax = border.Max(x => x.Y);
            tmp = new Bitmap(xMax + 3, yMax + 3);
            Graphics.FromImage(tmp).FillRectangle(new SolidBrush(Color.White), 0, 0, tmp.Width, tmp.Height);
            border.ForEach(x => tmp.SetPixel(x.X + 1, x.Y + 1, Color.Black));

            List<Point> left = border.Where(x => x.X < tmp.Width / 2).ToList();
            List<Point> right = border.Where(x => x.X >= tmp.Width / 2).ToList();
            List<Point> uLeft = new List<Point>();
            List<Point> uRight = new List<Point>();
            int delta = 15;
            for (int y = 0; y < yMax; y += delta)
            {
                List<Point> ol = left.Where(x => x.Y > y && x.Y <= y + delta).ToList();
                if (ol.Count > 0)
                    uLeft.Add(new Point((int)ol.Average(x => (double)x.X), (int)ol.Average(x => (double)x.Y)));
                List<Point> or = right.Where(x => x.Y > y && x.Y <= y + delta).ToList();
                if (or.Count > 0)
                    uRight.Add(new Point((int)or.Average(x => (double)x.X), (int)or.Average(x => (double)x.Y)));
            }

            uRight.Reverse();
            List<Point> merge = new List<Point>(uLeft);
            merge.AddRange(uRight);
            string str = "data={" + string.Join(", ", merge.Select(x => "{" + x.X + ", " + x.Y + "}")) + "};";
            str += Environment.NewLine + @"ar := Area[Polygon[data]]
per := N[Perimeter[Polygon[data]]]
res := NSolve[{ar == r (Pi r + 2 h), per == 2 (h + Pi r)}, {h, r}, Reals]
h /. res[[1]][[1]]";
            string output = wolf.Wolf(str).Replace('.', ',');

            return double.Parse(output);
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
}